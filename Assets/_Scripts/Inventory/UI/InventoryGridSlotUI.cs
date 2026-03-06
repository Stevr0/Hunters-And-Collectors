using HuntersAndCollectors.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HuntersAndCollectors.Inventory.UI
{
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

        [Header("Durability")]
        [SerializeField] private Image durabilityBackground;
        [SerializeField] private Image durabilityFill;

        [Header("Debug")]
        [SerializeField] private bool debugHover;

        private string itemId = string.Empty;
        private int quantity;
        private ItemTooltipData tooltipData;

        public int SlotIndex { get; private set; } = -1;

        private System.Action<int, string, int> onClicked;

        public void SetSlotIndex(int index)
        {
            SlotIndex = index;
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
                Debug.Log($"[InventoryGridSlotUI] Hover enter slot={SlotIndex} itemId='{itemId}'");

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
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            dragDrop?.BeginDragFromInventory(this, itemId, iconImage != null ? iconImage.sprite : null);
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragDrop?.CancelDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            dragDrop?.CompleteDropOnInventorySlot(SlotIndex);
        }
    }
}
