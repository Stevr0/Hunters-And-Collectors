using HuntersAndCollectors.Stats;
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
    /// - Max health is derived from actor stats (IStatsProvider) with a safe fallback.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HealthNet : NetworkBehaviour
    {
        [Header("Health")]
        [SerializeField] private int fallbackMaxHealth = 100;
        [SerializeField] private bool despawnOnZero = true;

        // Server-authoritative health value. Clients read only.
        private readonly NetworkVariable<int> currentHealth =
            new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Guards one-time server spawn initialization.
        private bool serverInitialized;
        private int resolvedMaxHealth;
        private IStatsProvider cachedStatsProvider;
        private bool warnedMissingStatsProvider;

        public int MaxHealth => Mathf.Max(1, resolvedMaxHealth > 0 ? resolvedMaxHealth : fallbackMaxHealth);
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

            fallbackMaxHealth = Mathf.Max(1, fallbackMaxHealth);
            ServerRecalculateMaxHealthInternal(initializeIfNeeded: !serverInitialized);
        }

        /// <summary>
        /// SERVER ONLY: Re-resolves max health from actor stats and clamps current health if needed.
        /// </summary>
        public void ServerRecalculateMaxHealth()
        {
            ServerRecalculateMaxHealthInternal(initializeIfNeeded: false);
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

        private void ServerRecalculateMaxHealthInternal(bool initializeIfNeeded)
        {
            if (!IsServer)
                return;

            if (!IsSpawned)
                return;

            resolvedMaxHealth = ResolveMaxHealthFromStats();
            int effectiveMax = MaxHealth;

            if (initializeIfNeeded)
            {
                currentHealth.Value = effectiveMax;
                serverInitialized = true;
                return;
            }

            if (currentHealth.Value > effectiveMax)
                currentHealth.Value = effectiveMax;
        }

        private int ResolveMaxHealthFromStats()
        {
            if (cachedStatsProvider == null)
                cachedStatsProvider = GetComponentInParent<IStatsProvider>();

            if (cachedStatsProvider == null)
            {
                if (!warnedMissingStatsProvider)
                {
                    warnedMissingStatsProvider = true;
                    Debug.LogWarning($"[Combat] HealthNet missing IStatsProvider on '{name}'. Using fallback max health {Mathf.Max(1, fallbackMaxHealth)}.", this);
                }

                return Mathf.Max(1, fallbackMaxHealth);
            }

            EffectiveStats stats = cachedStatsProvider.GetEffectiveStats();
            int derivedMax = Mathf.RoundToInt(stats.MaxHealth);

            if (derivedMax <= 0)
                return Mathf.Max(1, fallbackMaxHealth);

            return derivedMax;
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
