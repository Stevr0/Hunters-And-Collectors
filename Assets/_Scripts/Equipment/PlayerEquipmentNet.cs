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

        // Server writes durability, all clients read for UI.
        private readonly NetworkVariable<int> mainHandDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> offHandDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> headDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> chestDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> legsDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> feetDurability = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n`r`n        // Per-slot crafted instance attribute bonuses (server writes, all clients read).`r`n        private readonly NetworkVariable<int> mainHandBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> mainHandBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> mainHandBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n`r`n        private readonly NetworkVariable<int> offHandBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> offHandBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> offHandBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n`r`n        private readonly NetworkVariable<int> headBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> headBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> headBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n`r`n        private readonly NetworkVariable<int> chestBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> chestBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> chestBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n`r`n        private readonly NetworkVariable<int> legsBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> legsBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> legsBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n`r`n        private readonly NetworkVariable<int> feetBonusStrength = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> feetBonusDexterity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);`r`n        private readonly NetworkVariable<int> feetBonusIntelligence = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public event Action OnEquipmentChanged;

        public NetworkVariable<FixedString64Bytes> MainHandNetVar => mainHand;
        public NetworkVariable<FixedString64Bytes> OffHandNetVar => offHand;
        public NetworkVariable<FixedString64Bytes> HeadNetVar => head;
        public NetworkVariable<FixedString64Bytes> ChestNetVar => chest;
        public NetworkVariable<FixedString64Bytes> LegsNetVar => legs;
        public NetworkVariable<FixedString64Bytes> FeetNetVar => feet;

        public NetworkVariable<int> MainHandDurabilityNetVar => mainHandDurability;
        public NetworkVariable<int> OffHandDurabilityNetVar => offHandDurability;
        public NetworkVariable<int> HeadDurabilityNetVar => headDurability;
        public NetworkVariable<int> ChestDurabilityNetVar => chestDurability;
        public NetworkVariable<int> LegsDurabilityNetVar => legsDurability;
        public NetworkVariable<int> FeetDurabilityNetVar => feetDurability;

        public string GetMainHandItemId() => mainHand.Value.ToString();

        public string GetEquippedItemId(EquipSlot slot)
        {
            return slot switch
            {
                EquipSlot.MainHand => mainHand.Value.ToString(),
                EquipSlot.OffHand => offHand.Value.ToString(),
                EquipSlot.Head => head.Value.ToString(),
                EquipSlot.Chest => chest.Value.ToString(),
                EquipSlot.Legs => legs.Value.ToString(),
                EquipSlot.Feet => feet.Value.ToString(),
                _ => string.Empty
            };
        }

        public int GetEquippedDurability(EquipSlot slot)
        {
            return slot switch
            {
                EquipSlot.MainHand => mainHandDurability.Value,
                EquipSlot.OffHand => offHandDurability.Value,
                EquipSlot.Head => headDurability.Value,
                EquipSlot.Chest => chestDurability.Value,
                EquipSlot.Legs => legsDurability.Value,
                EquipSlot.Feet => feetDurability.Value,
                _ => 0
            };
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
                || SlotMatches(feet.Value, canonical);
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
                || EquippedItemHasToolTag(feet.Value, tag);
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

            mainHandDurability.OnValueChanged += OnAnyDurabilityChanged;
            offHandDurability.OnValueChanged += OnAnyDurabilityChanged;
            headDurability.OnValueChanged += OnAnyDurabilityChanged;
            chestDurability.OnValueChanged += OnAnyDurabilityChanged;
            legsDurability.OnValueChanged += OnAnyDurabilityChanged;
            feetDurability.OnValueChanged += OnAnyDurabilityChanged;

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

            mainHandDurability.OnValueChanged -= OnAnyDurabilityChanged;
            offHandDurability.OnValueChanged -= OnAnyDurabilityChanged;
            headDurability.OnValueChanged -= OnAnyDurabilityChanged;
            chestDurability.OnValueChanged -= OnAnyDurabilityChanged;
            legsDurability.OnValueChanged -= OnAnyDurabilityChanged;
            feetDurability.OnValueChanged -= OnAnyDurabilityChanged;
        }

        private void OnAnySlotChanged(FixedString64Bytes prev, FixedString64Bytes next)
        {
            OnEquipmentChanged?.Invoke();
        }

        private void OnAnyDurabilityChanged(int prev, int next)
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

            SetSlot(toSlot, fromItemId, fromDurability);
            SetSlot(fromSlot, toItemId, toDurability);
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
                EquipSlot.Head,
                EquipSlot.Chest,
                EquipSlot.Legs,
                EquipSlot.Feet
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

            if (!inventoryNet.ServerTryGetSlotItem(inventorySlotIndex, out string slotItemId, out int qty, out int slotDurability))
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

            if (!inventoryNet.ServerRemoveOneAtSlot(inventorySlotIndex, out string removedItemId, out int removedDurability))
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
            ApplyEquip(def, finalDurability);
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

            if (def.EquipSlot is EquipSlot.Head or EquipSlot.Chest or EquipSlot.Legs or EquipSlot.Feet)
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
                case EquipSlot.Head:
                case EquipSlot.Chest:
                case EquipSlot.Legs:
                case EquipSlot.Feet:
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

            bool stored = false;
            if (preferredInventoryIndex >= 0)
                stored = inventoryNet.ServerTryAddItemToSlot(equippedId, preferredInventoryIndex, equippedDurability);

            if (!stored)
            {
                var remainder = inventoryNet.ServerAddItem(equippedId, 1, equippedDurability);
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

        private void ApplyEquip(ItemDef def, int durability)
        {
            if (def == null)
                return;

            var itemId = def.ItemId;
            int finalDurability = ResolveInitialDurability(def, durability);

            if (def.EquipSlot is EquipSlot.Head or EquipSlot.Chest or EquipSlot.Legs or EquipSlot.Feet)
            {
                SetSlot(def.EquipSlot, itemId, finalDurability);
            }
            else if (def.Handedness == Handedness.BothHands)
            {
                SetSlot(EquipSlot.MainHand, itemId, finalDurability);
                SetSlot(EquipSlot.OffHand, itemId, finalDurability);
            }
            else if (def.Handedness == Handedness.MainHand)
            {
                SetSlot(EquipSlot.MainHand, itemId, finalDurability);
            }
            else if (def.Handedness == Handedness.OffHand)
            {
                SetSlot(EquipSlot.OffHand, itemId, finalDurability);
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

        private void SetSlot(EquipSlot slot, string itemId, int durability)
        {
            var fs = new FixedString64Bytes(itemId ?? "");
            bool clear = string.IsNullOrWhiteSpace(itemId);
            int finalDurability = clear ? 0 : Mathf.Max(0, durability);

            switch (slot)
            {
                case EquipSlot.MainHand:
                    mainHand.Value = fs;
                    mainHandDurability.Value = finalDurability;
                    break;
                case EquipSlot.OffHand:
                    offHand.Value = fs;
                    offHandDurability.Value = finalDurability;
                    break;
                case EquipSlot.Head:
                    head.Value = fs;
                    headDurability.Value = finalDurability;
                    break;
                case EquipSlot.Chest:
                    chest.Value = fs;
                    chestDurability.Value = finalDurability;
                    break;
                case EquipSlot.Legs:
                    legs.Value = fs;
                    legsDurability.Value = finalDurability;
                    break;
                case EquipSlot.Feet:
                    feet.Value = fs;
                    feetDurability.Value = finalDurability;
                    break;
            }
        }

        private void SetSlotDurability(EquipSlot slot, int durability)
        {
            int finalDurability = Mathf.Max(0, durability);
            switch (slot)
            {
                case EquipSlot.MainHand: mainHandDurability.Value = finalDurability; break;
                case EquipSlot.OffHand: offHandDurability.Value = finalDurability; break;
                case EquipSlot.Head: headDurability.Value = finalDurability; break;
                case EquipSlot.Chest: chestDurability.Value = finalDurability; break;
                case EquipSlot.Legs: legsDurability.Value = finalDurability; break;
                case EquipSlot.Feet: feetDurability.Value = finalDurability; break;
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
              .Append(", Head=").Append(GetSlotString(head.Value)).Append('(').Append(headDurability.Value).Append(')')
              .Append(", Chest=").Append(GetSlotString(chest.Value)).Append('(').Append(chestDurability.Value).Append(')')
              .Append(", Legs=").Append(GetSlotString(legs.Value)).Append('(').Append(legsDurability.Value).Append(')')
              .Append(", Feet=").Append(GetSlotString(feet.Value)).Append('(').Append(feetDurability.Value).Append(')')
              .Append(']');

            if (EnsureItemDatabase())
            {
                sb.Append(" ToolTags[");
                var startLength = sb.Length;
                AppendSlotToolTags(sb, "MH", mainHand.Value);
                AppendSlotToolTags(sb, "OH", offHand.Value);
                AppendSlotToolTags(sb, "Head", head.Value);
                AppendSlotToolTags(sb, "Chest", chest.Value);
                AppendSlotToolTags(sb, "Legs", legs.Value);
                AppendSlotToolTags(sb, "Feet", feet.Value);
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



