using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Player attack bridge for server-authoritative melee combat.
    ///
    /// Client responsibilities:
    /// - Read input and hold state.
    /// - Perform local raycast to pick a candidate target.
    /// - Send target NetworkObjectId to server via ServerRpc.
    ///
    /// Server responsibilities:
    /// - Validate cooldown, target existence/spawn/range.
    /// - Resolve equipped weapon stats (or unarmed fallback).
    /// - Apply damage on TrainingDummyNet.
    ///
    /// IMPORTANT: Clients never apply damage and never own cooldown state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAttackNet : NetworkBehaviour
    {
        [Header("Attack Raycast")]
        [Tooltip("Maximum melee attack distance in meters.")]
        [Min(0.1f)]
        [SerializeField] private float attackRange = 2.5f;

        [Tooltip("Only colliders in this mask are considered attack targets.")]
        [SerializeField] private LayerMask interactableMask;

        [Header("Unarmed Fallback")]
        [Tooltip("Used when no valid main-hand weapon is equipped.")]
        [Min(1)]
        [SerializeField] private int defaultUnarmedDamage = 1;

        [Tooltip("Attacks/second used when no valid weapon is equipped.")]
        [Min(0.01f)]
        [SerializeField] private float defaultUnarmedSwingSpeed = 0.5f;

        [Header("References")]
        [Tooltip("Camera used for attack raycast origin/direction. If null, Camera.main is used.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Player equipment source (auto-fetched from same GameObject if null).")]
        [SerializeField] private PlayerEquipmentNet equipmentNet;

        private PlayerInputActions input;

        // Client-side hold/repeat (not authoritative; only request pacing).
        private bool primaryHeld;
        private double nextAttackAttemptClientTime;

        // Server-authoritative cooldown timestamp in server time seconds.
        private double _nextAttackServerTime;

        public override void OnNetworkSpawn()
        {
            if (equipmentNet == null)
                equipmentNet = GetComponent<PlayerEquipmentNet>();

            if (!IsOwner || !IsClient)
                return;

            if (interactableMask.value == 0)
            {
                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer >= 0)
                    interactableMask = 1 << interactableLayer;
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
            if (!IsOwner || !IsClient)
                return;

            if (!primaryHeld)
                return;

            double now = Time.timeAsDouble;
            if (now < nextAttackAttemptClientTime)
                return;

            bool sent = TryRequestAttackFromRaycast();

            // Use local estimate to pace requests while held.
            float interval = GetOwnerExpectedSwingIntervalSeconds();
            nextAttackAttemptClientTime = now + Mathf.Max(0.01f, interval);

            // If no valid target this frame, keep checking frequently while held.
            if (!sent)
                nextAttackAttemptClientTime = now + 0.05d;
        }

        private void OnPrimaryStarted(InputAction.CallbackContext ctx)
        {
            if (!IsOwner || !IsClient || !ctx.started)
                return;

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
        /// CLIENT/OWNER ONLY: Select candidate target and request a server attack.
        /// </summary>
        private bool TryRequestAttackFromRaycast()
        {
            if (!IsOwner || !IsClient)
                return false;

            if (playerCamera == null)
                return false;

            Ray ray = new(playerCamera.transform.position, playerCamera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, attackRange, interactableMask, QueryTriggerInteraction.Collide))
                return false;

            TrainingDummyNet dummy = hit.collider.GetComponentInParent<TrainingDummyNet>();
            if (dummy == null)
                return false;

            RequestAttackServerRpc(dummy.NetworkObjectId);
            return true;
        }

        /// <summary>
        /// OWNER -> SERVER: attack request for a specific network target.
        /// Server validates cooldown + target + range, then applies damage.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void RequestAttackServerRpc(ulong targetNetworkObjectId)
        {
            if (!IsServer)
                return;

            NetworkManager manager = NetworkManager.Singleton != null ? NetworkManager.Singleton : NetworkManager;
            if (manager == null || manager.SpawnManager == null)
            {
                LogAttackRejected("TargetMissing");
                return;
            }

            double now = manager.ServerTime.Time;
            if (now < _nextAttackServerTime)
            {
                LogAttackRejected("Cooldown");
                return;
            }

            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObject) ||
                targetObject == null ||
                !targetObject.IsSpawned)
            {
                LogAttackRejected("TargetMissing");
                return;
            }

            TrainingDummyNet dummy = targetObject.GetComponent<TrainingDummyNet>();
            if (dummy == null || !dummy.IsSpawned)
            {
                LogAttackRejected("InvalidTarget");
                return;
            }

            float maxRange = Mathf.Max(0.1f, attackRange);
            float sqrDistance = (dummy.transform.position - transform.position).sqrMagnitude;
            if (sqrDistance > maxRange * maxRange)
            {
                LogAttackRejected("OutOfRange");
                return;
            }

            ResolveServerAttackStats(out int damage, out float swingSpeed, out string weaponLabel);
            float cooldownSeconds = 1f / Mathf.Max(0.01f, swingSpeed);

            dummy.ServerApplyDamage(damage);
            _nextAttackServerTime = now + cooldownSeconds;

            Debug.Log($"[Combat] Attack accepted weapon={weaponLabel} dmg={damage} speed={swingSpeed:0.##} cd={cooldownSeconds:0.###}", this);
            SwingFeedbackClientRpc(weaponLabel, damage);
        }

        /// <summary>
        /// SERVER ONLY: resolves damage + speed from main-hand item, else unarmed fallback.
        /// </summary>
        private void ResolveServerAttackStats(out int damage, out float swingSpeed, out string weaponLabel)
        {
            damage = Mathf.Max(1, defaultUnarmedDamage);
            swingSpeed = Mathf.Max(0.01f, defaultUnarmedSwingSpeed);
            weaponLabel = "Unarmed";

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
        }

        /// <summary>
        /// OWNER-LOCAL ESTIMATE ONLY (client pacing, not authority).
        /// Mirrors server weapon speed fallback for smoother hold-to-attack behavior.
        /// </summary>
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

        [ClientRpc]
        private void SwingFeedbackClientRpc(string weaponLabel, int damage)
        {
            Debug.Log($"[Combat] Swing request weapon={weaponLabel} dmg={damage}", this);
        }

        private void LogAttackRejected(string reason)
        {
            Debug.Log($"[Combat] Attack rejected reason={reason}", this);
        }
    }
}
