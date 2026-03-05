using System;
using System.Collections;
using System.Collections.Generic;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Server-authoritative player melee attack requester.
    ///
    /// Client owner:
    /// - Reads input.
    /// - Sends a target suggestion + camera ray to the server.
    ///
    /// Server:
    /// - Validates anti-spoof constraints (cooldown/origin/direction).
    /// - Performs authoritative melee sweep in physics world.
    /// - Picks best valid damageable hit and applies damage.
    /// - Replicates swing animation to everyone.
    ///
    /// Why sweep instead of single ray?
    /// - Melee feels forgiving (wider arc) instead of pixel-perfect pokes.
    ///
    /// Why server-side sweep?
    /// - Clients can suggest hits, but server confirms in trusted physics.
    /// - Prevents spoofed hit points and most hit-through-wall attempts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAttackNet : NetworkBehaviour
    {
        [Header("Melee Sweep")]
        [Min(0.1f)]
        [SerializeField] private float attackRange = 2.5f;

        [Min(0.01f)]
        [SerializeField] private float sweepRadius = 0.45f;

        [Tooltip("Layers considered as melee hit candidates (damageables/hitboxes).")]
        [SerializeField] private LayerMask hitMask;

        [Tooltip("Layers considered as line-of-sight blockers (walls/environment).")]
        [SerializeField] private LayerMask occlusionMask;

        [SerializeField] private bool requireLineOfSight = true;

        [Header("Hit Timing")]
        [Tooltip("Delay between accepted swing and authoritative hit check.")]
        [Min(0f)]
        [SerializeField] private float hitDelaySeconds = 0.15f;

        [Tooltip("Duration of hit window. MVP currently checks once at window start.")]
        [Min(0f)]
        [SerializeField] private float hitWindowSeconds = 0.20f;

        [SerializeField] private bool singleHitPerAttack = true;
        [SerializeField] private bool allowMissSwing = true;

        [Header("Unarmed Fallback")]
        [Min(1)]
        [SerializeField] private int defaultUnarmedDamage = 1;

        [Min(0.01f)]
        [SerializeField] private float defaultUnarmedSwingSpeed = 0.5f;

        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private PlayerEquipmentNet equipmentNet;
        [SerializeField] private PlayerCombatAnimNet playerCombatAnim;
        [SerializeField] private Transform viewOrigin;
        [SerializeField] private SkillsNet skills;

        [Header("Combat XP")]
        [Min(0)]
        [SerializeField] private int xpPerHit = 1;
        [SerializeField] private bool awardXpOnZeroDamage = false;

        [Header("Server Ray Validation")]
        [SerializeField] private float originTolerance = 1.25f;

        private PlayerInputActions input;

        // Client-side pacing only (never authoritative).
        private bool primaryHeld;
        private double nextAttackAttemptClientTime;

        // Server-authoritative cooldown timestamp.
        private double _nextAttackServerTime;
        private ulong _nextAttackId;

        private struct SweepCandidate
        {
            public DamageableNet Damageable;
            public Vector3 HitPoint;
            public float Distance;
            public float Multiplier;
        }

        private struct ServerAttackContext
        {
            public ulong AttackId;
            public ulong RequestedTargetNetworkObjectId;
            public Vector3 Direction;
            public int BaseDamage;
            public string WeaponLabel;
            public string WeaponItemId;
            public string CombatSkillId;
            public float SwingSpeed;
            public float CooldownSeconds;
        }

        public override void OnNetworkSpawn()
        {
            if (equipmentNet == null)
                equipmentNet = GetComponent<PlayerEquipmentNet>();

            if (playerCombatAnim == null)
                playerCombatAnim = GetComponent<PlayerCombatAnimNet>();

            if (skills == null)
                skills = GetComponent<SkillsNet>();

            if (viewOrigin == null)
            {
                viewOrigin = transform.Find("ViewOrigin");
                if (viewOrigin == null)
                {
                    Debug.LogWarning("[Combat] ViewOrigin not assigned on player. Falling back to player transform.", this);
                    viewOrigin = transform;
                }
            }

            if (!IsOwner || !IsClient)
                return;

            if (hitMask.value == 0)
            {
                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer >= 0)
                    hitMask = 1 << interactableLayer;
            }

            if (playerCamera == null)
                playerCamera = Camera.main;

            if (playerCamera == null)
                Debug.LogWarning("[Combat] PlayerAttackNet could not find a player camera.", this);

            input = new PlayerInputActions();
            input.Player.Primary.started += OnPrimaryStarted;
            input.Player.Primary.canceled += OnPrimaryCanceled;
            input.Enable();
        }

        private void OnDisable()
        {
            primaryHeld = false;
            nextAttackAttemptClientTime = 0d;

            if (input == null)
                return;

            input.Player.Primary.started -= OnPrimaryStarted;
            input.Player.Primary.canceled -= OnPrimaryCanceled;
            input.Disable();
            input = null;
        }

        private void Update()
        {
            if (!IsOwner || !IsClient || !primaryHeld)
                return;

            if (!CanProcessPrimaryGameplayInput())
            {
                nextAttackAttemptClientTime = Time.timeAsDouble + 0.05d;
                return;
            }

            double now = Time.timeAsDouble;
            if (now < nextAttackAttemptClientTime)
                return;

            bool sent = TryRequestAttackFromRaycast();
            float interval = GetOwnerExpectedSwingIntervalSeconds();
            nextAttackAttemptClientTime = now + (sent ? Mathf.Max(0.01f, interval) : 0.05d);
        }

        private void OnPrimaryStarted(InputAction.CallbackContext ctx)
        {
            if (!IsOwner || !IsClient || !ctx.started)
                return;

            if (!CanProcessPrimaryGameplayInput())
            {
                primaryHeld = false;
                nextAttackAttemptClientTime = 0d;
                return;
            }

            primaryHeld = true;
            bool sent = TryRequestAttackFromRaycast();
            float interval = GetOwnerExpectedSwingIntervalSeconds();
            nextAttackAttemptClientTime = Time.timeAsDouble + (sent ? Mathf.Max(0.01f, interval) : 0.05d);
        }

        private void OnPrimaryCanceled(InputAction.CallbackContext ctx)
        {
            if (!ctx.canceled)
                return;

            primaryHeld = false;
            nextAttackAttemptClientTime = 0d;
        }

        /// <summary>
        /// CLIENT OWNER ONLY: send attack suggestion. The server decides actual hit.
        /// We still raycast locally for a target hint, but this hint is non-authoritative.
        /// </summary>
        private bool TryRequestAttackFromRaycast()
        {
            if (!IsOwner || !IsClient || playerCamera == null)
                return false;

            if (!CanProcessPrimaryGameplayInput())
                return false;

            Vector3 origin = playerCamera.transform.position;
            Vector3 direction = playerCamera.transform.forward;

            ulong suggestedTargetId = 0;

            if (Physics.Raycast(origin, direction, out RaycastHit hit, attackRange, hitMask, QueryTriggerInteraction.Ignore))
            {
                DamageableNet suggested = ResolveDamageableFromCollider(hit.collider, out _);
                if (suggested != null && suggested.IsSpawned)
                    suggestedTargetId = suggested.NetworkObjectId;
            }

            // Always send attempt so accepted misses can still animate + consume cooldown.
            RequestAttackServerRpc(suggestedTargetId, origin, direction);
            return true;
        }

        /// <summary>
        /// OWNER -> SERVER: authoritative melee resolution.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void RequestAttackServerRpc(ulong targetNetworkObjectId, Vector3 clientRayOrigin, Vector3 clientRayDirection)
        {
            if (!IsServer)
                return;

            NetworkManager manager = NetworkManager.Singleton != null ? NetworkManager.Singleton : NetworkManager;
            if (manager == null || manager.SpawnManager == null)
            {
                LogAttackRejected("InvalidTarget");
                return;
            }

            double now = manager.ServerTime.Time;
            if (now < _nextAttackServerTime)
            {
                LogAttackRejected("Cooldown");
                return;
            }

            Vector3 expectedOrigin = viewOrigin != null ? viewOrigin.position : transform.position;
            float originDistance = Vector3.Distance(clientRayOrigin, expectedOrigin);
            if (originDistance > Mathf.Max(0.01f, originTolerance))
            {
                LogAttackRejected($"OriginMismatch dist={originDistance:F2}");
                return;
            }

            if (clientRayDirection.sqrMagnitude < 0.0001f)
            {
                LogAttackRejected("InvalidDirection");
                return;
            }
            clientRayDirection.Normalize();

            // Optional client suggestion: resolve to a spawned damageable on server.
            // The suggestion is never authoritative, but validating it here catches
            // spoofed/stale network IDs early and preserves explicit OutOfRange logging.
            if (targetNetworkObjectId != 0)
            {
                if (!TryResolveSuggestedTarget(manager.SpawnManager, targetNetworkObjectId, out DamageableNet suggested))
                {
                    LogAttackRejected("InvalidTarget");
                    return;
                }

                float toSuggested = Vector3.Distance(transform.position, suggested.transform.position);
                if (toSuggested > Mathf.Max(0.1f, attackRange))
                {
                    LogAttackRejected("OutOfRange");
                    return;
                }
            }

            ResolveServerAttackStats(out int baseDamage, out float swingSpeed, out string weaponLabel, out var style);
            bool hasCombatSkill = TryResolveCombatSkillForCurrentWeapon(out string combatSkillId, out string weaponItemId);
            float cooldownSeconds = 1f / Mathf.Max(0.01f, swingSpeed);
            ulong attackId = ++_nextAttackId;

            _nextAttackServerTime = now + cooldownSeconds;

            // Accepted swing intent: replicate animation to everyone now.
            playerCombatAnim?.ServerPlayAttack(style);
            // Server-authoritative durability consumption for accepted swings.
            // Rejected attacks return earlier and never consume durability.
            ServerConsumeAcceptedSwingDurability();
            Debug.Log($"[Combat] AttackAccepted id={attackId} dmg={baseDamage} delay={hitDelaySeconds:0.###} window={hitWindowSeconds:0.###}", this);

            var context = new ServerAttackContext
            {
                AttackId = attackId,
                RequestedTargetNetworkObjectId = targetNetworkObjectId,
                Direction = clientRayDirection,
                BaseDamage = baseDamage,
                WeaponLabel = weaponLabel,
                WeaponItemId = weaponItemId,
                CombatSkillId = hasCombatSkill ? combatSkillId : string.Empty,
                SwingSpeed = swingSpeed,
                CooldownSeconds = cooldownSeconds
            };
            StartCoroutine(ServerResolveAttackHitWindow(context));
        }

        /// <summary>
        /// SERVER ONLY: delayed hit timing window.
        /// - The swing animation starts immediately when attack is accepted.
        /// - Damage is evaluated later to match visual weapon connect timing.
        /// - This keeps hit timing authoritative while feeling less "instant click damage".
        /// </summary>
        private IEnumerator ServerResolveAttackHitWindow(ServerAttackContext context)
        {
            if (!IsServer)
                yield break;

            float delay = Mathf.Max(0f, hitDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            if (!IsServer || !IsSpawned)
            {
                Debug.Log($"[Combat] AttackIgnored id={context.AttackId} reason=AttackerDespawned", this);
                yield break;
            }

            NetworkManager manager = NetworkManager.Singleton != null ? NetworkManager.Singleton : NetworkManager;
            if (manager == null || manager.SpawnManager == null)
            {
                Debug.Log($"[Combat] AttackIgnored id={context.AttackId} reason=AttackerDespawned", this);
                yield break;
            }

            // If the suggested target despawned before the hit frame, treat as miss.
            if (context.RequestedTargetNetworkObjectId != 0 &&
                !TryResolveSuggestedTarget(manager.SpawnManager, context.RequestedTargetNetworkObjectId, out _))
            {
                Debug.Log($"[Combat] AttackIgnored id={context.AttackId} reason=TargetDespawned", this);
                Debug.Log($"[Combat] HitWindowCheck id={context.AttackId} result=Miss target=", this);
                yield break;
            }

            Vector3 origin = viewOrigin != null ? viewOrigin.position : transform.position;
            Vector3 direction = context.Direction;
            if (direction.sqrMagnitude < 0.0001f)
            {
                Debug.Log($"[Combat] HitWindowCheck id={context.AttackId} result=Miss target=", this);
                yield break;
            }

            direction.Normalize();

            bool hitApplied = false;
            var candidates = CollectSweepCandidates(origin, direction, Mathf.Max(0.1f, attackRange), out bool anyOccluded);
            SweepCandidate? selected = SelectSweepCandidate(candidates, context.RequestedTargetNetworkObjectId);

            if (selected.HasValue)
            {
                SweepCandidate c = selected.Value;
                int finalDamage = Mathf.Max(1, Mathf.RoundToInt(context.BaseDamage * Mathf.Max(0f, c.Multiplier)));

                IDamageableNet damageable = c.Damageable.GetComponent(typeof(IDamageableNet)) as IDamageableNet;
                if (damageable != null && damageable.ServerTryApplyDamage(finalDamage, OwnerClientId, c.HitPoint))
                {
                    hitApplied = true;
                    Debug.Log($"[Combat] SweepHit target={c.Damageable.name} dist={c.Distance:0.##} mult={c.Multiplier:0.##} dmg={finalDamage}", this);
                    Debug.Log($"[Combat] HitWindowCheck id={context.AttackId} result=Hit target={c.Damageable.name}", this);
                    Debug.Log($"[Combat] Attack accepted weapon={context.WeaponLabel} dmg={finalDamage} speed={context.SwingSpeed:0.##} cd={context.CooldownSeconds:0.###}", this);
                    ServerAwardCombatXp(context, finalDamage);
                }
            }

            if (singleHitPerAttack && hitApplied)
                yield break;

            string missReason = candidates.Count > 0 ? "TargetMismatch" : (anyOccluded ? "Occluded" : "NoValidHits");
            if (allowMissSwing)
                Debug.Log($"[Combat] SweepMiss reason={missReason}", this);

            Debug.Log($"[Combat] HitWindowCheck id={context.AttackId} result=Miss target=", this);
            Debug.Log($"[Combat] Attack accepted (miss) weapon={context.WeaponLabel} speed={context.SwingSpeed:0.##} cd={context.CooldownSeconds:0.###}", this);
        }

        /// <summary>
        /// Server melee sweep:
        /// - SphereCastAll gives forgiving hit volume for melee feel.
        /// - Optional LOS ray prevents hitting through walls.
        /// </summary>
        private List<SweepCandidate> CollectSweepCandidates(Vector3 origin, Vector3 direction, float maxRange, out bool anyOccluded)
        {
            anyOccluded = false;
            var results = new List<SweepCandidate>(8);

            RaycastHit[] hits = Physics.SphereCastAll(
                origin,
                Mathf.Max(0.01f, sweepRadius),
                direction,
                maxRange,
                hitMask,
                QueryTriggerInteraction.Ignore);

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                DamageableNet damageable = ResolveDamageableFromCollider(hit.collider, out float multiplier);
                if (damageable == null || !damageable.IsSpawned)
                    continue;

                if (requireLineOfSight && occlusionMask.value != 0)
                {
                    Vector3 toHit = hit.point - origin;
                    float distToHit = toHit.magnitude;
                    if (distToHit > 0.0001f)
                    {
                        if (Physics.Raycast(origin, toHit / distToHit, out RaycastHit block, Mathf.Max(0f, distToHit - 0.01f), occlusionMask, QueryTriggerInteraction.Ignore))
                        {
                            anyOccluded = true;
                            continue;
                        }
                    }
                }

                results.Add(new SweepCandidate
                {
                    Damageable = damageable,
                    HitPoint = hit.point,
                    Distance = hit.distance,
                    Multiplier = multiplier
                });
            }

            return results;
        }

        /// <summary>
        /// Selection policy:
        /// - Prefer suggested target if present in valid sweep results.
        /// - Otherwise pick closest valid result.
        /// </summary>
        private static SweepCandidate? SelectSweepCandidate(List<SweepCandidate> candidates, ulong requestedTargetId)
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            if (requestedTargetId != 0)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Damageable != null && candidates[i].Damageable.NetworkObjectId == requestedTargetId)
                        return candidates[i];
                }
            }

            return candidates[0];
        }

        private static DamageableNet ResolveDamageableFromCollider(Collider col, out float multiplier)
        {
            multiplier = 1f;
            if (col == null)
                return null;

            HitboxNet hitbox = col.GetComponent<HitboxNet>();
            if (hitbox != null && hitbox.RootDamageable != null)
            {
                multiplier = hitbox.DamageMultiplier;
                return hitbox.RootDamageable;
            }

            return col.GetComponentInParent<DamageableNet>();
        }

        private static bool TryResolveSuggestedTarget(NetworkSpawnManager spawnManager, ulong targetNetworkObjectId, out DamageableNet damageable)
        {
            damageable = null;
            if (spawnManager == null || targetNetworkObjectId == 0)
                return false;

            if (!spawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject))
                return false;

            if (targetObject == null || !targetObject.IsSpawned)
                return false;

            damageable = targetObject.GetComponent<DamageableNet>();
            return damageable != null && damageable.IsSpawned;
        }

        private void ResolveServerAttackStats(
            out int damage,
            out float swingSpeed,
            out string weaponLabel,
            out PlayerCombatAnimNet.AttackStyle style)
        {
            damage = Mathf.Max(1, defaultUnarmedDamage);
            swingSpeed = Mathf.Max(0.01f, defaultUnarmedSwingSpeed);
            weaponLabel = "Unarmed";
            style = PlayerCombatAnimNet.AttackStyle.Unarmed;

            if (equipmentNet == null)
                return;

            string itemId = equipmentNet.GetMainHandItemId();
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            if (!equipmentNet.TryGetItemDef(itemId, out var def) || def == null)
                return;

            weaponLabel = itemId;
            damage = Mathf.Max(1, Mathf.RoundToInt(def.Damage));
            swingSpeed = Mathf.Max(0.01f, def.SwingSpeed);

            style = def.Handedness == Handedness.BothHands
                ? PlayerCombatAnimNet.AttackStyle.TwoHand
                : PlayerCombatAnimNet.AttackStyle.OneHand;
        }

        private void ServerAwardCombatXp(ServerAttackContext context, int appliedDamage)
        {
            // Server authoritative: only the server can grant progression.
            if (!IsServer)
                return;

            if (xpPerHit <= 0)
                return;

            // Optional guard for future mechanics where an accepted hit might do 0 damage.
            if (!awardXpOnZeroDamage && appliedDamage <= 0)
                return;

            if (string.IsNullOrWhiteSpace(context.CombatSkillId))
                return;

            if (skills == null)
                skills = GetComponent<SkillsNet>();

            if (skills == null)
                return;

            skills.AddXp(context.CombatSkillId, xpPerHit);
            Debug.Log($"[CombatSkill] Awarded XP skill={context.CombatSkillId} xp={xpPerHit} weapon={context.WeaponItemId} attacker={OwnerClientId}", this);
        }

        private bool TryResolveCombatSkillForCurrentWeapon(out string skillId, out string weaponItemId)
        {
            skillId = string.Empty;
            weaponItemId = "Unarmed";

            if (equipmentNet == null)
                return false;

            string itemId = equipmentNet.GetMainHandItemId();
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (!equipmentNet.TryGetItemDef(itemId, out ItemDef def) || def == null)
                return false;

            weaponItemId = itemId;
            return TryMapCombatSkillIdFromToolTags(def.ToolTags, out skillId);
        }

        private static bool TryMapCombatSkillIdFromToolTags(ToolTag[] tags, out string skillId)
        {
            // Priority rule for items with multiple tags.
            if (HasToolTag(tags, ToolTag.Axe))
            {
                skillId = SkillId.CombatAxe;
                return true;
            }

            if (HasToolTag(tags, ToolTag.Pickaxe))
            {
                skillId = SkillId.CombatPickaxe;
                return true;
            }

            if (HasToolTag(tags, ToolTag.Knife))
            {
                skillId = SkillId.CombatKnife;
                return true;
            }

            if (HasToolTag(tags, ToolTag.Club))
            {
                skillId = SkillId.CombatClub;
                return true;
            }

            skillId = string.Empty;
            return false;
        }

        private static bool HasToolTag(ToolTag[] tags, ToolTag wanted)
        {
            if (tags == null || tags.Length == 0)
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == wanted)
                    return true;
            }

            return false;
        }
        // Client local estimate for request pacing only.
        private float GetOwnerExpectedSwingIntervalSeconds()
        {
            float swingSpeed = Mathf.Max(0.01f, defaultUnarmedSwingSpeed);

            if (equipmentNet != null)
            {
                string itemId = equipmentNet.GetMainHandItemId();
                if (!string.IsNullOrWhiteSpace(itemId) && equipmentNet.TryGetItemDef(itemId, out var def) && def != null)
                    swingSpeed = Mathf.Max(0.01f, def.SwingSpeed);
            }

            return 1f / swingSpeed;
        }

        private bool CanProcessPrimaryGameplayInput()
        {
            // Client-side input gate only. Server authority still validates hits/damage.
            if (InputState.GameplayLocked)
                return false;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return false;

            return true;
        }
        private void LogAttackRejected(string reason)
        {
            Debug.Log($"[Combat] Attack rejected reason={reason}", this);
        }

        /// <summary>
        /// SERVER ONLY: consume one durability use for the equipped main-hand item.
        /// This is called on accepted swings (hit or miss), matching cooldown usage.
        /// </summary>
        private void ServerConsumeAcceptedSwingDurability()
        {
            if (!IsServer || equipmentNet == null)
                return;

            // This helper is a no-op when unarmed or when the equipped item is indestructible.
            equipmentNet.ServerDamageMainHandDurability(1, out _, out _);
        }
    }
}


