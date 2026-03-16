using System;
using System.Text;
using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Persistence;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// PlayerEquipmentNet (MVP)
    /// --------------------------------------------------------------------
    /// Server-authoritative equipment that replicates to ALL clients.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerEquipmentNet : NetworkBehaviour
    {
        [Header("References")]
        [Tooltip("Item database used to resolve equip rules and tool tags.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Tooltip("PlayerInventoryNet on the same player object.")]
        [SerializeField] private PlayerInventoryNet inventoryNet;

        private readonly NetworkVariable<FixedString64Bytes> mainHand = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> offHand = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> head = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> chest = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> legs = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> feet = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> gloves = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> shoulders = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> belt = new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // Hybrid model references for hand slots: points to hotbar inventory index or -1 when not reference-equipped.
        private readonly NetworkVariable<int> mainHandInventorySlotRef = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> offHandInventorySlotRef = new(-1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Server writes durability, all clients read for UI.
        private readonly NetworkVariable<int> mainHandDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> offHandDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> headDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> chestDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> legsDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> feetDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> glovesDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> shouldersDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> beltDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Per-slot crafted instance attribute bonuses (server writes, all clients read).
        private readonly NetworkVariable<int> mainHandBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> mainHandBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> mainHandBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> offHandBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> offHandBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> offHandBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> headBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> headBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> headBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> chestBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> chestBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> chestBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> legsBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> legsBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> legsBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> feetBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> feetBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> feetBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> glovesBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> glovesBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> glovesBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> shouldersBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> shouldersBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> shouldersBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> beltBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> beltBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> beltBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Per-slot maker marks. Empty when item was not crafted by a player.
        private readonly NetworkVariable<FixedString64Bytes> mainHandCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> offHandCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> headCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> chestCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> legsCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> feetCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> glovesCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> shouldersCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> beltCraftedBy = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Full replicated crafted-instance payload per equipped slot.
        private readonly NetworkVariable<ItemInstanceData> mainHandInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> offHandInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> headInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> chestInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> legsInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> feetInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> glovesInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> shouldersInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ItemInstanceData> beltInstanceData = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action OnEquipmentChanged;

        public NetworkVariable<FixedString64Bytes> MainHandNetVar => mainHand;
        public NetworkVariable<FixedString64Bytes> OffHandNetVar => offHand;
        public NetworkVariable<FixedString64Bytes> HelmetNetVar => head;
        [Obsolete("Use HelmetNetVar")]
        public NetworkVariable<FixedString64Bytes> HeadNetVar => head;
        public NetworkVariable<FixedString64Bytes> ChestNetVar => chest;
        public NetworkVariable<FixedString64Bytes> LegsNetVar => legs;
        public NetworkVariable<FixedString64Bytes> BootsNetVar => feet;
        public NetworkVariable<FixedString64Bytes> GlovesNetVar => gloves;
        public NetworkVariable<FixedString64Bytes> ShouldersNetVar => shoulders;
        public NetworkVariable<FixedString64Bytes> BeltNetVar => belt;
        [Obsolete("Use BootsNetVar")]
        public NetworkVariable<FixedString64Bytes> FeetNetVar => feet;

        public NetworkVariable<int> MainHandDurabilityNetVar => mainHandDurability;
        public NetworkVariable<int> OffHandDurabilityNetVar => offHandDurability;
        public NetworkVariable<int> HelmetDurabilityNetVar => headDurability;
        [Obsolete("Use HelmetDurabilityNetVar")]
        public NetworkVariable<int> HeadDurabilityNetVar => headDurability;
        public NetworkVariable<int> ChestDurabilityNetVar => chestDurability;
        public NetworkVariable<int> LegsDurabilityNetVar => legsDurability;
        public NetworkVariable<int> BootsDurabilityNetVar => feetDurability;
        public NetworkVariable<int> GlovesDurabilityNetVar => glovesDurability;
        public NetworkVariable<int> ShouldersDurabilityNetVar => shouldersDurability;
        public NetworkVariable<int> BeltDurabilityNetVar => beltDurability;
        [Obsolete("Use BootsDurabilityNetVar")]
        public NetworkVariable<int> FeetDurabilityNetVar => feetDurability;
        public NetworkVariable<int> MainHandInventorySlotRefNetVar => mainHandInventorySlotRef;
        public NetworkVariable<int> OffHandInventorySlotRefNetVar => offHandInventorySlotRef;

        public string GetMainHandItemId() => mainHand.Value.ToString();

        public string GetEquippedItemId(EquipSlot slot)
        {
            return slot switch
            {
                EquipSlot.MainHand => mainHand.Value.ToString(),
                EquipSlot.OffHand => offHand.Value.ToString(),
                EquipSlot.Helmet => head.Value.ToString(),
                EquipSlot.Chest => chest.Value.ToString(),
                EquipSlot.Legs => legs.Value.ToString(),
                EquipSlot.Boots => feet.Value.ToString(),
                EquipSlot.Gloves => gloves.Value.ToString(),
                EquipSlot.Shoulders => shoulders.Value.ToString(),
                EquipSlot.Belt => belt.Value.ToString(),
                _ => string.Empty
            };
        }

        public int GetEquippedDurability(EquipSlot slot)
        {
            return slot switch
            {
                EquipSlot.MainHand => mainHandDurability.Value,
                EquipSlot.OffHand => offHandDurability.Value,
                EquipSlot.Helmet => headDurability.Value,
                EquipSlot.Chest => chestDurability.Value,
                EquipSlot.Legs => legsDurability.Value,
                EquipSlot.Boots => feetDurability.Value,
                EquipSlot.Gloves => glovesDurability.Value,
                EquipSlot.Shoulders => shouldersDurability.Value,
                EquipSlot.Belt => beltDurability.Value,
                _ => 0
            };
        }
        /// <summary>
        /// True when this equip slot is reference-bound to a hotbar inventory slot
        /// instead of owning moved item data.
        /// </summary>
        public bool IsReferenceEquipSlot(EquipSlot slot)
        {
            return slot == EquipSlot.MainHand || slot == EquipSlot.OffHand;
        }

        public int GetReferenceInventorySlotIndex(EquipSlot slot)
        {
            return slot switch
            {
                EquipSlot.MainHand => mainHandInventorySlotRef.Value,
                EquipSlot.OffHand => offHandInventorySlotRef.Value,
                _ => -1
            };
        }

        public bool IsInventorySlotReferenceEquipped(int inventorySlotIndex)
        {
            if (inventorySlotIndex < 0)
                return false;

            return mainHandInventorySlotRef.Value == inventorySlotIndex
                || offHandInventorySlotRef.Value == inventorySlotIndex;
        }

        /// <summary>
        /// Server-side guard for inventory mutation paths.
        /// MVP rule: reference-equipped hotbar items are locked until unequipped.
        /// </summary>
        public bool ServerIsInventorySlotLockedByReferenceEquip(int inventorySlotIndex)
        {
            if (!IsServer)
                return false;

            return IsInventorySlotReferenceEquipped(inventorySlotIndex);
        }

        public int GetEquippedBonusStrength(EquipSlot slot) => GetEquippedBonusData(slot).BonusStrength;
        public int GetEquippedBonusDexterity(EquipSlot slot) => GetEquippedBonusData(slot).BonusDexterity;
        public int GetEquippedBonusIntelligence(EquipSlot slot) => GetEquippedBonusData(slot).BonusIntelligence;
        public string GetEquippedCraftedBy(EquipSlot slot) => GetEquippedBonusData(slot).CraftedBy.ToString();
        public ItemInstanceData GetEquippedInstanceData(EquipSlot slot) => GetEquippedBonusData(slot);

        /// <summary>
        /// SERVER: exports authoritative equipment state for persistence.
        /// Hybrid model:
        /// - Armor slots save moved-equipment item payload.
        /// - Hand slots save inventory reference indices (+ expected item ids) only.
        /// </summary>
        public PlayerEquipmentSaveData ServerExportSaveData()
        {
            var save = new PlayerEquipmentSaveData
            {
                helmet = ExportMovedSlotSave(EquipSlot.Helmet),
                chest = ExportMovedSlotSave(EquipSlot.Chest),
                legs = ExportMovedSlotSave(EquipSlot.Legs),
                boots = ExportMovedSlotSave(EquipSlot.Boots),
                gloves = ExportMovedSlotSave(EquipSlot.Gloves),
                shoulders = ExportMovedSlotSave(EquipSlot.Shoulders),
                belt = ExportMovedSlotSave(EquipSlot.Belt),
                mainHandInventorySlotRef = mainHandInventorySlotRef.Value,
                offHandInventorySlotRef = offHandInventorySlotRef.Value,
                mainHandExpectedItemId = GetReferenceExpectedItemId(EquipSlot.MainHand),
                offHandExpectedItemId = GetReferenceExpectedItemId(EquipSlot.OffHand)
            };

            return save;
        }

        /// <summary>
        /// SERVER: restores authoritative equipment state from persistence after inventory load.
        /// This method intentionally performs full validation and clears invalid rows safely.
        /// </summary>
        public void ServerApplySaveData(PlayerEquipmentSaveData saveData)
        {
            if (!IsServer)
                return;

            // Hard reset first so stale runtime state never leaks through partial saves.
            ServerClearAllEquipmentState();

            if (saveData == null)
            {
                ServerValidateReferenceAssignments();
                OnEquipmentChanged?.Invoke();
                return;
            }

            // Restore moved equipment (armor/worn slots).
            ApplyMovedSlotSave(EquipSlot.Helmet, saveData.helmet);
            ApplyMovedSlotSave(EquipSlot.Chest, saveData.chest);
            ApplyMovedSlotSave(EquipSlot.Legs, saveData.legs);
            ApplyMovedSlotSave(EquipSlot.Boots, saveData.boots);
            ApplyMovedSlotSave(EquipSlot.Gloves, saveData.gloves);
            ApplyMovedSlotSave(EquipSlot.Shoulders, saveData.shoulders);
            ApplyMovedSlotSave(EquipSlot.Belt, saveData.belt);

            // Restore reference-equip hand slots from inventory references.
            ApplyReferenceSlotSave(EquipSlot.MainHand, saveData.mainHandInventorySlotRef, saveData.mainHandExpectedItemId);
            ApplyReferenceSlotSave(EquipSlot.OffHand, saveData.offHandInventorySlotRef, saveData.offHandExpectedItemId);

            // Final server-side canonical validation pass.
            ServerValidateReferenceAssignments();
            OnEquipmentChanged?.Invoke();
        }

        /// <summary>
        /// True if any equipped item satisfies the requested tool category (server authoritative).
        /// </summary>
        public bool HasEquippedToolType(ToolType requiredType)
        {
            if (requiredType == ToolType.None)
                return true;

            var tag = ConvertToToolTag(requiredType);
            if (tag == ToolTag.None)
                return false;

            return HasToolTag(tag);
        }

        public bool HasEquippedItem(string itemId)
        {
            var canonical = CanonicalizeItemId(itemId);
            if (string.IsNullOrEmpty(canonical))
                return false;

            return SlotMatches(mainHand.Value, canonical)
                || SlotMatches(offHand.Value, canonical)
                || SlotMatches(head.Value, canonical)
                || SlotMatches(chest.Value, canonical)
                || SlotMatches(legs.Value, canonical)
                || SlotMatches(feet.Value, canonical)
                || SlotMatches(gloves.Value, canonical)
                || SlotMatches(shoulders.Value, canonical)
                || SlotMatches(belt.Value, canonical);
        }

        public bool HasToolTag(ToolTag tag)
        {
            if (tag == ToolTag.None)
                return false;

            if (!EnsureItemDatabase())
                return false;

            return EquippedItemHasToolTag(mainHand.Value, tag)
                || EquippedItemHasToolTag(offHand.Value, tag)
                || EquippedItemHasToolTag(head.Value, tag)
                || EquippedItemHasToolTag(chest.Value, tag)
                || EquippedItemHasToolTag(legs.Value, tag)
                || EquippedItemHasToolTag(feet.Value, tag)
                || EquippedItemHasToolTag(gloves.Value, tag)
                || EquippedItemHasToolTag(shoulders.Value, tag)
                || EquippedItemHasToolTag(belt.Value, tag);
        }

        private bool EquippedItemHasToolTag(FixedString64Bytes itemIdFs, ToolTag tag)
        {
            if (!EnsureItemDatabase())
                return false;

            var id = itemIdFs.ToString();
            if (string.IsNullOrWhiteSpace(id))
                return false;

            if (!itemDatabase.TryGet(id, out var def) || def == null)
                return false;

            var tags = def.ToolTags;
            if (tags == null || tags.Length == 0)
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == tag)
                    return true;
            }

            return false;
        }

        private static ToolTag ConvertToToolTag(ToolType toolType)
        {
            return toolType switch
            {
                ToolType.Axe => ToolTag.Axe,
                ToolType.Pickaxe => ToolTag.Pickaxe,
                ToolType.Sickle => ToolTag.Sickle,
                ToolType.Knife => ToolTag.Knife,
                _ => ToolTag.None
            };
        }

        public override void OnNetworkSpawn()
        {
            if (inventoryNet == null) inventoryNet = GetComponent<PlayerInventoryNet>();

            mainHand.OnValueChanged += OnAnySlotChanged;
            offHand.OnValueChanged += OnAnySlotChanged;
            head.OnValueChanged += OnAnySlotChanged;
            chest.OnValueChanged += OnAnySlotChanged;
            legs.OnValueChanged += OnAnySlotChanged;
            feet.OnValueChanged += OnAnySlotChanged;
            gloves.OnValueChanged += OnAnySlotChanged;
            shoulders.OnValueChanged += OnAnySlotChanged;
            belt.OnValueChanged += OnAnySlotChanged;
            mainHandInventorySlotRef.OnValueChanged += OnAnyReferenceSlotChanged;
            offHandInventorySlotRef.OnValueChanged += OnAnyReferenceSlotChanged;

            mainHandDurability.OnValueChanged += OnAnyDurabilityChanged;
            offHandDurability.OnValueChanged += OnAnyDurabilityChanged;
            headDurability.OnValueChanged += OnAnyDurabilityChanged;
            chestDurability.OnValueChanged += OnAnyDurabilityChanged;
            legsDurability.OnValueChanged += OnAnyDurabilityChanged;
            feetDurability.OnValueChanged += OnAnyDurabilityChanged;
            glovesDurability.OnValueChanged += OnAnyDurabilityChanged;
            shouldersDurability.OnValueChanged += OnAnyDurabilityChanged;
            beltDurability.OnValueChanged += OnAnyDurabilityChanged;

            mainHandBonusStrength.OnValueChanged += OnAnyBonusChanged;
            mainHandBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            mainHandBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            offHandBonusStrength.OnValueChanged += OnAnyBonusChanged;
            offHandBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            offHandBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            headBonusStrength.OnValueChanged += OnAnyBonusChanged;
            headBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            headBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            chestBonusStrength.OnValueChanged += OnAnyBonusChanged;
            chestBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            chestBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            legsBonusStrength.OnValueChanged += OnAnyBonusChanged;
            legsBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            legsBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            feetBonusStrength.OnValueChanged += OnAnyBonusChanged;
            feetBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            feetBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            glovesBonusStrength.OnValueChanged += OnAnyBonusChanged;
            glovesBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            glovesBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            shouldersBonusStrength.OnValueChanged += OnAnyBonusChanged;
            shouldersBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            shouldersBonusIntelligence.OnValueChanged += OnAnyBonusChanged;
            beltBonusStrength.OnValueChanged += OnAnyBonusChanged;
            beltBonusDexterity.OnValueChanged += OnAnyBonusChanged;
            beltBonusIntelligence.OnValueChanged += OnAnyBonusChanged;

            mainHandCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            offHandCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            headCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            chestCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            legsCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            feetCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            glovesCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            shouldersCraftedBy.OnValueChanged += OnAnyCraftedByChanged;
            beltCraftedBy.OnValueChanged += OnAnyCraftedByChanged;

            mainHandInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            offHandInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            headInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            chestInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            legsInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            feetInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            glovesInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            shouldersInstanceData.OnValueChanged += OnAnyInstanceDataChanged;
            beltInstanceData.OnValueChanged += OnAnyInstanceDataChanged;

            if (IsServer)
                ServerValidateReferenceAssignments();

            OnEquipmentChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            mainHand.OnValueChanged -= OnAnySlotChanged;
            offHand.OnValueChanged -= OnAnySlotChanged;
            head.OnValueChanged -= OnAnySlotChanged;
            chest.OnValueChanged -= OnAnySlotChanged;
            legs.OnValueChanged -= OnAnySlotChanged;
            feet.OnValueChanged -= OnAnySlotChanged;
            gloves.OnValueChanged -= OnAnySlotChanged;
            shoulders.OnValueChanged -= OnAnySlotChanged;
            belt.OnValueChanged -= OnAnySlotChanged;
            mainHandInventorySlotRef.OnValueChanged -= OnAnyReferenceSlotChanged;
            offHandInventorySlotRef.OnValueChanged -= OnAnyReferenceSlotChanged;

            mainHandDurability.OnValueChanged -= OnAnyDurabilityChanged;
            offHandDurability.OnValueChanged -= OnAnyDurabilityChanged;
            headDurability.OnValueChanged -= OnAnyDurabilityChanged;
            chestDurability.OnValueChanged -= OnAnyDurabilityChanged;
            legsDurability.OnValueChanged -= OnAnyDurabilityChanged;
            feetDurability.OnValueChanged -= OnAnyDurabilityChanged;
            glovesDurability.OnValueChanged -= OnAnyDurabilityChanged;
            shouldersDurability.OnValueChanged -= OnAnyDurabilityChanged;
            beltDurability.OnValueChanged -= OnAnyDurabilityChanged;

            mainHandBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            mainHandBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            mainHandBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            offHandBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            offHandBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            offHandBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            headBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            headBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            headBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            chestBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            chestBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            chestBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            legsBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            legsBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            legsBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            feetBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            feetBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            feetBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            glovesBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            glovesBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            glovesBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            shouldersBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            shouldersBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            shouldersBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;
            beltBonusStrength.OnValueChanged -= OnAnyBonusChanged;
            beltBonusDexterity.OnValueChanged -= OnAnyBonusChanged;
            beltBonusIntelligence.OnValueChanged -= OnAnyBonusChanged;

            mainHandCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            offHandCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            headCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            chestCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            legsCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            feetCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            glovesCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            shouldersCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;
            beltCraftedBy.OnValueChanged -= OnAnyCraftedByChanged;

            mainHandInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            offHandInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            headInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            chestInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            legsInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            feetInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            glovesInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            shouldersInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
            beltInstanceData.OnValueChanged -= OnAnyInstanceDataChanged;
        }

        private void OnAnySlotChanged(FixedString64Bytes prev, FixedString64Bytes next)
        {
            if (IsServer)
                ServerValidateReferenceAssignments();

            OnEquipmentChanged?.Invoke();
        }

        private void OnAnyDurabilityChanged(int prev, int next)
        {
            OnEquipmentChanged?.Invoke();
        }

        private void OnAnyBonusChanged(int prev, int next)
        {
            OnEquipmentChanged?.Invoke();
        }

        private void OnAnyCraftedByChanged(FixedString64Bytes prev, FixedString64Bytes next)
        {
            OnEquipmentChanged?.Invoke();
        }

        private void OnAnyInstanceDataChanged(ItemInstanceData previous, ItemInstanceData next)
        {
            OnEquipmentChanged?.Invoke();
        }

        private void OnAnyReferenceSlotChanged(int previous, int next)
        {
            if (IsServer)
                ServerValidateReferenceAssignments();

            OnEquipmentChanged?.Invoke();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestEquipByItemIdServerRpc(FixedString64Bytes itemIdFs)
        {
            if (!IsServer)
                return;

            string requestedId = itemIdFs.ToString();
            string itemId = CanonicalizeItemId(requestedId);
            if (string.IsNullOrWhiteSpace(itemId) || inventoryNet == null)
                return;

            // Legacy fallback path: choose first matching inventory slot.
            if (!inventoryNet.ServerTryFindFirstSlotWithItem(itemId, out int slotIndex))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: item not in inventory. itemId={itemId} requested={requestedId}");
                return;
            }

            ServerTryEquipFromInventorySlot(slotIndex);
        }

        /// <summary>
        /// New authoritative equip entrypoint using inventory slot index.
        /// Required for preserving per-slot durability.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        public void RequestEquipFromInventorySlotServerRpc(int inventorySlotIndex)
        {
            if (!IsServer)
                return;

            ServerTryEquipFromInventorySlot(inventorySlotIndex);
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestUnequipSlotServerRpc(EquipSlot slot, int preferredInventoryIndex = -1)
        {
            if (!IsServer || inventoryNet == null) return;

            var equippedId = GetEquippedItemId(slot);
            if (string.IsNullOrWhiteSpace(equippedId))
                return;

            if (itemDatabase != null && itemDatabase.TryGet(equippedId, out var def) && def != null && def.Handedness == Handedness.BothHands)
            {
                var otherSlot = slot == EquipSlot.MainHand ? EquipSlot.OffHand : EquipSlot.MainHand;
                if (!TryServerUnequipIfNeeded(slot, preferredInventoryIndex)) return;
                TryServerUnequipIfNeeded(otherSlot);
                return;
            }

            TryServerUnequipIfNeeded(slot, preferredInventoryIndex);
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestSwapEquipSlotsServerRpc(EquipSlot fromSlot, EquipSlot toSlot)
        {
            if (!IsServer) return;
            if (fromSlot == EquipSlot.None || toSlot == EquipSlot.None || fromSlot == toSlot) return;

            var fromItemId = GetEquippedItemId(fromSlot);
            if (string.IsNullOrWhiteSpace(fromItemId)) return;

            if (!ValidateCommon(fromItemId, out var fromDef)) return;
            if (!DoesSlotAcceptItem(toSlot, fromDef)) return;

            var toItemId = GetEquippedItemId(toSlot);
            ItemDef toDef = null;
            if (!string.IsNullOrWhiteSpace(toItemId))
            {
                if (!ValidateCommon(toItemId, out toDef))
                    return;

                if (!DoesSlotAcceptItem(fromSlot, toDef))
                    return;
            }

            if (fromDef.Handedness == Handedness.BothHands)
            {
                Debug.LogWarning("[Equipment][SERVER] Swap denied: moving two-hand items between slots is unsupported.");
                return;
            }

            if (toDef != null && toDef.Handedness == Handedness.BothHands)
            {
                Debug.LogWarning("[Equipment][SERVER] Swap denied: target slot holds a two-hand item.");
                return;
            }

            int fromDurability = GetEquippedDurability(fromSlot);
            int toDurability = GetEquippedDurability(toSlot);
            ItemInstanceData fromBonus = GetEquippedBonusData(fromSlot);
            ItemInstanceData toBonus = GetEquippedBonusData(toSlot);

            SetSlot(toSlot, fromItemId, fromDurability, fromBonus);
            SetSlot(fromSlot, toItemId, toDurability, toBonus);
        }

        /// <summary>
        /// Server-only durability damage helper for combat swings.
        /// </summary>
        public bool ServerDamageMainHandDurability(int amount, out bool broke, out string brokenItemId)
        {
            return ServerDamageEquippedDurability(EquipSlot.MainHand, amount, out broke, out brokenItemId);
        }

        /// <summary>
        /// Server-only durability damage helper for harvesting/combat based on specific equipped item id.
        /// </summary>
        public bool ServerDamageDurabilityForEquippedItem(string equippedItemId, int amount, out bool broke, out string brokenItemId)
        {
            broke = false;
            brokenItemId = string.Empty;

            if (!IsServer || string.IsNullOrWhiteSpace(equippedItemId))
                return false;

            EquipSlot[] order =
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

            for (int i = 0; i < order.Length; i++)
            {
                var slot = order[i];
                if (!string.Equals(GetEquippedItemId(slot), equippedItemId, StringComparison.Ordinal))
                    continue;

                return ServerDamageEquippedDurability(slot, amount, out broke, out brokenItemId);
            }

            return false;
        }

        public bool ServerDamageEquippedDurability(EquipSlot slot, int amount, out bool broke, out string brokenItemId)
        {
            broke = false;
            brokenItemId = string.Empty;

            if (!IsServer || amount <= 0)
                return false;

            string itemId = GetEquippedItemId(slot);
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (!ValidateCommon(itemId, out var def))
                return false;

            // Hybrid durability rule:
            // - Reference-equipped hand slots must damage the authoritative inventory item.
            // - Moved equipment slots keep damaging equipment storage durability.
            int referenceIndex = GetReferenceInventorySlotIndex(slot);
            if (IsReferenceEquipSlot(slot) && referenceIndex >= 0)
            {
                if (inventoryNet == null)
                    return false;

                if (!inventoryNet.ServerDamageDurabilityAtSlot(referenceIndex, amount, out broke))
                    return false;

                brokenItemId = broke ? itemId : string.Empty;

                // Re-sync replicated hand slot data (and clear stale refs if item broke/changed).
                ServerValidateReferenceAssignments();

                if (broke)
                    Debug.Log($"[Durability] Reference item broke item={itemId} slot={slot} invIndex={referenceIndex} owner={OwnerClientId}", this);
                else
                    Debug.Log($"[Durability] Used reference item={itemId} slot={slot} invIndex={referenceIndex}", this);

                return true;
            }

            int maxDurability = Mathf.Max(0, def.MaxDurability);
            if (maxDurability <= 0)
                return false;

            int current = GetEquippedDurability(slot);
            if (current <= 0)
                current = maxDurability;

            int next = Mathf.Max(0, current - amount);

            if (next > 0)
            {
                SetSlotDurability(slot, next);
                Debug.Log($"[Durability] Used item={itemId} slot={slot} dur={next}/{maxDurability}", this);
                return true;
            }

            // Broken: clear slot and matching linked two-hand slot if it mirrors same item id.
            broke = true;
            brokenItemId = itemId;

            SetSlot(slot, string.Empty, 0);

            if (slot == EquipSlot.MainHand && string.Equals(GetEquippedItemId(EquipSlot.OffHand), itemId, StringComparison.Ordinal))
                SetSlot(EquipSlot.OffHand, string.Empty, 0);
            else if (slot == EquipSlot.OffHand && string.Equals(GetEquippedItemId(EquipSlot.MainHand), itemId, StringComparison.Ordinal))
                SetSlot(EquipSlot.MainHand, string.Empty, 0);

            Debug.Log($"[Durability] Item broke item={itemId} slot={slot} owner={OwnerClientId}", this);
            return true;
        }

        private bool ServerTryEquipFromInventorySlot(int inventorySlotIndex)
        {
            if (!IsServer || inventoryNet == null)
                return false;

            if (!inventoryNet.ServerTryGetSlotItem(inventorySlotIndex, out string slotItemId, out int qty, out int slotDurability, out ItemInstanceData slotInstanceData))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: invalid inventory slot={inventorySlotIndex}");
                return false;
            }

            if (qty != 1)
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: slot quantity must be 1 for equippables. slot={inventorySlotIndex} qty={qty}");
                return false;
            }

            var itemId = CanonicalizeItemId(slotItemId);
            if (!ValidateCommon(itemId, out var def))
                return false;

            if (!def.IsEquippable)
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: item not equippable. itemId={itemId}");
                return false;
            }

            // Hybrid rule:
            // - Hand slots are reference-equipped from hotbar only.
            // - Armor slots keep move-to-equipment behavior.
            if (IsReferenceEquipSlot(def.EquipSlot))
            {
                if (!TryApplyReferenceEquip(def, inventorySlotIndex, slotDurability, slotInstanceData))
                    return false;

                ServerValidateReferenceAssignments();
                LogServerEquipmentState($"Reference-equipped itemId={def.ItemId} slot={def.EquipSlot} invRef={inventorySlotIndex}");
                return true;
            }

            if (!CanEquip(def, out var toUnequipA, out var toUnequipB))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: rules failed. itemId={itemId}");
                return false;
            }

            if (!TryServerUnequipIfNeeded(toUnequipA)) return false;
            if (!TryServerUnequipIfNeeded(toUnequipB)) return false;

            if (!inventoryNet.ServerRemoveOneAtSlot(inventorySlotIndex, out string removedItemId, out int removedDurability, out ItemInstanceData removedInstanceData))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: could not remove from inventory slot={inventorySlotIndex} itemId={itemId}");
                return false;
            }

            if (!string.Equals(removedItemId, itemId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: slot changed during equip. expected={itemId} got={removedItemId}");
                return false;
            }

            int finalDurability = ResolveInitialDurability(def, removedDurability);
            if (!removedInstanceData.HasAnyBonus && slotInstanceData.HasAnyBonus)
                removedInstanceData = slotInstanceData;

            ApplyEquip(def, finalDurability, removedInstanceData);
            LogServerEquipmentState($"Moved-equipped itemId={def.ItemId} slot={def.EquipSlot} fromInventorySlot={inventorySlotIndex}");
            return true;
        }

        /// <summary>
        /// SERVER: validates and synchronizes hotbar reference-equipment assignments.
        /// This keeps replicated hand-slot visuals aligned with the authoritative inventory source.
        /// </summary>
        public void ServerValidateReferenceAssignments()
        {
            if (!IsServer)
                return;

            ValidateReferenceSlot(EquipSlot.MainHand);
            ValidateReferenceSlot(EquipSlot.OffHand);
        }

        private void ValidateReferenceSlot(EquipSlot equipSlot)
        {
            if (!IsReferenceEquipSlot(equipSlot))
                return;

            int refIndex = GetReferenceInventorySlotIndex(equipSlot);
            if (refIndex < 0)
                return;

            if (refIndex >= 8)
            {
                Debug.LogWarning($"[Equipment][SERVER] Clearing invalid reference (not hotbar). slot={equipSlot} invIndex={refIndex}");
                ClearReferenceSlot(equipSlot);
                return;
            }

            if (inventoryNet == null || !inventoryNet.ServerTryGetSlotItem(refIndex, out string itemId, out int qty, out int durability, out ItemInstanceData data))
            {
                Debug.LogWarning($"[Equipment][SERVER] Clearing stale reference. slot={equipSlot} invIndex={refIndex}");
                ClearReferenceSlot(equipSlot);
                return;
            }

            if (qty <= 0 || !ValidateCommon(itemId, out ItemDef def) || !def.IsEquippable || !DoesSlotAcceptItem(equipSlot, def))
            {
                Debug.LogWarning($"[Equipment][SERVER] Clearing incompatible reference. slot={equipSlot} invIndex={refIndex} item={itemId}");
                ClearReferenceSlot(equipSlot);
                return;
            }

            // Keep replicated slot visuals synced to the referenced inventory item.
            int finalDurability = ResolveInitialDurability(def, durability);
            SetSlot(equipSlot, itemId, finalDurability, data);

            // Two-handed items mirror both hand references.
            if (def.Handedness == Handedness.BothHands)
            {
                EquipSlot other = equipSlot == EquipSlot.MainHand ? EquipSlot.OffHand : EquipSlot.MainHand;
                SetReferenceSlotIndex(other, refIndex);
                SetSlot(other, itemId, finalDurability, data);
            }
        }

        private void SetReferenceSlotIndex(EquipSlot slot, int inventorySlotIndex)
        {
            switch (slot)
            {
                case EquipSlot.MainHand:
                    mainHandInventorySlotRef.Value = inventorySlotIndex;
                    break;
                case EquipSlot.OffHand:
                    offHandInventorySlotRef.Value = inventorySlotIndex;
                    break;
            }
        }

        private void ClearReferenceSlot(EquipSlot slot)
        {
            if (!IsReferenceEquipSlot(slot))
                return;

            SetReferenceSlotIndex(slot, -1);
            SetSlot(slot, string.Empty, 0, default);
        }

        private bool TryApplyReferenceEquip(ItemDef def, int inventorySlotIndex, int durability, ItemInstanceData instanceData)
        {
            if (def == null)
                return false;

            if (!IsReferenceEquipSlot(def.EquipSlot))
                return false;

            if (inventorySlotIndex < 0 || inventorySlotIndex >= 8)
            {
                Debug.LogWarning($"[Equipment][SERVER] Reference-equip denied: item must be in hotbar. itemId={def.ItemId} slotIndex={inventorySlotIndex}");
                return false;
            }

            if (!CanEquip(def, out EquipSlot toUnequipA, out EquipSlot toUnequipB))
                return false;

            if (!TryServerUnequipIfNeeded(toUnequipA))
                return false;
            if (!TryServerUnequipIfNeeded(toUnequipB))
                return false;

            int finalDurability = ResolveInitialDurability(def, durability);
            if (def.Handedness == Handedness.BothHands)
            {
                SetReferenceSlotIndex(EquipSlot.MainHand, inventorySlotIndex);
                SetReferenceSlotIndex(EquipSlot.OffHand, inventorySlotIndex);
                SetSlot(EquipSlot.MainHand, def.ItemId, finalDurability, instanceData);
                SetSlot(EquipSlot.OffHand, def.ItemId, finalDurability, instanceData);
                return true;
            }

            SetReferenceSlotIndex(def.EquipSlot, inventorySlotIndex);
            SetSlot(def.EquipSlot, def.ItemId, finalDurability, instanceData);

            return true;
        }
        private EquipmentSlotSaveData ExportMovedSlotSave(EquipSlot slot)
        {
            if (IsReferenceEquipSlot(slot))
                return null;

            string itemId = GetEquippedItemId(slot);
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            ItemInstanceData bonus = GetEquippedBonusData(slot);
            int durability = Mathf.Max(0, GetEquippedDurability(slot));
            int maxDurability = 0;
            if (itemDatabase != null && itemDatabase.TryGet(itemId, out ItemDef def) && def != null)
                maxDurability = Mathf.Max(0, def.ResolveDurabilityMax());

            return new EquipmentSlotSaveData
            {
                itemId = itemId,
                durability = durability,
                maxDurability = maxDurability,
                bonusStrength = bonus.BonusStrength,
                bonusDexterity = bonus.BonusDexterity,
                bonusIntelligence = bonus.BonusIntelligence,
                craftedBy = bonus.CraftedBy.ToString(),
                instanceId = bonus.InstanceId,
                rolledDamage = bonus.RolledDamage,
                rolledDefence = bonus.RolledDefence,
                rolledSwingSpeed = bonus.RolledSwingSpeed,
                rolledMovementSpeed = bonus.RolledMovementSpeed,
                rolledCastSpeed = bonus.RolledCastSpeed,
                rolledBlockValue = bonus.RolledBlockValue,
                damageBonus = bonus.DamageBonus,
                defenceBonus = bonus.DefenceBonus,
                attackSpeedBonus = bonus.AttackSpeedBonus,
                castSpeedBonus = bonus.CastSpeedBonus,
                critChanceBonus = bonus.CritChanceBonus,
                blockValueBonus = bonus.BlockValueBonus,
                statusPowerBonus = bonus.StatusPowerBonus,
                trapPowerBonus = bonus.TrapPowerBonus,
                physicalResist = bonus.PhysicalResist,
                fireResist = bonus.FireResist,
                frostResist = bonus.FrostResist,
                poisonResist = bonus.PoisonResist,
                lightningResist = bonus.LightningResist,
                affixA = (byte)bonus.AffixA,
                affixB = (byte)bonus.AffixB,
                affixC = (byte)bonus.AffixC,
                resistanceAffix = (byte)bonus.ResistanceAffix
            };
        }

        private string GetReferenceExpectedItemId(EquipSlot slot)
        {
            if (!IsReferenceEquipSlot(slot))
                return string.Empty;

            int refIndex = GetReferenceInventorySlotIndex(slot);
            if (refIndex < 0 || inventoryNet == null)
                return string.Empty;

            if (!inventoryNet.ServerTryGetSlotItem(refIndex, out string itemId, out int qty, out _, out _))
                return string.Empty;

            if (qty <= 0)
                return string.Empty;

            return itemId ?? string.Empty;
        }

        private void ServerClearAllEquipmentState()
        {
            SetSlot(EquipSlot.MainHand, string.Empty, 0, default);
            SetSlot(EquipSlot.OffHand, string.Empty, 0, default);
            SetSlot(EquipSlot.Helmet, string.Empty, 0, default);
            SetSlot(EquipSlot.Chest, string.Empty, 0, default);
            SetSlot(EquipSlot.Legs, string.Empty, 0, default);
            SetSlot(EquipSlot.Boots, string.Empty, 0, default);
            SetSlot(EquipSlot.Gloves, string.Empty, 0, default);
            SetSlot(EquipSlot.Shoulders, string.Empty, 0, default);
            SetSlot(EquipSlot.Belt, string.Empty, 0, default);
            SetReferenceSlotIndex(EquipSlot.MainHand, -1);
            SetReferenceSlotIndex(EquipSlot.OffHand, -1);
        }

        private void ApplyMovedSlotSave(EquipSlot slot, EquipmentSlotSaveData saveSlot)
        {
            if (saveSlot == null || string.IsNullOrWhiteSpace(saveSlot.itemId))
                return;

            if (!ValidateCommon(saveSlot.itemId, out ItemDef def) || !def.IsEquippable || IsReferenceEquipSlot(slot) || def.EquipSlot != slot)
            {
                Debug.LogWarning($"[Equipment][SERVER] Ignoring invalid moved slot save slot={slot} itemId={saveSlot.itemId}");
                return;
            }

            int maxDurability = Mathf.Max(0, saveSlot.maxDurability > 0 ? saveSlot.maxDurability : def.ResolveDurabilityMax());
            int finalDurability = maxDurability > 0
                ? Mathf.Clamp(saveSlot.durability > 0 ? saveSlot.durability : maxDurability, 1, maxDurability)
                : 0;

            ItemInstanceData data = default;
            data.BonusStrength = saveSlot.bonusStrength;
            data.BonusDexterity = saveSlot.bonusDexterity;
            data.BonusIntelligence = saveSlot.bonusIntelligence;
            data.CraftedBy = new FixedString64Bytes(saveSlot.craftedBy ?? string.Empty);
            data.InstanceId = saveSlot.instanceId;
            data.RolledDamage = saveSlot.rolledDamage;
            data.RolledDefence = saveSlot.rolledDefence;
            data.RolledSwingSpeed = saveSlot.rolledSwingSpeed;
            data.RolledMovementSpeed = saveSlot.rolledMovementSpeed;
            data.RolledCastSpeed = saveSlot.rolledCastSpeed;
            data.RolledBlockValue = saveSlot.rolledBlockValue;
            data.MaxDurability = maxDurability;
            data.CurrentDurability = finalDurability;
            data.DamageBonus = saveSlot.damageBonus;
            data.DefenceBonus = saveSlot.defenceBonus;
            data.AttackSpeedBonus = saveSlot.attackSpeedBonus;
            data.CastSpeedBonus = saveSlot.castSpeedBonus;
            data.CritChanceBonus = saveSlot.critChanceBonus;
            data.BlockValueBonus = saveSlot.blockValueBonus;
            data.StatusPowerBonus = saveSlot.statusPowerBonus;
            data.TrapPowerBonus = saveSlot.trapPowerBonus;
            data.PhysicalResist = saveSlot.physicalResist;
            data.FireResist = saveSlot.fireResist;
            data.FrostResist = saveSlot.frostResist;
            data.PoisonResist = saveSlot.poisonResist;
            data.LightningResist = saveSlot.lightningResist;
            data.AffixA = (ItemAffixId)saveSlot.affixA;
            data.AffixB = (ItemAffixId)saveSlot.affixB;
            data.AffixC = (ItemAffixId)saveSlot.affixC;
            data.ResistanceAffix = (ResistanceAffixId)saveSlot.resistanceAffix;

            SetSlot(slot, def.ItemId, finalDurability, data);
        }

        private void ApplyReferenceSlotSave(EquipSlot slot, int inventorySlotIndex, string expectedItemId)
        {
            if (!IsReferenceEquipSlot(slot))
                return;

            if (inventorySlotIndex < 0)
                return;

            if (inventorySlotIndex >= 8)
            {
                Debug.LogWarning($"[Equipment][SERVER] Clearing invalid saved reference slot={slot} index={inventorySlotIndex} (not hotbar)");
                return;
            }

            if (inventoryNet == null || !inventoryNet.ServerTryGetSlotItem(inventorySlotIndex, out string itemId, out int qty, out int durability, out ItemInstanceData data))
            {
                Debug.LogWarning($"[Equipment][SERVER] Clearing invalid saved reference slot={slot} index={inventorySlotIndex} (missing inventory row)");
                return;
            }

            if (qty <= 0 || string.IsNullOrWhiteSpace(itemId))
                return;

            // The expected id guard prevents stale references from binding to unrelated items
            // when the inventory changed between save and load.
            if (!string.IsNullOrWhiteSpace(expectedItemId) && !string.Equals(itemId, expectedItemId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[Equipment][SERVER] Clearing mismatched saved reference slot={slot} index={inventorySlotIndex} expected={expectedItemId} actual={itemId}");
                return;
            }

            if (!ValidateCommon(itemId, out ItemDef def) || !def.IsEquippable || !DoesSlotAcceptItem(slot, def))
                return;

            int finalDurability = ResolveInitialDurability(def, durability);
            SetReferenceSlotIndex(slot, inventorySlotIndex);
            SetSlot(slot, itemId, finalDurability, data);

            if (def.Handedness == Handedness.BothHands)
            {
                EquipSlot other = slot == EquipSlot.MainHand ? EquipSlot.OffHand : EquipSlot.MainHand;
                SetReferenceSlotIndex(other, inventorySlotIndex);
                SetSlot(other, itemId, finalDurability, data);
            }
        }
        private bool ValidateCommon(string itemId, out ItemDef def)
        {
            def = null;

            if (itemDatabase == null)
            {
                Debug.LogError("[Equipment][SERVER] ItemDatabase not assigned.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (!itemDatabase.TryGet(itemId, out def) || def == null)
            {
                Debug.LogWarning($"[Equipment][SERVER] Unknown itemId={itemId}");
                return false;
            }

            return true;
        }

        private bool CanEquip(ItemDef def, out EquipSlot unequipA, out EquipSlot unequipB)
        {
            unequipA = EquipSlot.None;
            unequipB = EquipSlot.None;

            if (def.EquipSlot is EquipSlot.Helmet or EquipSlot.Chest or EquipSlot.Legs or EquipSlot.Boots or EquipSlot.Gloves or EquipSlot.Shoulders or EquipSlot.Belt)
            {
                unequipA = def.EquipSlot;
                return true;
            }

            if (def.EquipSlot != EquipSlot.MainHand && def.EquipSlot != EquipSlot.OffHand)
                return false;

            if (def.Handedness == Handedness.BothHands)
            {
                unequipA = EquipSlot.MainHand;
                unequipB = EquipSlot.OffHand;
                return true;
            }

            if (def.Handedness == Handedness.MainHand)
            {
                unequipA = EquipSlot.MainHand;
                if (IsBothHandsEquippedInEitherHand())
                    unequipB = EquipSlot.OffHand;
                return true;
            }

            if (def.Handedness == Handedness.OffHand)
            {
                unequipA = EquipSlot.OffHand;
                if (IsBothHandsEquippedInEitherHand())
                    unequipB = EquipSlot.MainHand;
                return true;
            }

            return false;
        }

        private bool IsBothHandsEquippedInEitherHand()
        {
            if (itemDatabase == null) return false;

            var mh = mainHand.Value.ToString();
            if (!string.IsNullOrWhiteSpace(mh) && itemDatabase.TryGet(mh, out var defMh) && defMh != null && defMh.Handedness == Handedness.BothHands)
                return true;

            var oh = offHand.Value.ToString();
            if (!string.IsNullOrWhiteSpace(oh) && itemDatabase.TryGet(oh, out var defOh) && defOh != null && defOh.Handedness == Handedness.BothHands)
                return true;

            return false;
        }

        private bool DoesSlotAcceptItem(EquipSlot slot, ItemDef def)
        {
            if (def == null) return false;

            switch (slot)
            {
                case EquipSlot.Helmet:
                case EquipSlot.Chest:
                case EquipSlot.Legs:
                case EquipSlot.Boots:
                case EquipSlot.Gloves:
                case EquipSlot.Shoulders:
                case EquipSlot.Belt:
                    return def.EquipSlot == slot;
                case EquipSlot.MainHand:
                    if (def.Handedness == Handedness.BothHands)
                        return true;
                    return def.Handedness == Handedness.MainHand && def.EquipSlot == EquipSlot.MainHand;
                case EquipSlot.OffHand:
                    if (def.Handedness == Handedness.BothHands)
                        return true;
                    return def.Handedness == Handedness.OffHand && def.EquipSlot == EquipSlot.OffHand;
                default:
                    return false;
            }
        }

        private bool TryServerUnequipIfNeeded(EquipSlot slot, int preferredInventoryIndex = -1)
        {
            if (slot == EquipSlot.None) return true;
            if (inventoryNet == null) return false;

            // Hybrid behavior: hand slots can be reference-equipped from hotbar.
            // Unequipping a reference slot only clears the reference; item remains in inventory.
            int refIndex = GetReferenceInventorySlotIndex(slot);
            if (IsReferenceEquipSlot(slot) && refIndex >= 0)
            {
                ClearReferenceSlot(slot);
                Debug.Log($"[Equipment][SERVER] Cleared reference equip slot={slot} invIndex={refIndex}");
                LogServerEquipmentState($"Post-reference-unequip slot={slot}");
                return true;
            }

            var equippedId = GetEquippedItemId(slot);
            if (string.IsNullOrWhiteSpace(equippedId))
                return true;

            int equippedDurability = GetEquippedDurability(slot);
            ItemInstanceData equippedBonus = GetEquippedBonusData(slot);

            bool stored = false;
            if (preferredInventoryIndex >= 0)
                stored = inventoryNet.ServerTryAddItemToSlot(
                    equippedId,
                    preferredInventoryIndex,
                    equippedDurability,
                    equippedBonus.BonusStrength,
                    equippedBonus.BonusDexterity,
                    equippedBonus.BonusIntelligence,
                    equippedBonus.CraftedBy);

            if (!stored)
            {
                var remainder = inventoryNet.ServerAddItem(
                    equippedId,
                    1,
                    equippedDurability,
                    equippedBonus.BonusStrength,
                    equippedBonus.BonusDexterity,
                    equippedBonus.BonusIntelligence,
                    equippedBonus.CraftedBy);
                if (remainder > 0)
                {
                    Debug.LogWarning($"[Equipment][SERVER] Unequip denied: inventory full. slot={slot} itemId={equippedId}");
                    return false;
                }
            }

            SetSlot(slot, "", 0);
            Debug.Log($"[Equipment][SERVER] Unequipped slot={slot} itemId={equippedId}");
            LogServerEquipmentState($"Post-unequip slot={slot}");
            return true;
        }

        private void ApplyEquip(ItemDef def, int durability, ItemInstanceData instanceData)
        {
            if (def == null)
                return;

            var itemId = def.ItemId;
            int finalDurability = ResolveInitialDurability(def, durability);

            if (def.EquipSlot is EquipSlot.Helmet or EquipSlot.Chest or EquipSlot.Legs or EquipSlot.Boots or EquipSlot.Gloves or EquipSlot.Shoulders or EquipSlot.Belt)
            {
                SetSlot(def.EquipSlot, itemId, finalDurability, instanceData);
            }
            else if (def.Handedness == Handedness.BothHands)
            {
                SetSlot(EquipSlot.MainHand, itemId, finalDurability, instanceData);
                SetSlot(EquipSlot.OffHand, itemId, finalDurability, instanceData);
            }
            else if (def.Handedness == Handedness.MainHand)
            {
                SetSlot(EquipSlot.MainHand, itemId, finalDurability, instanceData);
            }
            else if (def.Handedness == Handedness.OffHand)
            {
                SetSlot(EquipSlot.OffHand, itemId, finalDurability, instanceData);
            }
            else
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip applied to unsupported slot. itemId={itemId}");
            }

            Debug.Log($"[Equipment][SERVER] Equipped. mainHand={mainHand.Value} offHand={offHand.Value}");
            LogServerEquipmentState($"ApplyEquip itemId={itemId}");
        }

        private int ResolveInitialDurability(ItemDef def, int durability)
        {
            if (def == null || def.MaxDurability <= 0)
                return 0;

            if (durability <= 0)
                return def.MaxDurability;

            return Mathf.Clamp(durability, 1, def.MaxDurability);
        }

        private void SetSlot(EquipSlot slot, string itemId, int durability, ItemInstanceData instanceData = default)
        {
            var fs = new FixedString64Bytes(itemId ?? "");
            bool clear = string.IsNullOrWhiteSpace(itemId);
            int finalDurability = clear ? 0 : Mathf.Max(0, durability);

            if (clear)
                instanceData = default;

            switch (slot)
            {
                case EquipSlot.MainHand:
                    if (clear)
                        mainHandInventorySlotRef.Value = -1;
                    mainHand.Value = fs;
                    mainHandDurability.Value = finalDurability;
                    mainHandBonusStrength.Value = instanceData.BonusStrength;
                    mainHandBonusDexterity.Value = instanceData.BonusDexterity;
                    mainHandBonusIntelligence.Value = instanceData.BonusIntelligence;
                    mainHandCraftedBy.Value = instanceData.CraftedBy;
                    mainHandInstanceData.Value = instanceData;
                    break;
                case EquipSlot.OffHand:
                    if (clear)
                        offHandInventorySlotRef.Value = -1;
                    offHand.Value = fs;
                    offHandDurability.Value = finalDurability;
                    offHandBonusStrength.Value = instanceData.BonusStrength;
                    offHandBonusDexterity.Value = instanceData.BonusDexterity;
                    offHandBonusIntelligence.Value = instanceData.BonusIntelligence;
                    offHandCraftedBy.Value = instanceData.CraftedBy;
                    offHandInstanceData.Value = instanceData;
                    break;
                case EquipSlot.Helmet:
                    head.Value = fs;
                    headDurability.Value = finalDurability;
                    headBonusStrength.Value = instanceData.BonusStrength;
                    headBonusDexterity.Value = instanceData.BonusDexterity;
                    headBonusIntelligence.Value = instanceData.BonusIntelligence;
                    headCraftedBy.Value = instanceData.CraftedBy;
                    headInstanceData.Value = instanceData;
                    break;
                case EquipSlot.Chest:
                    chest.Value = fs;
                    chestDurability.Value = finalDurability;
                    chestBonusStrength.Value = instanceData.BonusStrength;
                    chestBonusDexterity.Value = instanceData.BonusDexterity;
                    chestBonusIntelligence.Value = instanceData.BonusIntelligence;
                    chestCraftedBy.Value = instanceData.CraftedBy;
                    chestInstanceData.Value = instanceData;
                    break;
                case EquipSlot.Legs:
                    legs.Value = fs;
                    legsDurability.Value = finalDurability;
                    legsBonusStrength.Value = instanceData.BonusStrength;
                    legsBonusDexterity.Value = instanceData.BonusDexterity;
                    legsBonusIntelligence.Value = instanceData.BonusIntelligence;
                    legsCraftedBy.Value = instanceData.CraftedBy;
                    legsInstanceData.Value = instanceData;
                    break;
                case EquipSlot.Boots:
                    feet.Value = fs;
                    feetDurability.Value = finalDurability;
                    feetBonusStrength.Value = instanceData.BonusStrength;
                    feetBonusDexterity.Value = instanceData.BonusDexterity;
                    feetBonusIntelligence.Value = instanceData.BonusIntelligence;
                    feetCraftedBy.Value = instanceData.CraftedBy;
                    feetInstanceData.Value = instanceData;
                    break;
                case EquipSlot.Gloves:
                    gloves.Value = fs;
                    glovesDurability.Value = finalDurability;
                    glovesBonusStrength.Value = instanceData.BonusStrength;
                    glovesBonusDexterity.Value = instanceData.BonusDexterity;
                    glovesBonusIntelligence.Value = instanceData.BonusIntelligence;
                    glovesCraftedBy.Value = instanceData.CraftedBy;
                    glovesInstanceData.Value = instanceData;
                    break;
                case EquipSlot.Shoulders:
                    shoulders.Value = fs;
                    shouldersDurability.Value = finalDurability;
                    shouldersBonusStrength.Value = instanceData.BonusStrength;
                    shouldersBonusDexterity.Value = instanceData.BonusDexterity;
                    shouldersBonusIntelligence.Value = instanceData.BonusIntelligence;
                    shouldersCraftedBy.Value = instanceData.CraftedBy;
                    shouldersInstanceData.Value = instanceData;
                    break;
                case EquipSlot.Belt:
                    belt.Value = fs;
                    beltDurability.Value = finalDurability;
                    beltBonusStrength.Value = instanceData.BonusStrength;
                    beltBonusDexterity.Value = instanceData.BonusDexterity;
                    beltBonusIntelligence.Value = instanceData.BonusIntelligence;
                    beltCraftedBy.Value = instanceData.CraftedBy;
                    beltInstanceData.Value = instanceData;
                    break;
            }
        }

        private ItemInstanceData GetEquippedBonusData(EquipSlot slot)
        {
            ItemInstanceData data = slot switch
            {
                EquipSlot.MainHand => mainHandInstanceData.Value,
                EquipSlot.OffHand => offHandInstanceData.Value,
                EquipSlot.Helmet => headInstanceData.Value,
                EquipSlot.Chest => chestInstanceData.Value,
                EquipSlot.Legs => legsInstanceData.Value,
                EquipSlot.Boots => feetInstanceData.Value,
                EquipSlot.Gloves => glovesInstanceData.Value,
                EquipSlot.Shoulders => shouldersInstanceData.Value,
                EquipSlot.Belt => beltInstanceData.Value,
                _ => default
            };

            // Backward-compatible fallback for older scene/runtime state that still only set the legacy bonus netvars.
            if (data.BonusStrength == 0 && data.BonusDexterity == 0 && data.BonusIntelligence == 0 && data.CraftedBy.IsEmpty)
            {
                switch (slot)
                {
                    case EquipSlot.MainHand:
                        data.BonusStrength = mainHandBonusStrength.Value;
                        data.BonusDexterity = mainHandBonusDexterity.Value;
                        data.BonusIntelligence = mainHandBonusIntelligence.Value;
                        data.CraftedBy = mainHandCraftedBy.Value;
                        break;
                    case EquipSlot.OffHand:
                        data.BonusStrength = offHandBonusStrength.Value;
                        data.BonusDexterity = offHandBonusDexterity.Value;
                        data.BonusIntelligence = offHandBonusIntelligence.Value;
                        data.CraftedBy = offHandCraftedBy.Value;
                        break;
                    case EquipSlot.Helmet:
                        data.BonusStrength = headBonusStrength.Value;
                        data.BonusDexterity = headBonusDexterity.Value;
                        data.BonusIntelligence = headBonusIntelligence.Value;
                        data.CraftedBy = headCraftedBy.Value;
                        break;
                    case EquipSlot.Chest:
                        data.BonusStrength = chestBonusStrength.Value;
                        data.BonusDexterity = chestBonusDexterity.Value;
                        data.BonusIntelligence = chestBonusIntelligence.Value;
                        data.CraftedBy = chestCraftedBy.Value;
                        break;
                    case EquipSlot.Legs:
                        data.BonusStrength = legsBonusStrength.Value;
                        data.BonusDexterity = legsBonusDexterity.Value;
                        data.BonusIntelligence = legsBonusIntelligence.Value;
                        data.CraftedBy = legsCraftedBy.Value;
                        break;
                    case EquipSlot.Boots:
                        data.BonusStrength = feetBonusStrength.Value;
                        data.BonusDexterity = feetBonusDexterity.Value;
                        data.BonusIntelligence = feetBonusIntelligence.Value;
                        data.CraftedBy = feetCraftedBy.Value;
                        break;
                    case EquipSlot.Gloves:
                        data.BonusStrength = glovesBonusStrength.Value;
                        data.BonusDexterity = glovesBonusDexterity.Value;
                        data.BonusIntelligence = glovesBonusIntelligence.Value;
                        data.CraftedBy = glovesCraftedBy.Value;
                        break;
                    case EquipSlot.Shoulders:
                        data.BonusStrength = shouldersBonusStrength.Value;
                        data.BonusDexterity = shouldersBonusDexterity.Value;
                        data.BonusIntelligence = shouldersBonusIntelligence.Value;
                        data.CraftedBy = shouldersCraftedBy.Value;
                        break;
                    case EquipSlot.Belt:
                        data.BonusStrength = beltBonusStrength.Value;
                        data.BonusDexterity = beltBonusDexterity.Value;
                        data.BonusIntelligence = beltBonusIntelligence.Value;
                        data.CraftedBy = beltCraftedBy.Value;
                        break;
                }
            }

            return data;
        }

        private void SetSlotDurability(EquipSlot slot, int durability)
        {
            int finalDurability = Mathf.Max(0, durability);
            switch (slot)
            {
                case EquipSlot.MainHand:
                    mainHandDurability.Value = finalDurability;
                    { var data = mainHandInstanceData.Value; data.CurrentDurability = finalDurability; mainHandInstanceData.Value = data; }
                    break;
                case EquipSlot.OffHand:
                    offHandDurability.Value = finalDurability;
                    { var data = offHandInstanceData.Value; data.CurrentDurability = finalDurability; offHandInstanceData.Value = data; }
                    break;
                case EquipSlot.Helmet:
                    headDurability.Value = finalDurability;
                    { var data = headInstanceData.Value; data.CurrentDurability = finalDurability; headInstanceData.Value = data; }
                    break;
                case EquipSlot.Chest:
                    chestDurability.Value = finalDurability;
                    { var data = chestInstanceData.Value; data.CurrentDurability = finalDurability; chestInstanceData.Value = data; }
                    break;
                case EquipSlot.Legs:
                    legsDurability.Value = finalDurability;
                    { var data = legsInstanceData.Value; data.CurrentDurability = finalDurability; legsInstanceData.Value = data; }
                    break;
                case EquipSlot.Boots:
                    feetDurability.Value = finalDurability;
                    { var data = feetInstanceData.Value; data.CurrentDurability = finalDurability; feetInstanceData.Value = data; }
                    break;
                case EquipSlot.Gloves:
                    glovesDurability.Value = finalDurability;
                    { var data = glovesInstanceData.Value; data.CurrentDurability = finalDurability; glovesInstanceData.Value = data; }
                    break;
                case EquipSlot.Shoulders:
                    shouldersDurability.Value = finalDurability;
                    { var data = shouldersInstanceData.Value; data.CurrentDurability = finalDurability; shouldersInstanceData.Value = data; }
                    break;
                case EquipSlot.Belt:
                    beltDurability.Value = finalDurability;
                    { var data = beltInstanceData.Value; data.CurrentDurability = finalDurability; beltInstanceData.Value = data; }
                    break;
            }
        }

        private bool SlotMatches(FixedString64Bytes slotValue, string canonicalId)
        {
            if (string.IsNullOrEmpty(canonicalId))
                return false;

            var slotId = slotValue.ToString();
            if (string.IsNullOrEmpty(slotId))
                return false;

            return string.Equals(slotId, canonicalId, StringComparison.Ordinal);
        }

        private static string GetSlotString(FixedString64Bytes slotValue)
        {
            return slotValue.ToString();
        }

        private string CanonicalizeItemId(string rawItemId)
        {
            if (string.IsNullOrWhiteSpace(rawItemId))
                return string.Empty;

            var trimmed = rawItemId.Trim();

            if (EnsureItemDatabase() && itemDatabase.TryGet(trimmed, out var def) && def != null)
                return def.ItemId ?? trimmed;

            return trimmed;
        }

        private bool EnsureItemDatabase()
        {
            if (itemDatabase != null)
                return true;

            Debug.LogError("[Equipment][SERVER] ItemDatabase reference missing.");
            return false;
        }

        public string BuildServerDebugString()
        {
            var sb = new StringBuilder(320);
            sb.Append("Slots[MH=").Append(GetSlotString(mainHand.Value)).Append('(').Append(mainHandDurability.Value).Append(')')
              .Append(", OH=").Append(GetSlotString(offHand.Value)).Append('(').Append(offHandDurability.Value).Append(')')
               .Append(", Helmet=").Append(GetSlotString(head.Value)).Append('(').Append(headDurability.Value).Append(')')
              .Append(", Chest=").Append(GetSlotString(chest.Value)).Append('(').Append(chestDurability.Value).Append(')')
              .Append(", Legs=").Append(GetSlotString(legs.Value)).Append('(').Append(legsDurability.Value).Append(')')
               .Append(", Boots=").Append(GetSlotString(feet.Value)).Append('(').Append(feetDurability.Value).Append(')')
              .Append(", Gloves=").Append(GetSlotString(gloves.Value)).Append('(').Append(glovesDurability.Value).Append(')')
              .Append(", Shoulders=").Append(GetSlotString(shoulders.Value)).Append('(').Append(shouldersDurability.Value).Append(')')
              .Append(", Belt=").Append(GetSlotString(belt.Value)).Append('(').Append(beltDurability.Value).Append(')')
              .Append(']');

            if (EnsureItemDatabase())
            {
                sb.Append(" ToolTags[");
                var startLength = sb.Length;
                AppendSlotToolTags(sb, "MH", mainHand.Value);
                AppendSlotToolTags(sb, "OH", offHand.Value);
                AppendSlotToolTags(sb, "Helmet", head.Value);
                AppendSlotToolTags(sb, "Chest", chest.Value);
                AppendSlotToolTags(sb, "Legs", legs.Value);
                AppendSlotToolTags(sb, "Boots", feet.Value);
                AppendSlotToolTags(sb, "Gloves", gloves.Value);
                AppendSlotToolTags(sb, "Shoulders", shoulders.Value);
                AppendSlotToolTags(sb, "Belt", belt.Value);
                if (sb.Length == startLength)
                    sb.Append("none");
                sb.Append(']');
            }
            else
            {
                sb.Append(" ToolTags=unavailable");
            }

            return sb.ToString();
        }

        private void AppendSlotToolTags(StringBuilder sb, string slotLabel, FixedString64Bytes slotValue)
        {
            var id = GetSlotString(slotValue);
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (!itemDatabase.TryGet(id, out var def) || def == null)
                return;

            var tags = def.ToolTags;
            if (tags == null || tags.Length == 0)
                return;

            sb.Append(slotLabel).Append('=');
            for (int i = 0; i < tags.Length; i++)
            {
                if (i > 0)
                    sb.Append('|');
                sb.Append(tags[i]);
            }
            sb.Append(' ');
        }

        private void LogServerEquipmentState(string context)
        {
            if (!IsServer)
                return;

            Debug.Log($"[Equipment][SERVER] {context} owner={OwnerClientId} state={BuildServerDebugString()}");
        }

        public bool TryGetItemDef(string itemId, out ItemDef def)
        {
            def = null;
            if (itemDatabase == null) return false;
            return itemDatabase.TryGet(itemId, out def);
        }
    }
}













































































