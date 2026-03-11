using HuntersAndCollectors.Actors;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Persistence;
using HuntersAndCollectors.Stats;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Reusable server-authoritative health component.
    ///
    /// Compatibility notes:
    /// - Non-player actors use native HealthNet storage exactly as before.
    /// - Player actors can attach PlayerVitalsNet and HealthNet will delegate damage/reset/max handling
    ///   to PlayerVitalsNet while still mirroring values for existing systems.
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

        // Guards one-time death resolution for this actor instance.
        private bool serverDeathResolved;
        private ActorLootDropper cachedLootDropper;

        // Optional player vitals bridge.
        private PlayerVitalsNet cachedPlayerVitals;

        public int MaxHealth
        {
            get
            {
                if (cachedPlayerVitals != null)
                    return Mathf.Max(1, cachedPlayerVitals.CurrentMaxHealth);

                return Mathf.Max(1, resolvedMaxHealth > 0 ? resolvedMaxHealth : fallbackMaxHealth);
            }
        }

        public int CurrentHealth
        {
            get
            {
                if (cachedPlayerVitals != null)
                    return Mathf.Clamp(cachedPlayerVitals.CurrentHealth, 0, MaxHealth);

                return Mathf.Clamp(currentHealth.Value, 0, MaxHealth);
            }
        }

        public float Health01 => Mathf.Clamp01((float)CurrentHealth / Mathf.Max(1, MaxHealth));

        /// <summary>
        /// Read-only exposure for UI/view scripts that subscribe to health changes.
        /// Server is still the only writer due to NetworkVariable permissions.
        /// </summary>
        public NetworkVariable<int> CurrentHealthNetVar => currentHealth;

        public override void OnNetworkSpawn()
        {
            cachedPlayerVitals = GetComponent<PlayerVitalsNet>();

            if (!IsServer)
                return;

            fallbackMaxHealth = Mathf.Max(1, fallbackMaxHealth);
            serverDeathResolved = false;

            if (cachedPlayerVitals != null)
            {
                ServerMirrorFromVitals(cachedPlayerVitals.CurrentHealth, cachedPlayerVitals.CurrentMaxHealth);
                serverInitialized = true;
                return;
            }

            ServerRecalculateMaxHealthInternal(initializeIfNeeded: !serverInitialized);
        }

        /// <summary>
        /// SERVER ONLY: called by PlayerVitalsNet to keep backward compatibility mirrors in sync.
        /// </summary>
        public void ServerMirrorFromVitals(int current, int max)
        {
            if (!IsServer)
                return;

            int safeMax = Mathf.Max(1, max);
            int safeCurrent = Mathf.Clamp(current, 0, safeMax);

            resolvedMaxHealth = safeMax;
            currentHealth.Value = safeCurrent;

            if (safeCurrent > 0)
                serverDeathResolved = false;
        }

        /// <summary>
        /// SERVER ONLY: Re-resolves max health from actor stats and clamps current health if needed.
        /// </summary>
        public void ServerRecalculateMaxHealth()
        {
            if (cachedPlayerVitals != null)
            {
                ServerMirrorFromVitals(cachedPlayerVitals.CurrentHealth, cachedPlayerVitals.CurrentMaxHealth);
                return;
            }

            ServerRecalculateMaxHealthInternal(initializeIfNeeded: false);
        }

        /// <summary>
        /// SERVER ONLY: Resets to full health.
        /// </summary>
        public void ServerResetHealth()
        {
            if (!IsServer)
                return;

            if (cachedPlayerVitals != null)
            {
                cachedPlayerVitals.ServerResetToFull();
                ServerMirrorFromVitals(cachedPlayerVitals.CurrentHealth, cachedPlayerVitals.CurrentMaxHealth);
                serverDeathResolved = false;
                return;
            }

            currentHealth.Value = MaxHealth;
            serverDeathResolved = false;
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
            int previous = CurrentHealth;
            int next;

            if (cachedPlayerVitals != null)
            {
                bool applied = cachedPlayerVitals.ServerApplyDamage(clampedAmount);
                if (!applied)
                    return false;

                next = cachedPlayerVitals.CurrentHealth;
                ServerMirrorFromVitals(next, cachedPlayerVitals.CurrentMaxHealth);
            }
            else
            {
                next = Mathf.Max(0, CurrentHealth - clampedAmount);
                currentHealth.Value = next;
            }

            int appliedAmount = Mathf.Max(0, previous - next);

            Vector3 safeHitPoint = IsFinite(hitPoint) ? hitPoint : transform.position + Vector3.up * 1.6f;
            DamageFeedbackClientRpc(appliedAmount, safeHitPoint, next, MaxHealth);

            if (next <= 0)
            {
                ServerResolveDeathOnce();

                if (ShouldDespawnOnDeath())
                {
                    ServerDespawnSelf();
                }
                else if (cachedPlayerVitals != null)
                {
                    PlayerNetworkRoot playerRoot = GetComponent<PlayerNetworkRoot>();
                    string playerKey = playerRoot != null ? playerRoot.PlayerKey : "<unknown>";
                    Debug.Log($"[Death] Prevented player despawn for key={playerKey}");
                }
            }

            return appliedAmount > 0;
        }


        private bool ShouldDespawnOnDeath()
        {
            if (!despawnOnZero)
                return false;

            // Player death is a state transition on the same owned player object.
            // Only non-player actors should use the generic despawn cleanup path.
            return cachedPlayerVitals == null;
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

        private void ServerResolveDeathOnce()
        {
            if (!IsServer || serverDeathResolved)
                return;

            serverDeathResolved = true;

            if (cachedPlayerVitals != null)
            {
                PlayerNetworkRoot playerRoot = GetComponent<PlayerNetworkRoot>();
                if (playerRoot != null)
                {
                    Debug.Log($"[Death] Player death started key={playerRoot.PlayerKey}");
                    SaveManager.TryCreateGraveForPlayerDeath(playerRoot, transform.position);
                }
            }

            if (cachedLootDropper == null)
                cachedLootDropper = GetComponent<ActorLootDropper>();

            cachedLootDropper?.ServerDropLoot();
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
                serverDeathResolved = false;
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





