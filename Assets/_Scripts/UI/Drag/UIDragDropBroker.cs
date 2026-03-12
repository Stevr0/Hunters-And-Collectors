using HuntersAndCollectors.Inventory.UI;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using UnityEngine;
using UnityEngine.EventSystems;

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
    /// - UI decides intent (swap, equip, unequip, world drop).
    /// - Server validates + applies via ServerRpc.
    /// </summary>
    public sealed class UIDragDropBroker : MonoBehaviour
    {
        [Header("Drag Visual")]
        [SerializeField] private UIDragGhost dragGhost;

        [Header("Debug")]
        [SerializeField] private bool debugDragTrace = true;

        // Current local drag payload (client-only)
        private DragPayload _payload;
        private bool _dropHandledThisDrag;

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

        public void BeginDragFromInventory(InventoryGridSlotUI fromSlot, string itemId, int quantity, Sprite icon)
        {
            EnsureLocalRefs();

            // Ignore empty
            if (fromSlot == null || string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
                return;

            _payload = DragPayload.FromInventory(fromSlot.SlotIndex, itemId, quantity);
            _dropHandledThisDrag = false;
            if (debugDragTrace)
                Debug.Log($"[InventoryDragTrace][Broker] BeginDrag sourceIndex={fromSlot.SlotIndex} itemId={itemId} qty={quantity}");
            dragGhost?.Show(icon);
        }

        public void BeginDragFromEquipment(EquipSlot slot, string itemId, Sprite icon)
        {
            EnsureLocalRefs();

            if (slot == EquipSlot.None || string.IsNullOrWhiteSpace(itemId))
                return;

            _payload = DragPayload.FromEquipment(slot, itemId);
            _dropHandledThisDrag = false;
            dragGhost?.Show(icon);
        }

        public void EndDrag(PointerEventData eventData)
        {
            EnsureLocalRefs();

            if (!_payload.IsValid)
            {
                CancelDrag();
                return;
            }

            if (_dropHandledThisDrag)
            {
                CancelDrag();
                return;
            }

            if (_payload.SourceKind == DragSourceKind.Inventory && _localInventoryNet != null)
            {
                int requestedQuantity = Mathf.Max(1, _payload.SourceQuantity);
                if (debugDragTrace)
                    Debug.Log($"[InventoryDragTrace][Broker] WorldDropRequest sourceIndex={_payload.SourceInventoryIndex} itemId={_payload.ItemId} qty={requestedQuantity}");

                _localInventoryNet.RequestDropSlotToWorldServerRpc(_payload.SourceInventoryIndex, requestedQuantity);
            }

            CancelDrag();
        }

        public void CancelDrag()
        {
            _payload = default;
            _dropHandledThisDrag = false;
            dragGhost?.Hide();
        }

        public void CompleteDropOnInventorySlot(int targetInventoryIndex)
        {
            EnsureLocalRefs();

            if (!_payload.IsValid)
            {
                if (debugDragTrace)
                    Debug.Log($"[InventoryDragTrace][Broker] Drop ignored reason=InvalidPayload targetIndex={targetInventoryIndex}");
                CancelDrag();
                return;
            }

            _dropHandledThisDrag = true;

            if (debugDragTrace)
                Debug.Log($"[InventoryDragTrace][Broker] CompleteDrop targetIndex={targetInventoryIndex} sourceKind={_payload.SourceKind} sourceIndex={_payload.SourceInventoryIndex}");

            // Equipment -> Inventory = unequip (MVP: server decides where it lands)
            if (_payload.SourceKind == DragSourceKind.Equipment)
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
                    if (debugDragTrace)
                        Debug.Log($"[InventoryDragTrace][Broker] RequestMove from={_payload.SourceInventoryIndex} to={targetInventoryIndex}");
                    _localInventoryNet.RequestMoveSlotServerRpc(_payload.SourceInventoryIndex, targetInventoryIndex);
                }

                CancelDrag();
                return;
            }

            CancelDrag();
        }

        public void CompleteDropOnEquipmentSlot(EquipSlot targetSlot)
        {
            EnsureLocalRefs();

            if (!_payload.IsValid)
            {
                if (debugDragTrace)
                    Debug.Log($"[InventoryDragTrace][Broker] Drop ignored reason=InvalidPayload targetEquipSlot={targetSlot}");
                CancelDrag();
                return;
            }

            _dropHandledThisDrag = true;

            if (debugDragTrace)
                Debug.Log($"[InventoryDragTrace][Broker] CompleteDrop targetEquipSlot={targetSlot} sourceKind={_payload.SourceKind} sourceIndex={_payload.SourceInventoryIndex}");

            // Inventory -> Equipment = equip
            // MVP: ignore targetSlot and just ask server to equip by rules.
            if (_payload.SourceKind == DragSourceKind.Inventory)
            {
                if (_localEquipmentNet != null)
                    _localEquipmentNet.RequestEquipFromInventorySlotServerRpc(_payload.SourceInventoryIndex);

                CancelDrag();
                return;
            }

            // Equipment -> Equipment (swap between slots)
            if (_payload.SourceKind == DragSourceKind.Equipment && _payload.SourceEquipSlot != targetSlot)
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

        private enum DragSourceKind { None, Inventory, Equipment }

        private struct DragPayload
        {
            public DragSourceKind SourceKind;
            public int SourceInventoryIndex;
            public int SourceQuantity;
            public EquipSlot SourceEquipSlot;
            public Unity.Collections.FixedString64Bytes ItemId;

            public bool IsValid => SourceKind != DragSourceKind.None && !ItemId.IsEmpty;

            public static DragPayload FromInventory(int index, string itemId, int quantity)
            {
                return new DragPayload
                {
                    SourceKind = DragSourceKind.Inventory,
                    SourceInventoryIndex = index,
                    SourceQuantity = Mathf.Max(1, quantity),
                    SourceEquipSlot = EquipSlot.None,
                    ItemId = new Unity.Collections.FixedString64Bytes(itemId ?? "")
                };
            }

            public static DragPayload FromEquipment(EquipSlot slot, string itemId)
            {
                return new DragPayload
                {
                    SourceKind = DragSourceKind.Equipment,
                    SourceInventoryIndex = -1,
                    SourceQuantity = 1,
                    SourceEquipSlot = slot,
                    ItemId = new Unity.Collections.FixedString64Bytes(itemId ?? "")
                };
            }
        }
    }
}
