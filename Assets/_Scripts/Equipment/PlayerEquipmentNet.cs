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

        public event Action OnEquipmentChanged;

        public NetworkVariable<FixedString64Bytes> MainHandNetVar => mainHand;

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

        [ServerRpc(RequireOwnership = true)]
        public void RequestEquipByItemIdServerRpc(FixedString64Bytes itemIdFs)
        {
            Debug.Log($"[Equipment][SERVER] RequestEquipByItemIdServerRpc itemId={itemIdFs} from owner={OwnerClientId}");
            if (!IsServer) return;

            var requestedId = itemIdFs.ToString();
            var itemId = CanonicalizeItemId(requestedId);
            if (!ValidateCommon(itemId, out var def)) return;

            if (inventoryNet == null || !inventoryNet.ServerHasItem(itemId, 1))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: item not in inventory. itemId={itemId} requested={requestedId}");
                return;
            }

            if (!def.IsEquippable)
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: item not equippable. itemId={itemId}");
                return;
            }

            if (!CanEquip(def, out var toUnequipA, out var toUnequipB))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: rules failed. itemId={itemId}");
                return;
            }

            if (!TryServerUnequipIfNeeded(toUnequipA)) return;
            if (!TryServerUnequipIfNeeded(toUnequipB)) return;

            if (!inventoryNet.ServerRemoveItem(itemId, 1))
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip denied: could not remove from inventory. itemId={itemId}");
                return;
            }

            ApplyEquip(def);
            LogServerEquipmentState($"Equipped itemId={def.ItemId} slot={def.EquipSlot}");
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

            SetSlot(toSlot, fromItemId);
            SetSlot(fromSlot, toItemId);
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

            bool stored = false;
            if (preferredInventoryIndex >= 0)
                stored = inventoryNet.ServerTryAddItemToSlot(equippedId, preferredInventoryIndex);

            if (!stored)
            {
                var remainder = inventoryNet.ServerAddItem(equippedId, 1);
                if (remainder > 0)
                {
                    Debug.LogWarning($"[Equipment][SERVER] Unequip denied: inventory full. slot={slot} itemId={equippedId}");
                    return false;
                }
            }

            SetSlot(slot, "");
            Debug.Log($"[Equipment][SERVER] Unequipped slot={slot} itemId={equippedId}");
            LogServerEquipmentState($"Post-unequip slot={slot}");
            return true;
        }

        private void ApplyEquip(ItemDef def)
        {
            if (def == null)
                return;

            var itemId = def.ItemId;

            if (def.EquipSlot is EquipSlot.Head or EquipSlot.Chest or EquipSlot.Legs or EquipSlot.Feet)
            {
                SetSlot(def.EquipSlot, itemId);
            }
            else if (def.Handedness == Handedness.BothHands)
            {
                SetSlot(EquipSlot.MainHand, itemId);
                SetSlot(EquipSlot.OffHand, itemId);
            }
            else if (def.Handedness == Handedness.MainHand)
            {
                SetSlot(EquipSlot.MainHand, itemId);
            }
            else if (def.Handedness == Handedness.OffHand)
            {
                SetSlot(EquipSlot.OffHand, itemId);
            }
            else
            {
                Debug.LogWarning($"[Equipment][SERVER] Equip applied to unsupported slot. itemId={itemId}");
            }

            Debug.Log($"[Equipment][SERVER] Equipped. mainHand={mainHand.Value} offHand={offHand.Value}");
            LogServerEquipmentState($"ApplyEquip itemId={itemId}");
        }

        private void SetSlot(EquipSlot slot, string itemId)
        {
            var fs = new FixedString64Bytes(itemId ?? "");

            switch (slot)
            {
                case EquipSlot.MainHand: mainHand.Value = fs; break;
                case EquipSlot.OffHand: offHand.Value = fs; break;
                case EquipSlot.Head: head.Value = fs; break;
                case EquipSlot.Chest: chest.Value = fs; break;
                case EquipSlot.Legs: legs.Value = fs; break;
                case EquipSlot.Feet: feet.Value = fs; break;
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
            var sb = new StringBuilder(256);
            sb.Append("Slots[MH=").Append(GetSlotString(mainHand.Value))
              .Append(", OH=").Append(GetSlotString(offHand.Value))
              .Append(", Head=").Append(GetSlotString(head.Value))
              .Append(", Chest=").Append(GetSlotString(chest.Value))
              .Append(", Legs=").Append(GetSlotString(legs.Value))
              .Append(", Feet=").Append(GetSlotString(feet.Value))
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
