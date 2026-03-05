using System;
using System.Collections.Generic;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Players;
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

        [Header("Server Ray Validation")]
        [SerializeField] private float originTolerance = 1.25f;

        private PlayerInputActions input;

        // Client-side pacing only (never authoritative).
        private bool primaryHeld;
        private double nextAttackAttemptClientTime;

        // Server-authoritative cooldown timestamp.
        private double _nextAttackServerTime;

        private struct SweepCandidate
        {
            public DamageableNet Damageable;
            public Vector3 HitPoint;
            public float Distance;
            public float Multiplier;
        }

        public override void OnNetworkSpawn()
        {
            if (equipmentNet == null)
                equipmentNet = GetComponent<PlayerEquipmentNet>();

            if (playerCombatAnim == null)
                playerCombatAnim = GetComponent<PlayerCombatAnimNet>();

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
            float cooldownSeconds = 1f / Mathf.Max(0.01f, swingSpeed);

            // Accepted swing intent: replicate animation to everyone now.
            playerCombatAnim?.ServerPlayAttack(style);

            var candidates = CollectSweepCandidates(clientRayOrigin, clientRayDirection, Mathf.Max(0.1f, attackRange), out bool anyOccluded);
            SweepCandidate? selected = SelectSweepCandidate(candidates, targetNetworkObjectId);

            if (selected.HasValue)
            {
                SweepCandidate c = selected.Value;
                int finalDamage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * Mathf.Max(0f, c.Multiplier)));

                IDamageableNet damageable = c.Damageable.GetComponent(typeof(IDamageableNet)) as IDamageableNet;
                if (damageable != null && damageable.ServerTryApplyDamage(finalDamage, OwnerClientId, c.HitPoint))
                {
                    _nextAttackServerTime = now + cooldownSeconds;
                    Debug.Log($"[Combat] SweepHit target={c.Damageable.name} dist={c.Distance:0.##} mult={c.Multiplier:0.##} dmg={finalDamage}", this);
                    Debug.Log($"[Combat] Attack accepted weapon={weaponLabel} dmg={finalDamage} speed={swingSpeed:0.##} cd={cooldownSeconds:0.###}", this);
                    return;
                }
            }

            _nextAttackServerTime = now + cooldownSeconds;
            string missReason = candidates.Count > 0 ? "TargetMismatch" : (anyOccluded ? "Occluded" : "NoValidHits");
            Debug.Log($"[Combat] SweepMiss reason={missReason}", this);
            Debug.Log($"[Combat] Attack accepted (miss) weapon={weaponLabel} speed={swingSpeed:0.##} cd={cooldownSeconds:0.###}", this);
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
    }
}

