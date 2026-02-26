using HuntersAndCollectors.Inventory.UI;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// UIDragDropBroker
    /// --------------------------------------------------------------------
    /// Central place that performs actions when a drop happens.
    ///
    /// Why central?
    /// - Slots don't need to know about each other.
    /// - Keeps "what happens on drop" consistent and easy to extend.
    ///
    /// Authority rules:
    /// - UI decides intent (swap, equip, unequip).
    /// - Server validates + applies via ServerRpc.
    /// </summary>
    public sealed class UIDragDropBroker : MonoBehaviour
    {
        [Header("Drag Visual")]
        [SerializeField] private UIDragGhost dragGhost;

        // Current local drag payload (client-only)
        private DragPayload _payload;

        // Cached local player refs (client-side)
        private Inventory.PlayerInventoryNet _localInventoryNet;
        private PlayerEquipmentNet _localEquipmentNet;

        private void Awake()
        {
            if (dragGhost == null)
                dragGhost = FindObjectOfType<UIDragGhost>(true);
        }

        private void EnsureLocalRefs()
        {
            if (_localInventoryNet != null && _localEquipmentNet != null)
                return;

            // Find local owner objects (simple MVP approach).
            var invs = FindObjectsOfType<Inventory.PlayerInventoryNet>(true);
            foreach (var inv in invs)
                if (inv != null && inv.IsOwner) { _localInventoryNet = inv; break; }

            var eqs = FindObjectsOfType<PlayerEquipmentNet>(true);
            foreach (var eq in eqs)
                if (eq != null && eq.IsOwner) { _localEquipmentNet = eq; break; }
        }

        // ------------------------
        // Begin / End Drag
        // ------------------------

        public void BeginDragFromInventory(InventoryGridSlotUI fromSlot, string itemId, Sprite icon)
        {
            EnsureLocalRefs();

            // Ignore empty
            if (fromSlot == null || string.IsNullOrWhiteSpace(itemId))
                return;

            _payload = DragPayload.FromInventory(fromSlot.SlotIndex, itemId);
            dragGhost?.Show(icon);
        }

        public void BeginDragFromPaperdoll(EquipSlot slot, string itemId, Sprite icon)
        {
            EnsureLocalRefs();

            if (slot == EquipSlot.None || string.IsNullOrWhiteSpace(itemId))
                return;

            _payload = DragPayload.FromPaperdoll(slot, itemId);
            dragGhost?.Show(icon);
        }

        public void CancelDrag()
        {
            _payload = default;
            dragGhost?.Hide();
        }

        public void CompleteDropOnInventorySlot(int targetInventoryIndex)
        {
            EnsureLocalRefs();

            if (!_payload.IsValid)
            {
                CancelDrag();
                return;
            }

            // Paperdoll -> Inventory = unequip (MVP: server decides where it lands)
            if (_payload.SourceKind == DragSourceKind.Paperdoll)
            {
                if (_localEquipmentNet != null)
                    _localEquipmentNet.RequestUnequipSlotServerRpc(_payload.SourceEquipSlot, targetInventoryIndex);

                CancelDrag();
                return;
            }

            // Inventory -> Inventory = swap/move
            if (_payload.SourceKind == DragSourceKind.Inventory)
            {
                if (_localInventoryNet != null)
                {
                    // ✅ YOU NEED THIS RPC ON PlayerInventoryNet:
                    // RequestSwapSlotsServerRpc(int a, int b)
                    _localInventoryNet.RequestMoveSlotServerRpc(_payload.SourceInventoryIndex, targetInventoryIndex); ;
                }

                CancelDrag();
                return;
            }

            CancelDrag();
        }

        public void CompleteDropOnPaperdollSlot(EquipSlot targetSlot)
        {
            EnsureLocalRefs();

            if (!_payload.IsValid)
            {
                CancelDrag();
                return;
            }

            // Inventory -> Paperdoll = equip
            // MVP: ignore targetSlot and just ask server to equip by rules.
            if (_payload.SourceKind == DragSourceKind.Inventory)
            {
                if (_localEquipmentNet != null)
                    _localEquipmentNet.RequestEquipByItemIdServerRpc(_payload.ItemId);

                CancelDrag();
                return;
            }

            // Paperdoll -> Paperdoll (swap between slots)
            if (_payload.SourceKind == DragSourceKind.Paperdoll && _payload.SourceEquipSlot != targetSlot)
            {
                if (_localEquipmentNet != null)
                    _localEquipmentNet.RequestSwapEquipSlotsServerRpc(_payload.SourceEquipSlot, targetSlot);

                CancelDrag();
                return;
            }

            CancelDrag();
        }

        // ------------------------
        // Payload struct
        // ------------------------

        private enum DragSourceKind { None, Inventory, Paperdoll }

        private struct DragPayload
        {
            public DragSourceKind SourceKind;
            public int SourceInventoryIndex;
            public EquipSlot SourceEquipSlot;
            public Unity.Collections.FixedString64Bytes ItemId;

            public bool IsValid => SourceKind != DragSourceKind.None && !ItemId.IsEmpty;

            public static DragPayload FromInventory(int index, string itemId)
            {
                return new DragPayload
                {
                    SourceKind = DragSourceKind.Inventory,
                    SourceInventoryIndex = index,
                    SourceEquipSlot = EquipSlot.None,
                    ItemId = new Unity.Collections.FixedString64Bytes(itemId ?? "")
                };
            }

            public static DragPayload FromPaperdoll(EquipSlot slot, string itemId)
            {
                return new DragPayload
                {
                    SourceKind = DragSourceKind.Paperdoll,
                    SourceInventoryIndex = -1,
                    SourceEquipSlot = slot,
                    ItemId = new Unity.Collections.FixedString64Bytes(itemId ?? "")
                };
            }
        }
    }
}