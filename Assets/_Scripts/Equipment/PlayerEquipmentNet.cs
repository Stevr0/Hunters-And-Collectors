using System;
using System.Text;
using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
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

        public int GetEquippedBonusStrength(EquipSlot slot) => GetEquippedBonusData(slot).BonusStrength;
        public int GetEquippedBonusDexterity(EquipSlot slot) => GetEquippedBonusData(slot).BonusDexterity;
        public int GetEquippedBonusIntelligence(EquipSlot slot) => GetEquippedBonusData(slot).BonusIntelligence;
        public string GetEquippedCraftedBy(EquipSlot slot) => GetEquippedBonusData(slot).CraftedBy.ToString();

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
        }

        private void OnAnySlotChanged(FixedString64Bytes prev, FixedString64Bytes next)
        {
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
            LogServerEquipmentState($"Equipped itemId={def.ItemId} slot={def.EquipSlot} fromInventorySlot={inventorySlotIndex}");
            return true;
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
                    mainHand.Value = fs;
                    mainHandDurability.Value = finalDurability;
                    mainHandBonusStrength.Value = instanceData.BonusStrength;
                    mainHandBonusDexterity.Value = instanceData.BonusDexterity;
                    mainHandBonusIntelligence.Value = instanceData.BonusIntelligence;
                    mainHandCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.OffHand:
                    offHand.Value = fs;
                    offHandDurability.Value = finalDurability;
                    offHandBonusStrength.Value = instanceData.BonusStrength;
                    offHandBonusDexterity.Value = instanceData.BonusDexterity;
                    offHandBonusIntelligence.Value = instanceData.BonusIntelligence;
                    offHandCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.Helmet:
                    head.Value = fs;
                    headDurability.Value = finalDurability;
                    headBonusStrength.Value = instanceData.BonusStrength;
                    headBonusDexterity.Value = instanceData.BonusDexterity;
                    headBonusIntelligence.Value = instanceData.BonusIntelligence;
                    headCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.Chest:
                    chest.Value = fs;
                    chestDurability.Value = finalDurability;
                    chestBonusStrength.Value = instanceData.BonusStrength;
                    chestBonusDexterity.Value = instanceData.BonusDexterity;
                    chestBonusIntelligence.Value = instanceData.BonusIntelligence;
                    chestCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.Legs:
                    legs.Value = fs;
                    legsDurability.Value = finalDurability;
                    legsBonusStrength.Value = instanceData.BonusStrength;
                    legsBonusDexterity.Value = instanceData.BonusDexterity;
                    legsBonusIntelligence.Value = instanceData.BonusIntelligence;
                    legsCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.Boots:
                    feet.Value = fs;
                    feetDurability.Value = finalDurability;
                    feetBonusStrength.Value = instanceData.BonusStrength;
                    feetBonusDexterity.Value = instanceData.BonusDexterity;
                    feetBonusIntelligence.Value = instanceData.BonusIntelligence;
                    feetCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.Gloves:
                    gloves.Value = fs;
                    glovesDurability.Value = finalDurability;
                    glovesBonusStrength.Value = instanceData.BonusStrength;
                    glovesBonusDexterity.Value = instanceData.BonusDexterity;
                    glovesBonusIntelligence.Value = instanceData.BonusIntelligence;
                    glovesCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.Shoulders:
                    shoulders.Value = fs;
                    shouldersDurability.Value = finalDurability;
                    shouldersBonusStrength.Value = instanceData.BonusStrength;
                    shouldersBonusDexterity.Value = instanceData.BonusDexterity;
                    shouldersBonusIntelligence.Value = instanceData.BonusIntelligence;
                    shouldersCraftedBy.Value = instanceData.CraftedBy;
                    break;
                case EquipSlot.Belt:
                    belt.Value = fs;
                    beltDurability.Value = finalDurability;
                    beltBonusStrength.Value = instanceData.BonusStrength;
                    beltBonusDexterity.Value = instanceData.BonusDexterity;
                    beltBonusIntelligence.Value = instanceData.BonusIntelligence;
                    beltCraftedBy.Value = instanceData.CraftedBy;
                    break;
            }
        }

        private ItemInstanceData GetEquippedBonusData(EquipSlot slot)
        {
            ItemInstanceData data;
            switch (slot)
            {
                case EquipSlot.MainHand:
                    data.BonusStrength = mainHandBonusStrength.Value;
                    data.BonusDexterity = mainHandBonusDexterity.Value;
                    data.BonusIntelligence = mainHandBonusIntelligence.Value;
                    data.CraftedBy = mainHandCraftedBy.Value;
                    return data;
                case EquipSlot.OffHand:
                    data.BonusStrength = offHandBonusStrength.Value;
                    data.BonusDexterity = offHandBonusDexterity.Value;
                    data.BonusIntelligence = offHandBonusIntelligence.Value;
                    data.CraftedBy = offHandCraftedBy.Value;
                    return data;
                case EquipSlot.Helmet:
                    data.BonusStrength = headBonusStrength.Value;
                    data.BonusDexterity = headBonusDexterity.Value;
                    data.BonusIntelligence = headBonusIntelligence.Value;
                    data.CraftedBy = headCraftedBy.Value;
                    return data;
                case EquipSlot.Chest:
                    data.BonusStrength = chestBonusStrength.Value;
                    data.BonusDexterity = chestBonusDexterity.Value;
                    data.BonusIntelligence = chestBonusIntelligence.Value;
                    data.CraftedBy = chestCraftedBy.Value;
                    return data;
                case EquipSlot.Legs:
                    data.BonusStrength = legsBonusStrength.Value;
                    data.BonusDexterity = legsBonusDexterity.Value;
                    data.BonusIntelligence = legsBonusIntelligence.Value;
                    data.CraftedBy = legsCraftedBy.Value;
                    return data;
                case EquipSlot.Boots:
                    data.BonusStrength = feetBonusStrength.Value;
                    data.BonusDexterity = feetBonusDexterity.Value;
                    data.BonusIntelligence = feetBonusIntelligence.Value;
                    data.CraftedBy = feetCraftedBy.Value;
                    return data;
                case EquipSlot.Gloves:
                    data.BonusStrength = glovesBonusStrength.Value;
                    data.BonusDexterity = glovesBonusDexterity.Value;
                    data.BonusIntelligence = glovesBonusIntelligence.Value;
                    data.CraftedBy = glovesCraftedBy.Value;
                    return data;
                case EquipSlot.Shoulders:
                    data.BonusStrength = shouldersBonusStrength.Value;
                    data.BonusDexterity = shouldersBonusDexterity.Value;
                    data.BonusIntelligence = shouldersBonusIntelligence.Value;
                    data.CraftedBy = shouldersCraftedBy.Value;
                    return data;
                case EquipSlot.Belt:
                    data.BonusStrength = beltBonusStrength.Value;
                    data.BonusDexterity = beltBonusDexterity.Value;
                    data.BonusIntelligence = beltBonusIntelligence.Value;
                    data.CraftedBy = beltCraftedBy.Value;
                    return data;
                default:
                    return default;
            }
        }

        private void SetSlotDurability(EquipSlot slot, int durability)
        {
            int finalDurability = Mathf.Max(0, durability);
            switch (slot)
            {
                case EquipSlot.MainHand: mainHandDurability.Value = finalDurability; break;
                case EquipSlot.OffHand: offHandDurability.Value = finalDurability; break;
                case EquipSlot.Helmet: headDurability.Value = finalDurability; break;
                case EquipSlot.Chest: chestDurability.Value = finalDurability; break;
                case EquipSlot.Legs: legsDurability.Value = finalDurability; break;
                case EquipSlot.Boots: feetDurability.Value = finalDurability; break;
                case EquipSlot.Gloves: glovesDurability.Value = finalDurability; break;
                case EquipSlot.Shoulders: shouldersDurability.Value = finalDurability; break;
                case EquipSlot.Belt: beltDurability.Value = finalDurability; break;
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

















































