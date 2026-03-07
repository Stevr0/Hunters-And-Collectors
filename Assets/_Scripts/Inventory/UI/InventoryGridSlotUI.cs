using HuntersAndCollectors.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// Identifies which inventory container a slot belongs to.
    /// This is used by chest drag/drop routing to decide transfer direction.
    /// </summary>
    public enum InventoryContainerType
    {
        Player = 0,
        Chest = 1
    }

    /// <summary>
    /// Visual+clickable UI for one inventory slot.
    /// Supports drag/drop and click forwarding to window-level handlers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryGridSlotUI : MonoBehaviour,
        IBeginDragHandler, IEndDragHandler, IDragHandler, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text qtyText;
        [SerializeField] private Button button;
        [SerializeField] private UIDragDropBroker dragDrop;

        [Header("Container Context")]
        [SerializeField] private InventoryContainerType containerType = InventoryContainerType.Player;
        [SerializeField] private InventoryDragController containerDragController;

        [Header("Durability")]
        [SerializeField] private Image durabilityBackground;
        [SerializeField] private Image durabilityFill;

        [Header("Debug")]
        [SerializeField] private bool debugHover;

        private string itemId = string.Empty;
        private int quantity;
        private ItemTooltipData tooltipData;

        public int SlotIndex { get; private set; } = -1;
        public string ItemId => itemId;
        public int Quantity => quantity;
        public InventoryContainerType ContainerType => containerType;

        private System.Action<int, string, int> onClicked;

        public void SetSlotIndex(int index)
        {
            SlotIndex = index;
        }

        /// <summary>
        /// Sets both the slot index and which container this slot belongs to.
        /// </summary>
        public void SetContainerContext(InventoryContainerType type, int index)
        {
            containerType = type;
            SlotIndex = index;
        }

        /// <summary>
        /// Allows window presenters to explicitly bind a container drag controller.
        /// If this is not set, slot falls back to the generic drag broker.
        /// </summary>
        public void SetContainerDragController(InventoryDragController controller)
        {
            containerDragController = controller;
        }

        private void Reset()
        {
            button = GetComponent<Button>();
            TryAutoBindDurabilityRefs();
        }

        private void Awake()
        {
            if (dragDrop == null)
                dragDrop = FindFirstObjectByType<UIDragDropBroker>();

            if (containerDragController == null)
                containerDragController = FindFirstObjectByType<InventoryDragController>(FindObjectsInactive.Include);

            TryAutoBindDurabilityRefs();
            ResetVisuals();
        }

        public void BindClick(System.Action<int, string, int> onClick)
        {
            onClicked = onClick;
        }

        public void SetEmpty()
        {
            itemId = string.Empty;
            quantity = 0;
            tooltipData = default;
            ResetVisuals();

            if (button != null)
                button.interactable = false;
        }

        public void SetItem(string newItemId, Sprite iconSprite, int qty, int durability, int maxDurability, ItemTooltipData newTooltipData)
        {
            ResetVisuals();

            itemId = newItemId ?? string.Empty;
            quantity = Mathf.Max(0, qty);
            tooltipData = newTooltipData;

            bool hasItem = !string.IsNullOrWhiteSpace(itemId);
            if (!hasItem)
            {
                if (button != null)
                    button.interactable = false;
                return;
            }

            if (iconImage != null)
            {
                iconImage.sprite = iconSprite;
                iconImage.enabled = iconSprite != null;
            }

            if (qtyText != null && quantity > 1)
            {
                qtyText.text = quantity.ToString();
                qtyText.enabled = true;
            }

            bool showDurability = maxDurability > 0 && quantity == 1 && durability > 0;
            if (showDurability)
            {
                if (durabilityBackground != null)
                    durabilityBackground.enabled = true;

                if (durabilityFill != null)
                {
                    durabilityFill.enabled = true;
                    durabilityFill.fillAmount = Mathf.Clamp01(durability / (float)maxDurability);
                }
            }

            if (button != null)
                button.interactable = true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                return;

            if (string.IsNullOrWhiteSpace(itemId))
                return;

            // clickCount is provided by EventSystem and supports double-click UX.
            int clicks = Mathf.Max(1, eventData.clickCount);
            onClicked?.Invoke(SlotIndex, itemId, clicks);
        }

        private void ResetVisuals()
        {
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

            if (durabilityBackground != null)
                durabilityBackground.enabled = false;

            if (durabilityFill != null)
            {
                durabilityFill.enabled = false;
                durabilityFill.fillAmount = 1f;
            }
        }

        private void TryAutoBindDurabilityRefs()
        {
            if (durabilityBackground == null)
            {
                Transform bg = transform.Find("DurabilityBG") ?? transform.Find("Durability");
                if (bg != null)
                    durabilityBackground = bg.GetComponent<Image>();
            }

            if (durabilityFill == null)
            {
                Transform fill =
                    transform.Find("DurabilityBG/DurabilityFill") ??
                    transform.Find("DurabilityBG/DurFill") ??
                    transform.Find("DurabilityFill") ??
                    transform.Find("DurFill");

                if (fill != null)
                    durabilityFill = fill.GetComponent<Image>();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (debugHover)
                Debug.Log($"[InventoryGridSlotUI] Hover enter slot={SlotIndex} itemId='{itemId}' container={containerType}");

            if (string.IsNullOrWhiteSpace(itemId))
            {
                ItemHoverBus.PublishClear();
                return;
            }

            ItemHoverBus.PublishHover(tooltipData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ItemHoverBus.PublishClear();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
                return;

            // Prefer the container drag controller for player<->chest transfer UX.
            if (containerDragController != null)
            {
                containerDragController.BeginDrag(this, itemId, quantity, iconImage != null ? iconImage.sprite : null);
                return;
            }

            // Fallback for existing inventory/equipment drag flows.
            dragDrop?.BeginDragFromInventory(this, itemId, iconImage != null ? iconImage.sprite : null);
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (containerDragController != null)
            {
                containerDragController.CancelDrag();
                return;
            }

            dragDrop?.CancelDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (containerDragController != null)
            {
                containerDragController.CompleteDropOnSlot(containerType, SlotIndex);
                return;
            }

            dragDrop?.CompleteDropOnInventorySlot(SlotIndex);
        }
    }
}
