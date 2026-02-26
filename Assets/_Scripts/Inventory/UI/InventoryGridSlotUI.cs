using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using HuntersAndCollectors.UI;


namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// InventoryGridSlotUI
    /// ---------------------------------------------------------
    /// Visual+clickable UI for ONE inventory slot.
    ///
    /// Now also publishes hover events so other windows can show item details.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryGridSlotUI : MonoBehaviour,
       IBeginDragHandler, IEndDragHandler, IDragHandler, IDropHandler
    {
        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text qtyText;
        [SerializeField] private Button button;

        [SerializeField] private UIDragDropBroker dragDrop; // assign in prefab or auto-find

        // Current item in this UI slot (empty string = empty).
        private string itemId = string.Empty;
        private int quantity;

        public int SlotIndex { get; private set; } = -1;

        /// <summary>
        /// Called by the inventory window so this slot knows which inventory index it represents.
        /// Drag/drop needs this index for ServerRpc requests.
        /// </summary>
        public void SetSlotIndex(int index)
        {
            SlotIndex = index;
        }

        // Callback set by the window so the slot doesn't need to know "what to do".
        private System.Action<string> onClicked;

        private void Reset()
        {
            button = GetComponent<Button>();
        }

        private void Awake()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }
            if (dragDrop == null)
                dragDrop = FindObjectOfType<UIDragDropBroker>(true);
        }

        public void BindClick(System.Action<string> onClick)
        {
            onClicked = onClick;
        }

        public void SetEmpty()
        {
            itemId = string.Empty;
            quantity = 0;

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }

            if (qtyText != null)
            {
                qtyText.text = string.Empty;
                qtyText.enabled = false;
            }

            if (button != null)
                button.interactable = false;
        }

        public void SetItem(string newItemId, Sprite iconSprite, int qty)
        {
            itemId = newItemId ?? string.Empty;
            quantity = qty;

            if (iconImage != null)
            {
                iconImage.sprite = iconSprite;
                iconImage.enabled = iconSprite != null;
            }

            if (qtyText != null)
            {
                if (qty > 1)
                {
                    qtyText.text = qty.ToString();
                    qtyText.enabled = true;
                }
                else
                {
                    qtyText.text = string.Empty;
                    qtyText.enabled = false;
                }
            }

            if (button != null)
                button.interactable = !string.IsNullOrWhiteSpace(itemId);
        }

        private void HandleClick()
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            onClicked?.Invoke(itemId);
        }

        /// <summary>
        /// Unity UI event: mouse entered this slot.
        /// If the slot has an item, publish it so other UI (paperdoll) can show details.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            // If empty, clear hover (prevents stale info text).
            ItemHoverBus.PublishHover(itemId);
        }

        /// <summary>
        /// Unity UI event: mouse left this slot.
        /// Clear the hover.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            ItemHoverBus.PublishClear();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Only drag if slot has an item.
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            // Start a drag payload from inventory.
            dragDrop?.BeginDragFromInventory(this, itemId, iconImage != null ? iconImage.sprite : null);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Drag ghost follows mouse via UIDragGhost.Update()
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // If we ended drag NOT over a valid drop target,
            // we still want to hide the ghost.
            dragDrop?.CancelDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            // Something was dropped onto this inventory slot.
            // We don't need to inspect eventData; our broker knows the current payload.
            dragDrop?.CompleteDropOnInventorySlot(SlotIndex);
        }
    }
}