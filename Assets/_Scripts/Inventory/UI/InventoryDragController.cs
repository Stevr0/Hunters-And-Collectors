using HuntersAndCollectors.Storage;
using HuntersAndCollectors.UI;
using UnityEngine;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// InventoryDragController
    /// --------------------------------------------------------------------
    /// Dedicated drag/drop router for the chest container window.
    ///
    /// Important rules:
    /// - This controller only sends transfer requests.
    /// - It never mutates inventory state locally.
    /// - Server snapshots remain the source of truth.
    /// </summary>
    public sealed class InventoryDragController : MonoBehaviour
    {
        [Header("Drag Visual")]
        [SerializeField] private UIDragGhost dragGhost;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;

        private ChestContainerNet activeChest;

        private DragPayload activePayload;

        /// <summary>
        /// Called by chest window when a chest is opened.
        /// </summary>
        public void BindChest(ChestContainerNet chest)
        {
            activeChest = chest;
            CancelDrag();
        }

        /// <summary>
        /// Called by chest window when closed/unbound.
        /// </summary>
        public void ClearChest()
        {
            activeChest = null;
            CancelDrag();
        }

        /// <summary>
        /// Starts a drag from one UI slot.
        /// </summary>
        public void BeginDrag(InventoryGridSlotUI sourceSlot, string itemId, int quantity, Sprite icon)
        {
            if (sourceSlot == null)
                return;

            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
                return;

            // Chest transfer UI should only run while a chest binding exists.
            if (activeChest == null)
                return;

            activePayload = new DragPayload
            {
                SourceContainer = sourceSlot.ContainerType,
                SourceSlotIndex = sourceSlot.SlotIndex,
                ItemId = itemId,
                Quantity = quantity
            };

            if (debugLogs)
            {
                Debug.Log(
                    $"[InventoryDrag] Drag start container={activePayload.SourceContainer} slot={activePayload.SourceSlotIndex} itemId={activePayload.ItemId} qty={activePayload.Quantity}");
            }

            if (dragGhost == null)
                dragGhost = FindFirstObjectByType<UIDragGhost>(FindObjectsInactive.Include);

            dragGhost?.Show(icon);
        }

        /// <summary>
        /// Called by drop target slots to finalize transfer intent.
        /// </summary>
        public void CompleteDropOnSlot(InventoryContainerType targetContainer, int targetSlotIndex)
        {
            if (!activePayload.IsValid)
            {
                CancelDrag();
                return;
            }

            if (activeChest == null)
            {
                CancelDrag();
                return;
            }

            if (debugLogs)
            {
                Debug.Log(
                    $"[InventoryDrag] Drop target container={targetContainer} slot={targetSlotIndex} sourceContainer={activePayload.SourceContainer} sourceSlot={activePayload.SourceSlotIndex}");
            }

            // Same-container drops are ignored in this first pass chest UX.
            if (activePayload.SourceContainer == targetContainer)
            {
                CancelDrag();
                return;
            }

            // Player -> Chest = store request.
            if (activePayload.SourceContainer == InventoryContainerType.Player &&
                targetContainer == InventoryContainerType.Chest)
            {
                activeChest.RequestStoreFromPlayerServerRpc(activePayload.SourceSlotIndex, activePayload.Quantity);

                if (debugLogs)
                {
                    Debug.Log(
                        $"[InventoryDrag] Sent store request playerSlot={activePayload.SourceSlotIndex} qty={activePayload.Quantity} itemId={activePayload.ItemId}");
                }

                CancelDrag();
                return;
            }

            // Chest -> Player = take request.
            if (activePayload.SourceContainer == InventoryContainerType.Chest &&
                targetContainer == InventoryContainerType.Player)
            {
                activeChest.RequestTakeToPlayerServerRpc(activePayload.SourceSlotIndex, activePayload.Quantity);

                if (debugLogs)
                {
                    Debug.Log(
                        $"[InventoryDrag] Sent take request chestSlot={activePayload.SourceSlotIndex} qty={activePayload.Quantity} itemId={activePayload.ItemId}");
                }

                CancelDrag();
                return;
            }

            // Any unknown combination is treated as invalid for this MVP.
            CancelDrag();
        }

        /// <summary>
        /// Cancels active drag and hides drag visual.
        /// </summary>
        public void CancelDrag()
        {
            activePayload = default;
            dragGhost?.Hide();
        }

        private struct DragPayload
        {
            public InventoryContainerType SourceContainer;
            public int SourceSlotIndex;
            public string ItemId;
            public int Quantity;

            public bool IsValid =>
                !string.IsNullOrWhiteSpace(ItemId) &&
                SourceSlotIndex >= 0 &&
                Quantity > 0;
        }
    }
}
