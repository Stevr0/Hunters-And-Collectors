using System;
using System.Collections.Generic;
using HuntersAndCollectors.Building;
using HuntersAndCollectors.Combat;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Skills;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// PlayerVitalsNet
    /// --------------------------------------------------------------------
    /// Server-authoritative first-pass vitals + regen + food + rested runtime system.
    ///
    /// Design notes:
    /// - This component is intentionally scoped to players.
    /// - Temporary food/rested state is runtime-only in first pass (not persisted).
    /// - Clients only request actions; server validates and mutates all authoritative state.
    /// - HealthNet can mirror these values for backward compatibility with existing damageable pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerVitalsNet : NetworkBehaviour
    {
        // --------------------------------------------------------------------
        // Baseline values and locked first-pass tuning
        // --------------------------------------------------------------------
        private const int BaseMaxHealth = 100;
        private const int BaseMaxStamina = 100;

        private const float BaseHealthRegenPerSecond = 0.25f;
        private const float BaseStaminaRegenPerSecond = 1.00f;

        private const float HealthRegenPerVitalityLevel = 0.0125f;
        private const float StaminaRegenPerEnduranceLevel = 0.03f;

        private const float RestedHealthRegenMultiplier = 1.5f;
        private const float RestedStaminaRegenMultiplier = 2.0f;

        private const float BusyHealthRegenMultiplier = 0.25f;
        private const float BusyStaminaRegenMultiplier = 0.50f;

        private const float BusyDurationSeconds = 5f;
        private const int MaxActiveFoods = 3;

        // XP gain tuning from actual regenerated amounts.
        private const float VitalityHpPerXp = 10f;
        private const float EnduranceStaminaPerXp = 20f;

        // --------------------------------------------------------------------
        // References
        // --------------------------------------------------------------------
        [Header("Dependencies")]
        [Tooltip("Authoritative inventory source used to validate consume-by-slot requests.")]
        [SerializeField] private PlayerInventoryNet inventory;

        [Tooltip("Skill container used to read vitality/endurance levels and grant regen XP.")]
        [SerializeField] private SkillsNet skills;

        [Tooltip("Optional HealthNet bridge used for backward compatibility with existing damage pipeline/UI.")]
        [SerializeField] private HealthNet healthBridge;

        [Header("Server Tick")]
        [Tooltip("How often the server updates replicated food remaining-seconds summaries.")]
        [Min(0.1f)]
        [SerializeField] private float foodSummaryRefreshSeconds = 1f;

        // --------------------------------------------------------------------
        // Replicated vitals state
        // --------------------------------------------------------------------
        private readonly NetworkVariable<int> currentHealth =
            new(BaseMaxHealth, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> currentStamina =
            new(BaseMaxStamina, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> currentMaxHealth =
            new(BaseMaxHealth, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> currentMaxStamina =
            new(BaseMaxStamina, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> isRested =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> restedSecondsRemaining =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Small bounded UI payload for active foods.
        private readonly NetworkList<ActiveFoodBuff> activeFoodSlots =
            new(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // --------------------------------------------------------------------
        // Server runtime-only food entries (precise countdown/totals)
        // --------------------------------------------------------------------
        private readonly List<RuntimeFoodBuff> runtimeFoods = new(MaxActiveFoods);

        private struct RuntimeFoodBuff
        {
            public string ItemId;
            public int MaxHealthBonus;
            public int MaxStaminaBonus;
            public float HealthRegenBonus;
            public float StaminaRegenBonus;
            public float RemainingSeconds;
            public float DurationSeconds;
        }

        // --------------------------------------------------------------------
        // Server runtime state
        // --------------------------------------------------------------------
        private float serverHealthExact;
        private float serverStaminaExact;

        private float vitalityXpAccumulator;
        private float enduranceXpAccumulator;

        private double serverBusyUntil;

        private RestZone currentRestZone;
        private double restZoneEnterServerTime;
        private bool restWarmupStarted;

        private float foodSummaryRefreshTimer;
        private bool serverInitialized;

        // --------------------------------------------------------------------
        // Client/UI events
        // --------------------------------------------------------------------
        public event Action OnVitalsChanged;
        public event Action<FoodConsumeResult, int> OnConsumeFoodResult;

        // --------------------------------------------------------------------
        // Public read-only accessors for UI/other systems
        // --------------------------------------------------------------------
        public int CurrentHealth => Mathf.Clamp(currentHealth.Value, 0, Mathf.Max(1, currentMaxHealth.Value));
        public int CurrentStamina => Mathf.Clamp(currentStamina.Value, 0, Mathf.Max(1, currentMaxStamina.Value));
        public int CurrentMaxHealth => Mathf.Max(1, currentMaxHealth.Value);
        public int CurrentMaxStamina => Mathf.Max(1, currentMaxStamina.Value);

        public bool IsRested => isRested.Value;
        public float RestedSecondsRemaining => Mathf.Max(0f, restedSecondsRemaining.Value);
        public bool IsBusy => IsServer && ServerTimeNow() < serverBusyUntil;

        public NetworkVariable<int> CurrentHealthNetVar => currentHealth;
        public NetworkVariable<int> CurrentStaminaNetVar => currentStamina;
        public NetworkVariable<int> CurrentMaxHealthNetVar => currentMaxHealth;
        public NetworkVariable<int> CurrentMaxStaminaNetVar => currentMaxStamina;
        public NetworkVariable<bool> IsRestedNetVar => isRested;
        public NetworkVariable<float> RestedSecondsRemainingNetVar => restedSecondsRemaining;
        public NetworkList<ActiveFoodBuff> ActiveFoodSlots => activeFoodSlots;

        private void Awake()
        {
            if (inventory == null)
                inventory = GetComponent<PlayerInventoryNet>();

            if (skills == null)
                skills = GetComponent<SkillsNet>();

            if (healthBridge == null)
                healthBridge = GetComponent<HealthNet>();
        }

        public override void OnNetworkSpawn()
        {
            currentHealth.OnValueChanged += OnAnyVitalValueChanged;
            currentStamina.OnValueChanged += OnAnyVitalValueChanged;
            currentMaxHealth.OnValueChanged += OnAnyVitalValueChanged;
            currentMaxStamina.OnValueChanged += OnAnyVitalValueChanged;
            isRested.OnValueChanged += OnRestedFlagChanged;
            restedSecondsRemaining.OnValueChanged += OnRestedSecondsChanged;
            activeFoodSlots.OnListChanged += OnActiveFoodSlotsChanged;

            if (IsServer)
            {
                // First-pass runtime reset requirements:
                // - full vitals
                // - no food
                // - no rested
                // - no busy carryover
                ServerResetRuntimeStateToDefaults();
            }
        }

        public override void OnNetworkDespawn()
        {
            currentHealth.OnValueChanged -= OnAnyVitalValueChanged;
            currentStamina.OnValueChanged -= OnAnyVitalValueChanged;
            currentMaxHealth.OnValueChanged -= OnAnyVitalValueChanged;
            currentMaxStamina.OnValueChanged -= OnAnyVitalValueChanged;
            isRested.OnValueChanged -= OnRestedFlagChanged;
            restedSecondsRemaining.OnValueChanged -= OnRestedSecondsChanged;
            activeFoodSlots.OnListChanged -= OnActiveFoodSlotsChanged;

            currentRestZone = null;
            restWarmupStarted = false;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned)
                return;

            if (!serverInitialized)
                ServerResetRuntimeStateToDefaults();

            float dt = Mathf.Max(0f, Time.deltaTime);
            if (dt <= 0f)
                return;

            TickRestedState(dt);
            TickFoodBuffDurations(dt);
            TickRegeneration(dt);
            RefreshFoodSummaryIfNeeded(dt);
        }

        /// <summary>
        /// UI helper method. Owner calls this to request server-authoritative food consumption.
        /// </summary>
        public void TryConsumeFoodFromInventorySlot(int slotIndex)
        {
            if (!IsOwner)
                return;

            RequestConsumeFoodFromSlotServerRpc(slotIndex);
        }

        /// <summary>
        /// SERVER ONLY: applies authoritative damage.
        /// Returns true when damage was applied.
        /// </summary>
        public bool ServerApplyDamage(int amount)
        {
            if (!IsServer || amount <= 0)
                return false;

            int oldInt = CurrentHealth;
            float oldExact = serverHealthExact;

            serverHealthExact = Mathf.Max(0f, serverHealthExact - Mathf.Max(1, amount));
            int next = Mathf.Clamp(Mathf.FloorToInt(serverHealthExact), 0, CurrentMaxHealth);
            currentHealth.Value = next;

            // Keep exact and int coherent when crossing integer boundaries due floor.
            serverHealthExact = currentHealth.Value;

            if (next < oldInt || serverHealthExact < oldExact)
                ServerMarkBusyFromAction();

            MirrorHealthBridgeIfPresent();
            return next < oldInt;
        }

        /// <summary>
        /// SERVER ONLY: applies direct healing (non-regen source).
        /// </summary>
        public bool ServerApplyHealing(int amount)
        {
            if (!IsServer || amount <= 0)
                return false;

            int maxHealth = CurrentMaxHealth;
            int old = CurrentHealth;
            serverHealthExact = Mathf.Min(maxHealth, serverHealthExact + Mathf.Max(1, amount));
            currentHealth.Value = Mathf.Clamp(Mathf.FloorToInt(serverHealthExact), 0, maxHealth);
            serverHealthExact = currentHealth.Value;
            MirrorHealthBridgeIfPresent();
            return currentHealth.Value > old;
        }

        /// <summary>
        /// SERVER ONLY: spends stamina and marks the player busy when any amount is actually spent.
        /// </summary>
        public bool ServerSpendStamina(int amount)
        {
            if (!IsServer || amount <= 0)
                return false;

            int old = CurrentStamina;
            serverStaminaExact = Mathf.Max(0f, serverStaminaExact - Mathf.Max(1, amount));
            currentStamina.Value = Mathf.Clamp(Mathf.FloorToInt(serverStaminaExact), 0, CurrentMaxStamina);
            serverStaminaExact = currentStamina.Value;

            if (currentStamina.Value < old)
                ServerMarkBusyFromAction();

            return currentStamina.Value < old;
        }

        /// <summary>
        /// SERVER ONLY: restores stamina directly (non-regen source).
        /// </summary>
        public bool ServerRestoreStamina(int amount)
        {
            if (!IsServer || amount <= 0)
                return false;

            int old = CurrentStamina;
            serverStaminaExact = Mathf.Min(CurrentMaxStamina, serverStaminaExact + Mathf.Max(1, amount));
            currentStamina.Value = Mathf.Clamp(Mathf.FloorToInt(serverStaminaExact), 0, CurrentMaxStamina);
            serverStaminaExact = currentStamina.Value;
            return currentStamina.Value > old;
        }

        /// <summary>
        /// SERVER ONLY: external systems (harvesting/pickup/etc.) can call this to apply busy suppression window.
        /// </summary>
        public void ServerMarkBusyFromAction()
        {
            if (!IsServer)
                return;

            serverBusyUntil = Math.Max(serverBusyUntil, ServerTimeNow() + BusyDurationSeconds);
        }

        /// <summary>
        /// SERVER ONLY: rest zone trigger callback.
        /// </summary>
        public void ServerNotifyEnteredRestZone(RestZone zone)
        {
            if (!IsServer || zone == null)
                return;

            if (!zone.GrantsRested)
                return;

            currentRestZone = zone;
            restZoneEnterServerTime = ServerTimeNow();
            restWarmupStarted = true;
        }

        /// <summary>
        /// SERVER ONLY: rest zone trigger callback.
        /// </summary>
        public void ServerNotifyExitedRestZone(RestZone zone)
        {
            if (!IsServer)
                return;

            if (zone == null || currentRestZone != zone)
                return;

            currentRestZone = null;
            restWarmupStarted = false;
        }

        /// <summary>
        /// SERVER ONLY: immediately refresh rested duration to a specific value and activate rested.
        /// </summary>
        public void ServerRefreshRested(float durationSeconds)
        {
            if (!IsServer)
                return;

            float duration = Mathf.Max(1f, durationSeconds);
            isRested.Value = true;
            restedSecondsRemaining.Value = duration;
        }

        /// <summary>
        /// SERVER ONLY: full runtime reset used on spawn/join for first pass.
        /// </summary>
        public void ServerResetRuntimeStateToDefaults()
        {
            if (!IsServer)
                return;

            runtimeFoods.Clear();
            activeFoodSlots.Clear();

            vitalityXpAccumulator = 0f;
            enduranceXpAccumulator = 0f;

            serverBusyUntil = 0d;
            currentRestZone = null;
            restWarmupStarted = false;

            isRested.Value = false;
            restedSecondsRemaining.Value = 0f;

            RecomputeDynamicMaxVitals();

            serverHealthExact = CurrentMaxHealth;
            serverStaminaExact = CurrentMaxStamina;
            currentHealth.Value = CurrentMaxHealth;
            currentStamina.Value = CurrentMaxStamina;

            foodSummaryRefreshTimer = 0f;
            serverInitialized = true;
            MirrorHealthBridgeIfPresent();
        }

        /// <summary>
        /// Used by HealthNet compatibility bridge to force full reset.
        /// </summary>
        public void ServerResetToFull()
        {
            if (!IsServer)
                return;

            RecomputeDynamicMaxVitals();
            serverHealthExact = CurrentMaxHealth;
            serverStaminaExact = CurrentMaxStamina;
            currentHealth.Value = CurrentMaxHealth;
            currentStamina.Value = CurrentMaxStamina;
            MirrorHealthBridgeIfPresent();
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestConsumeFoodFromSlotServerRpc(int slotIndex)
        {
            FoodConsumeResult result = ServerTryConsumeFoodFromSlot(slotIndex, out string consumedItemId);
            SendConsumeFoodResultToOwner(result, slotIndex, consumedItemId);
        }

        private FoodConsumeResult ServerTryConsumeFoodFromSlot(int slotIndex, out string consumedItemId)
        {
            consumedItemId = string.Empty;

            if (!IsServer || inventory == null)
                return FoodConsumeResult.InvalidRequest;

            if (!inventory.ServerIsValidSlotIndex(slotIndex))
                return FoodConsumeResult.SlotOutOfRange;

            if (inventory.ServerIsSlotEmpty(slotIndex))
                return FoodConsumeResult.SlotEmpty;

            if (!inventory.ServerTryGetSlotItem(slotIndex, out string itemId, out int qty, out _, out _))
                return FoodConsumeResult.InvalidRequest;

            if (qty <= 0 || string.IsNullOrWhiteSpace(itemId))
                return FoodConsumeResult.InvalidRequest;

            if (!inventory.ServerTryGetItemDef(itemId, out ItemDef def) || def == null)
                return FoodConsumeResult.ConfigError;

            if (!def.IsFood)
                return FoodConsumeResult.ItemNotFood;

            if (def.FoodDurationSeconds <= 0f ||
                def.FoodMaxHealthBonus < 0 ||
                def.FoodMaxStaminaBonus < 0 ||
                def.FoodHealthRegenBonus < 0f ||
                def.FoodStaminaRegenBonus < 0f)
            {
                return FoodConsumeResult.ConfigError;
            }

            if (HasActiveFood(itemId))
                return FoodConsumeResult.AlreadyActive;

            if (runtimeFoods.Count >= MaxActiveFoods)
                return FoodConsumeResult.NoFoodSlotAvailable;

            // Inventory mutation is authoritative and slot-index based.
            if (!inventory.ServerRemoveOneAtSlot(slotIndex, out string removedItemId, out _, out _))
                return FoodConsumeResult.InventoryRemoveFailed;

            if (!string.Equals(removedItemId, itemId, StringComparison.Ordinal))
            {
                // Safety net: if slot changed unexpectedly, put it back as best effort.
                inventory.ServerAddItem(removedItemId, 1);
                return FoodConsumeResult.InventoryRemoveFailed;
            }

            var runtime = new RuntimeFoodBuff
            {
                ItemId = itemId,
                MaxHealthBonus = Mathf.Max(0, def.FoodMaxHealthBonus),
                MaxStaminaBonus = Mathf.Max(0, def.FoodMaxStaminaBonus),
                HealthRegenBonus = Mathf.Max(0f, def.FoodHealthRegenBonus),
                StaminaRegenBonus = Mathf.Max(0f, def.FoodStaminaRegenBonus),
                RemainingSeconds = Mathf.Max(1f, def.FoodDurationSeconds),
                DurationSeconds = Mathf.Max(1f, def.FoodDurationSeconds)
            };

            runtimeFoods.Add(runtime);
            consumedItemId = itemId;

            // Consuming food increases max vitals, but does not auto-fill current vitals.
            RecomputeDynamicMaxVitals();
            serverHealthExact = Mathf.Min(serverHealthExact, CurrentMaxHealth);
            serverStaminaExact = Mathf.Min(serverStaminaExact, CurrentMaxStamina);
            currentHealth.Value = Mathf.Clamp(Mathf.FloorToInt(serverHealthExact), 0, CurrentMaxHealth);
            currentStamina.Value = Mathf.Clamp(Mathf.FloorToInt(serverStaminaExact), 0, CurrentMaxStamina);

            RebuildActiveFoodSlotReplication();
            MirrorHealthBridgeIfPresent();
            return FoodConsumeResult.None;
        }

        private bool HasActiveFood(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            for (int i = 0; i < runtimeFoods.Count; i++)
            {
                if (string.Equals(runtimeFoods[i].ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void TickRestedState(float dt)
        {
            if (currentRestZone != null && currentRestZone.GrantsRested)
            {
                if (restWarmupStarted)
                {
                    double elapsed = ServerTimeNow() - restZoneEnterServerTime;
                    if (elapsed >= currentRestZone.WarmupSeconds)
                    {
                        restWarmupStarted = false;
                        ServerRefreshRested(currentRestZone.RestedDurationSeconds);
                    }
                }
                else if (isRested.Value)
                {
                    // Staying in zone continuously keeps rested refreshed at full duration.
                    restedSecondsRemaining.Value = currentRestZone.RestedDurationSeconds;
                }

                return;
            }

            if (!isRested.Value)
                return;

            float remaining = Mathf.Max(0f, restedSecondsRemaining.Value - dt);
            restedSecondsRemaining.Value = remaining;
            if (remaining <= 0f)
            {
                isRested.Value = false;
                restedSecondsRemaining.Value = 0f;
            }
        }

        private void TickFoodBuffDurations(float dt)
        {
            if (runtimeFoods.Count == 0)
                return;

            bool removedAny = false;

            for (int i = runtimeFoods.Count - 1; i >= 0; i--)
            {
                RuntimeFoodBuff food = runtimeFoods[i];
                food.RemainingSeconds = Mathf.Max(0f, food.RemainingSeconds - dt);

                if (food.RemainingSeconds <= 0f)
                {
                    runtimeFoods.RemoveAt(i);
                    removedAny = true;
                    continue;
                }

                runtimeFoods[i] = food;
            }

            if (!removedAny)
                return;

            RecomputeDynamicMaxVitals();
            serverHealthExact = Mathf.Min(serverHealthExact, CurrentMaxHealth);
            serverStaminaExact = Mathf.Min(serverStaminaExact, CurrentMaxStamina);
            currentHealth.Value = Mathf.Clamp(Mathf.FloorToInt(serverHealthExact), 0, CurrentMaxHealth);
            currentStamina.Value = Mathf.Clamp(Mathf.FloorToInt(serverStaminaExact), 0, CurrentMaxStamina);

            RebuildActiveFoodSlotReplication();
            MirrorHealthBridgeIfPresent();
        }

        private void TickRegeneration(float dt)
        {
            float healthFoodFlat = 0f;
            float staminaFoodFlat = 0f;

            for (int i = 0; i < runtimeFoods.Count; i++)
            {
                healthFoodFlat += runtimeFoods[i].HealthRegenBonus;
                staminaFoodFlat += runtimeFoods[i].StaminaRegenBonus;
            }

            int vitalityLevel = skills != null ? Mathf.Clamp(skills.GetLevel(SkillId.Vitality), 0, 100) : 0;
            int enduranceLevel = skills != null ? Mathf.Clamp(skills.GetLevel(SkillId.Endurance), 0, 100) : 0;

            float vitalityBonus = vitalityLevel * HealthRegenPerVitalityLevel;
            float enduranceBonus = enduranceLevel * StaminaRegenPerEnduranceLevel;

            float restedHealthMult = isRested.Value ? RestedHealthRegenMultiplier : 1f;
            float restedStaminaMult = isRested.Value ? RestedStaminaRegenMultiplier : 1f;

            bool busy = ServerTimeNow() < serverBusyUntil;
            float busyHealthMult = busy ? BusyHealthRegenMultiplier : 1f;
            float busyStaminaMult = busy ? BusyStaminaRegenMultiplier : 1f;

            float finalHealthRegen = (BaseHealthRegenPerSecond + vitalityBonus + healthFoodFlat) * restedHealthMult * busyHealthMult;
            float finalStaminaRegen = (BaseStaminaRegenPerSecond + enduranceBonus + staminaFoodFlat) * restedStaminaMult * busyStaminaMult;

            finalHealthRegen = Mathf.Max(0f, finalHealthRegen);
            finalStaminaRegen = Mathf.Max(0f, finalStaminaRegen);

            float oldHealth = serverHealthExact;
            float oldStamina = serverStaminaExact;

            serverHealthExact = Mathf.Min(CurrentMaxHealth, serverHealthExact + finalHealthRegen * dt);
            serverStaminaExact = Mathf.Min(CurrentMaxStamina, serverStaminaExact + finalStaminaRegen * dt);

            float actualHealthRegenerated = Mathf.Max(0f, serverHealthExact - oldHealth);
            float actualStaminaRegenerated = Mathf.Max(0f, serverStaminaExact - oldStamina);

            currentHealth.Value = Mathf.Clamp(Mathf.FloorToInt(serverHealthExact), 0, CurrentMaxHealth);
            currentStamina.Value = Mathf.Clamp(Mathf.FloorToInt(serverStaminaExact), 0, CurrentMaxStamina);

            AwardRegenSkillXp(actualHealthRegenerated, actualStaminaRegenerated);
            MirrorHealthBridgeIfPresent();
        }

        private void AwardRegenSkillXp(float healthRegenerated, float staminaRegenerated)
        {
            if (skills == null)
                return;

            if (healthRegenerated > 0f)
            {
                vitalityXpAccumulator += healthRegenerated;
                while (vitalityXpAccumulator >= VitalityHpPerXp)
                {
                    vitalityXpAccumulator -= VitalityHpPerXp;
                    skills.AddXp(SkillId.Vitality, 1);
                }
            }

            if (staminaRegenerated > 0f)
            {
                enduranceXpAccumulator += staminaRegenerated;
                while (enduranceXpAccumulator >= EnduranceStaminaPerXp)
                {
                    enduranceXpAccumulator -= EnduranceStaminaPerXp;
                    skills.AddXp(SkillId.Endurance, 1);
                }
            }
        }

        private void RecomputeDynamicMaxVitals()
        {
            int bonusHealth = 0;
            int bonusStamina = 0;

            for (int i = 0; i < runtimeFoods.Count; i++)
            {
                bonusHealth += Mathf.Max(0, runtimeFoods[i].MaxHealthBonus);
                bonusStamina += Mathf.Max(0, runtimeFoods[i].MaxStaminaBonus);
            }

            currentMaxHealth.Value = Mathf.Max(1, BaseMaxHealth + bonusHealth);
            currentMaxStamina.Value = Mathf.Max(1, BaseMaxStamina + bonusStamina);
        }

        private void RefreshFoodSummaryIfNeeded(float dt)
        {
            if (runtimeFoods.Count == 0)
            {
                if (activeFoodSlots.Count > 0)
                    activeFoodSlots.Clear();
                return;
            }

            foodSummaryRefreshTimer -= dt;
            if (foodSummaryRefreshTimer > 0f)
                return;

            foodSummaryRefreshTimer = Mathf.Max(0.1f, foodSummaryRefreshSeconds);
            RebuildActiveFoodSlotReplication();
        }

        private void RebuildActiveFoodSlotReplication()
        {
            activeFoodSlots.Clear();

            for (int i = 0; i < runtimeFoods.Count && i < MaxActiveFoods; i++)
            {
                RuntimeFoodBuff src = runtimeFoods[i];
                var net = new ActiveFoodBuff
                {
                    ItemId = new FixedString64Bytes(src.ItemId ?? string.Empty),
                    MaxHealthBonus = src.MaxHealthBonus,
                    MaxStaminaBonus = src.MaxStaminaBonus,
                    HealthRegenBonus = src.HealthRegenBonus,
                    StaminaRegenBonus = src.StaminaRegenBonus,
                    RemainingSeconds = Mathf.Max(0f, Mathf.Ceil(src.RemainingSeconds))
                };

                activeFoodSlots.Add(net);
            }
        }

        private void MirrorHealthBridgeIfPresent()
        {
            if (!IsServer)
                return;

            if (healthBridge == null)
                healthBridge = GetComponent<HealthNet>();

            healthBridge?.ServerMirrorFromVitals(CurrentHealth, CurrentMaxHealth);
        }

        private void SendConsumeFoodResultToOwner(FoodConsumeResult result, int slotIndex, string itemId)
        {
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            ConsumeFoodResultClientRpc((int)result, slotIndex, itemId ?? string.Empty, rpcParams);
        }

        [ClientRpc]
        private void ConsumeFoodResultClientRpc(int resultValue, int slotIndex, string itemId, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            FoodConsumeResult result = Enum.IsDefined(typeof(FoodConsumeResult), resultValue)
                ? (FoodConsumeResult)resultValue
                : FoodConsumeResult.InvalidRequest;

            OnConsumeFoodResult?.Invoke(result, slotIndex);

            Debug.Log($"[Vitals][CLIENT] ConsumeFood result={result} slot={slotIndex} item={itemId}", this);
        }

        private void OnAnyVitalValueChanged(int previousValue, int newValue)
        {
            OnVitalsChanged?.Invoke();
        }

        private void OnRestedFlagChanged(bool previousValue, bool newValue)
        {
            OnVitalsChanged?.Invoke();
        }

        private void OnRestedSecondsChanged(float previousValue, float newValue)
        {
            OnVitalsChanged?.Invoke();
        }

        private void OnActiveFoodSlotsChanged(NetworkListEvent<ActiveFoodBuff> changeEvent)
        {
            OnVitalsChanged?.Invoke();
        }

        private static double ServerTimeNow()
        {
            if (NetworkManager.Singleton == null)
                return Time.timeAsDouble;

            return NetworkManager.Singleton.ServerTime.Time;
        }
    }
}
