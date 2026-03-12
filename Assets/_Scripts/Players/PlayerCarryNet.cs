using System;
using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Stats;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    public enum EncumbranceTier
    {
        Normal,
        Heavy,
        VeryHeavy,
        Overloaded
    }

    /// <summary>
    /// Server-authoritative carry weight and encumbrance state.
    ///
    /// This component recalculates carry state from authoritative sources:
    /// - inventory contents
    /// - equipped gear
    /// - effective Strength
    ///
    /// Clients only read the replicated results for UI/presentation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerCarryNet : NetworkBehaviour
    {
        public const float BaseCarryWeight = 40f;
        public const float CarryPerStrength = 5f;

        private static readonly EquipSlot[] EquippedSlots =
        {
            EquipSlot.MainHand,
            EquipSlot.OffHand,
            EquipSlot.Helmet,
            EquipSlot.Chest,
            EquipSlot.Legs,
            EquipSlot.Boots,
            EquipSlot.Gloves,
            EquipSlot.Shoulders,
            EquipSlot.Belt
        };

        [Header("Dependencies")]
        [Tooltip("Authoritative player inventory used to count carried item weight.")]
        [SerializeField] private PlayerInventoryNet inventory;

        [Tooltip("Authoritative equipment used to count worn gear and hand items.")]
        [SerializeField] private PlayerEquipmentNet equipment;

        [Tooltip("Canonical stats provider used to read effective Strength.")]
        [SerializeField] private ActorStatsProvider statsProvider;

        private readonly NetworkVariable<float> currentCarryWeight =
            new(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> maxCarryWeight =
            new(BaseCarryWeight, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<EncumbranceTier> currentEncumbranceTier =
            new(EncumbranceTier.Normal, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> currentMovementMultiplier =
            new(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly HashSet<string> warnedMissingInventoryItems = new(StringComparer.Ordinal);
        private readonly HashSet<string> warnedMissingEquipmentItems = new(StringComparer.Ordinal);

        public float CurrentCarryWeight => Mathf.Max(0f, currentCarryWeight.Value);
        public float MaxCarryWeight => Mathf.Max(0f, maxCarryWeight.Value);
        public EncumbranceTier CurrentEncumbranceTier => currentEncumbranceTier.Value;
        public float CurrentMovementMultiplier => Mathf.Clamp(currentMovementMultiplier.Value, 0f, 1f);

        public NetworkVariable<float> CurrentCarryWeightNetVar => currentCarryWeight;
        public NetworkVariable<float> MaxCarryWeightNetVar => maxCarryWeight;
        public NetworkVariable<EncumbranceTier> CurrentEncumbranceTierNetVar => currentEncumbranceTier;
        public NetworkVariable<float> CurrentMovementMultiplierNetVar => currentMovementMultiplier;

        private void Awake()
        {
            AutoBindReferences();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoBindReferences();
        }
#endif

        public override void OnNetworkSpawn()
        {
            SubscribeToAuthoritativeSources();

            if (IsServer)
                ServerRecalculateCarryState();
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeFromAuthoritativeSources();
        }

        /// <summary>
        /// SERVER ONLY: recomputes carry weight, capacity, encumbrance tier, and movement multiplier.
        /// </summary>
        public void ServerRecalculateCarryState()
        {
            if (!IsServer)
                return;

            float nextCurrent = ComputeCurrentCarryWeight();
            float nextMax = ComputeMaxCarryWeight();
            EncumbranceTier nextTier = ResolveTier(nextCurrent, nextMax);
            float nextMovementMultiplier = ResolveMovementMultiplier(nextTier);

            currentCarryWeight.Value = nextCurrent;
            maxCarryWeight.Value = nextMax;
            currentEncumbranceTier.Value = nextTier;
            currentMovementMultiplier.Value = nextMovementMultiplier;
        }

        private void HandleAuthoritativeContentsChanged()
        {
            if (!IsServer)
                return;

            ServerRecalculateCarryState();
        }

        private float ComputeCurrentCarryWeight()
        {
            float total = 0f;
            total += ComputeInventoryWeight();
            total += ComputeEquippedWeight();
            return Mathf.Max(0f, total);
        }

        private float ComputeMaxCarryWeight()
        {
            int strength = 0;
            if (statsProvider != null)
                strength = Mathf.Max(0, statsProvider.GetEffectiveStats().Strength);

            return BaseCarryWeight + (strength * CarryPerStrength);
        }

        private float ComputeInventoryWeight()
        {
            if (inventory == null || inventory.Grid == null)
                return 0f;

            float total = 0f;
            InventorySlot[] slots = inventory.Grid.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty)
                    continue;

                string itemId;
                int quantity;

                if (slot.ContentType == InventorySlotContentType.Instance)
                {
                    itemId = slot.Instance.ItemId;
                    quantity = 1;
                }
                else
                {
                    itemId = slot.Stack.ItemId;
                    quantity = Mathf.Max(1, slot.Stack.Quantity);
                }

                if (!TryResolveInventoryItemWeight(itemId, out float unitWeight))
                    continue;

                total += unitWeight * quantity;
            }

            return total;
        }

        private float ComputeEquippedWeight()
        {
            if (equipment == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < EquippedSlots.Length; i++)
            {
                EquipSlot slot = EquippedSlots[i];

                // Hand slots can reference inventory items instead of owning their own payload.
                // Those already count through inventory, so skip them here to avoid double-counting.
                if (equipment.IsReferenceEquipSlot(slot) && equipment.GetReferenceInventorySlotIndex(slot) >= 0)
                    continue;

                string itemId = equipment.GetEquippedItemId(slot);
                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                if (!TryResolveEquipmentItemWeight(itemId, out float unitWeight))
                    continue;

                total += unitWeight;
            }

            return total;
        }

        private bool TryResolveInventoryItemWeight(string itemId, out float unitWeight)
        {
            unitWeight = 0f;

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (inventory != null && inventory.ServerTryGetItemDef(itemId, out ItemDef def) && def != null)
            {
                unitWeight = Mathf.Max(0f, def.Weight);
                return true;
            }

            if (warnedMissingInventoryItems.Add(itemId))
                Debug.LogWarning($"[Carry][SERVER] Missing ItemDef while calculating inventory weight. owner={OwnerClientId} itemId={itemId}", this);

            return false;
        }

        private bool TryResolveEquipmentItemWeight(string itemId, out float unitWeight)
        {
            unitWeight = 0f;

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (equipment != null && equipment.TryGetItemDef(itemId, out ItemDef def) && def != null)
            {
                unitWeight = Mathf.Max(0f, def.Weight);
                return true;
            }

            if (inventory != null && inventory.ServerTryGetItemDef(itemId, out def) && def != null)
            {
                unitWeight = Mathf.Max(0f, def.Weight);
                return true;
            }

            if (warnedMissingEquipmentItems.Add(itemId))
                Debug.LogWarning($"[Carry][SERVER] Missing ItemDef while calculating equipped weight. owner={OwnerClientId} itemId={itemId}", this);

            return false;
        }

        private static EncumbranceTier ResolveTier(float current, float max)
        {
            float safeMax = Mathf.Max(0.0001f, max);
            float ratio = current / safeMax;

            if (ratio <= 1f)
                return EncumbranceTier.Normal;

            if (ratio <= 1.2f)
                return EncumbranceTier.Heavy;

            if (ratio <= 1.5f)
                return EncumbranceTier.VeryHeavy;

            return EncumbranceTier.Overloaded;
        }

        private static float ResolveMovementMultiplier(EncumbranceTier tier)
        {
            return tier switch
            {
                EncumbranceTier.Heavy => 0.75f,
                EncumbranceTier.VeryHeavy => 0.50f,
                EncumbranceTier.Overloaded => 0f,
                _ => 1f
            };
        }

        private void SubscribeToAuthoritativeSources()
        {
            AutoBindReferences();

            if (inventory != null)
                inventory.OnServerInventoryChanged += HandleAuthoritativeContentsChanged;

            if (equipment != null)
                equipment.OnEquipmentChanged += HandleAuthoritativeContentsChanged;
        }

        private void UnsubscribeFromAuthoritativeSources()
        {
            if (inventory != null)
                inventory.OnServerInventoryChanged -= HandleAuthoritativeContentsChanged;

            if (equipment != null)
                equipment.OnEquipmentChanged -= HandleAuthoritativeContentsChanged;
        }

        private void AutoBindReferences()
        {
            if (inventory == null)
                inventory = GetComponent<PlayerInventoryNet>();

            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            if (statsProvider == null)
                statsProvider = GetComponent<ActorStatsProvider>();
        }
    }
}
