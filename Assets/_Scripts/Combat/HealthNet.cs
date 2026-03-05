using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Reusable server-authoritative health component.
    ///
    /// Authority model:
    /// - Health is replicated to everyone.
    /// - Only the server can write health.
    /// - Damage feedback visuals are broadcast via ClientRpc.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HealthNet : NetworkBehaviour
    {
        [Header("Health")]
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private bool despawnOnZero = true;

        // Server-authoritative health value. Clients read only.
        private readonly NetworkVariable<int> currentHealth =
            new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Guards one-time server spawn initialization.
        private bool _serverInitialized;

        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int CurrentHealth => Mathf.Clamp(currentHealth.Value, 0, MaxHealth);
        public float Health01 => Mathf.Clamp01((float)CurrentHealth / MaxHealth);

        /// <summary>
        /// Read-only exposure for UI/view scripts that subscribe to health changes.
        /// Server is still the only writer due to NetworkVariable permissions.
        /// </summary>
        public NetworkVariable<int> CurrentHealthNetVar => currentHealth;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            maxHealth = Mathf.Max(1, maxHealth);

            // Initialize once when this networked object first spawns on server.
            if (!_serverInitialized)
            {
                currentHealth.Value = maxHealth;
                _serverInitialized = true;
            }
        }

        /// <summary>
        /// SERVER ONLY: Resets to full health.
        /// </summary>
        public void ServerResetHealth()
        {
            if (!IsServer)
                return;

            currentHealth.Value = MaxHealth;
        }

        /// <summary>
        /// SERVER ONLY: Applies incoming damage and broadcasts visual feedback.
        /// Returns true if damage was applied.
        /// </summary>
        public bool ServerApplyDamage(int amount, ulong attackerClientId, Vector3 hitPoint)
        {
            if (!IsServer)
                return false;

            if (!IsSpawned)
                return false;

            if (CurrentHealth <= 0)
                return false;

            int clampedAmount = Mathf.Max(1, amount);
            int next = Mathf.Max(0, CurrentHealth - clampedAmount);
            int applied = CurrentHealth - next;
            currentHealth.Value = next;

            Vector3 safeHitPoint = IsFinite(hitPoint) ? hitPoint : transform.position + Vector3.up * 1.6f;
            DamageFeedbackClientRpc(applied, safeHitPoint, next, MaxHealth);

            if (next <= 0 && despawnOnZero)
                ServerDespawnSelf();

            return true;
        }

        [ClientRpc]
        private void DamageFeedbackClientRpc(int amount, Vector3 hitPoint, int current, int max)
        {
            // Popup is purely visual and local; health itself is replicated by currentHealth variable.
            if (amount > 0)
                DamagePopupWorld.Spawn(amount, hitPoint);

            // Optional local hit reaction if this object has DamageableNet visuals.
            if (TryGetComponent<DamageableNet>(out var damageable))
                damageable.PlayHitReactionLocal();
        }

        private void ServerDespawnSelf()
        {
            if (!IsServer)
                return;

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
                return;
            }

            Destroy(gameObject);
        }

        private static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        }
    }
}
