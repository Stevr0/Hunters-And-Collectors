using System;
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
    ///
    /// Goals:
    /// - Clients request equip/unequip.
    /// - Server validates inventory + rules (slot/handedness).
    /// - Server updates NetworkVariables so UI can show paperdoll.
    ///
    /// MVP simplifications:
    /// - Equipping moves 1 quantity out of inventory into equipment.
    /// - Unequipping moves 1 quantity back into inventory.
    /// - If inventory is full on unequip, server denies (no item loss).
    /// - No durability, no visuals/anim, no stat application yet.
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

        // --------------------------------------------------------------------
        // Replicated state (everyone can read, only server can write)
        // We store stable item ids as FixedString (safe for NGO replication).
        // Empty string means "nothing equipped".
        // --------------------------------------------------------------------

        private readonly NetworkVariable<FixedString64Bytes> mainHand =
            new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> offHand =
            new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> head =
            new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> chest =
            new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> legs =
            new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> feet =
            new("", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// Fired on clients (and host) whenever any equipment slot changes.
        /// Paperdoll UI should subscribe to this.
        /// </summary>
        public event Action OnEquipmentChanged;

        // --------------------------------------------------------------------
        // Public read APIs (safe to call on client for UI)
        // --------------------------------------------------------------------

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

        public bool HasEquippedItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId)) return false;

            // Compare stable ids across all slots.
            return mainHand.Value.ToString() == itemId
                || offHand.Value.ToString() == itemId
                || head.Value.ToString() == itemId
                || chest.Value.ToString() == itemId
                || legs.Value.ToString() == itemId
                || feet.Value.ToString() == itemId;
        }

        public bool HasToolTag(ToolTag tag)
        {
            if (tag == ToolTag.None) return false;
            if (itemDatabase == null) return false;

            // Check each equipped item; if its ItemDef contains the tag, return true.
            return EquippedItemHasToolTag(mainHand.Value, tag)
                || EquippedItemHasToolTag(offHand.Value, tag)
                || EquippedItemHasToolTag(head.Value, tag)
                || EquippedItemHasToolTag(chest.Value, tag)
                || EquippedItemHasToolTag(legs.Value, tag)
                || EquippedItemHasToolTag(feet.Value, tag);
        }

        private bool EquippedItemHasToolTag(FixedString64Bytes itemIdFs, ToolTag tag)
        {
            var id = itemIdFs.ToString();
            if (string.IsNullOrWhiteSpace(id)) return false;

            if (!itemDatabase.TryGet(id, out var def) || def == null) return false;
            return def.ToolTags != null && def.ToolTags.Contains(tag);
        }

        // --------------------------------------------------------------------
        // NGO lifecycle
        // --------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            // Auto-wire if possible (nice QoL).
            if (inventoryNet == null) inventoryNet = GetComponent<PlayerInventoryNet>();

            // Subscribe to netvar changes (for UI refresh)
            mainHand.OnValueChanged += OnAnySlotChanged;
            offHand.OnValueChanged += OnAnySlotChanged;
            head.OnValueChanged += OnAnySlotChanged;
            chest.OnValueChanged += OnAnySlotChanged;
            legs.OnValueChanged += OnAnySlotChanged;
            feet.OnValueChanged += OnAnySlotChanged;

            // Fire once for UI on spawn (so paperdoll can paint initial state).
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
        }

        private void OnAnySlotChanged(FixedString64Bytes prev, FixedString64Bytes next)
        {
            OnEquipmentChanged?.Invoke();
        }

        // --------------------------------------------------------------------
        // Client -> Server requests
        // --------------------------------------------------------------------

        /// <summary>
        /// Client asks the server to equip ONE quantity of an item by itemId.
        /// MVP note: since your inventory is stacks, we equip "one unit" of that itemId.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        public void RequestEquipByItemIdServerRpc(FixedString64Bytes itemIdFs)
        {
            if (!IsServer) return;

            var itemId = itemIdFs.ToString();
            if (!ValidateCommon(itemId, out var def)) return;

            // 1) Must exist in player's inventory
            if (inventoryNet == null || !inventoryNet.ServerHasItem(itemId, 1))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: item not in inventory. itemId={itemId}");
                return;
            }

            // 2) Must be equippable
            if (!def.IsEquippable)
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: item not equippable. itemId={itemId}");
                return;
            }

            // 3) Validate slot/handedness rules + compute what to unequip (if anything)
            if (!CanEquip(def, out var toUnequipA, out var toUnequipB))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: rules failed. itemId={itemId}");
                return;
            }

            // 4) Try to unequip displaced items first (back to inventory).
            //    IMPORTANT: We must ensure we can store them, otherwise deny equip.
            if (!TryServerUnequipIfNeeded(toUnequipA)) return;
            if (!TryServerUnequipIfNeeded(toUnequipB)) return;

            // 5) Remove the equipped item from inventory (authoritative).
            if (!inventoryNet.ServerRemoveItem(itemId, 1))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: could not remove from inventory. itemId={itemId}");
                return;
            }

            // 6) Finally set equipment netvars.
            ApplyEquip(def);

            Debug.Log($"[Equipment][SERVER] Equipped itemId={itemId} on {def.EquipSlot} handed={def.Handedness}");
        }

        /// <summary>
        /// Client asks the server to unequip a specific slot.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        public void RequestUnequipSlotServerRpc(EquipSlot slot)
        {
            if (!IsServer) return;
            if (inventoryNet == null) return;

            // Determine the equipped itemId (string) for that slot.
            var equippedId = GetEquippedItemId(slot);
            if (string.IsNullOrWhiteSpace(equippedId))
                return; // nothing to do

            // If the equipped item is BothHands, unequip BOTH MainHand and OffHand together.
            if (itemDatabase != null && itemDatabase.TryGet(equippedId, out var def) && def != null)
            {
                if (def.Handedness == Handedness.BothHands)
                {
                    if (!TryServerUnequipIfNeeded(EquipSlot.MainHand)) return;
                    if (!TryServerUnequipIfNeeded(EquipSlot.OffHand)) return;
                    return;
                }
            }

            // Otherwise just unequip this one slot.
            TryServerUnequipIfNeeded(slot);
        }

        // --------------------------------------------------------------------
        // Validation + rule logic
        // --------------------------------------------------------------------

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

        /// <summary>
        /// Determines if we can equip the item, and which slots (if any) must be unequipped first.
        /// </summary>
        private bool CanEquip(ItemDef def, out EquipSlot unequipA, out EquipSlot unequipB)
        {
            unequipA = EquipSlot.None;
            unequipB = EquipSlot.None;

            // Non-hand equipment is straightforward:
            // - must target that exact slot
            // - will replace what is currently in that slot
            if (def.EquipSlot is EquipSlot.Head or EquipSlot.Chest or EquipSlot.Legs or EquipSlot.Feet)
            {
                unequipA = def.EquipSlot;
                return true;
            }

            // Hand items:
            // - EquipSlot should be MainHand or OffHand
            // - Handedness rules determine whether it also occupies the other hand
            if (def.EquipSlot != EquipSlot.MainHand && def.EquipSlot != EquipSlot.OffHand)
                return false;

            // Item claims "BothHands": it will occupy BOTH slots regardless of primary slot.
            if (def.Handedness == Handedness.BothHands)
            {
                unequipA = EquipSlot.MainHand;
                unequipB = EquipSlot.OffHand;
                return true;
            }

            // MainHand-only item:
            if (def.Handedness == Handedness.MainHand)
            {
                unequipA = EquipSlot.MainHand;

                // If offhand currently holds a BothHands item (shouldn't happen if we enforce),
                // we'd need to clear it too. We'll be defensive:
                if (IsBothHandsEquippedInEitherHand())
                    unequipB = EquipSlot.OffHand;

                return true;
            }

            // OffHand-only item:
            if (def.Handedness == Handedness.OffHand)
            {
                unequipA = EquipSlot.OffHand;

                if (IsBothHandsEquippedInEitherHand())
                    unequipB = EquipSlot.MainHand;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if either hand currently has an item whose handedness is BothHands.
        /// Defensive: ensures equipping one-hand items can't coexist with a two-hand item.
        /// </summary>
        private bool IsBothHandsEquippedInEitherHand()
        {
            if (itemDatabase == null) return false;

            var mh = mainHand.Value.ToString();
            if (!string.IsNullOrWhiteSpace(mh) && itemDatabase.TryGet(mh, out var defMh) && defMh != null)
                if (defMh.Handedness == Handedness.BothHands) return true;

            var oh = offHand.Value.ToString();
            if (!string.IsNullOrWhiteSpace(oh) && itemDatabase.TryGet(oh, out var defOh) && defOh != null)
                if (defOh.Handedness == Handedness.BothHands) return true;

            return false;
        }

        // --------------------------------------------------------------------
        // Server equip/unequip operations (authoritative)
        // --------------------------------------------------------------------

        private bool TryServerUnequipIfNeeded(EquipSlot slot)
        {
            if (slot == EquipSlot.None) return true;

            var equippedId = GetEquippedItemId(slot);
            if (string.IsNullOrWhiteSpace(equippedId))
                return true; // already empty

            // Try to add back to inventory first (so we don't delete items if full).
            var remainder = inventoryNet.ServerAddItem(equippedId, 1);
            if (remainder > 0)
            {
                Debug.LogWarning($"[Equipment][SERVER] Unequip denied: inventory full. slot={slot} itemId={equippedId}");
                // We did NOT clear the slot since we couldn't store the item.
                return false;
            }

            // Now clear the slot.
            SetSlot(slot, "");
            Debug.Log($"[Equipment][SERVER] Unequipped slot={slot} itemId={equippedId}");
            return true;
        }

        private void ApplyEquip(ItemDef def)
        {
            var itemId = def.ItemId;

            // Armor slots simply set their slot
            if (def.EquipSlot is EquipSlot.Head or EquipSlot.Chest or EquipSlot.Legs or EquipSlot.Feet)
            {
                SetSlot(def.EquipSlot, itemId);
                return;
            }

            // Hand items:
            if (def.Handedness == Handedness.BothHands)
            {
                SetSlot(EquipSlot.MainHand, itemId);
                SetSlot(EquipSlot.OffHand, itemId);
                return;
            }

            if (def.Handedness == Handedness.MainHand)
            {
                SetSlot(EquipSlot.MainHand, itemId);
                return;
            }

            if (def.Handedness == Handedness.OffHand)
            {
                SetSlot(EquipSlot.OffHand, itemId);
                return;
            }
        }

        private void SetSlot(EquipSlot slot, string itemId)
        {
            // Store empty as "" so replication is clean.
            var fs = new FixedString64Bytes(itemId ?? "");

            switch (slot)
            {
                case EquipSlot.MainHand: mainHand.Value = fs; break;
                case EquipSlot.OffHand:  offHand.Value = fs; break;
                case EquipSlot.Head:     head.Value = fs; break;
                case EquipSlot.Chest:    chest.Value = fs; break;
                case EquipSlot.Legs:     legs.Value = fs; break;
                case EquipSlot.Feet:     feet.Value = fs; break;
            }
        }
    }
}
