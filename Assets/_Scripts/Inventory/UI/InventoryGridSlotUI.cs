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
    /// Also publishes hover events so other windows can show item details.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryGridSlotUI : MonoBehaviour,
        IBeginDragHandler, IEndDragHandler, IDragHandler, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text qtyText;
        [SerializeField] private Button button;
        [SerializeField] private UIDragDropBroker dragDrop; // assign in prefab or auto-find

        [Header("Durability")]
        [SerializeField] private Image durabilityFill;

        [Header("Debug")]
        [SerializeField] private bool debugHover;

        private string itemId = string.Empty;
        private int quantity;

        public int SlotIndex { get; private set; } = -1;

        public void SetSlotIndex(int index)
        {
            SlotIndex = index;
        }

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

            SetDurability(0, 0);

            if (button != null)
                button.interactable = false;
        }

        public void SetItem(string newItemId, Sprite iconSprite, int qty, int durability, int maxDurability)
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

            SetDurability(durability, maxDurability);

            if (button != null)
                button.interactable = !string.IsNullOrWhiteSpace(itemId);
        }

        public void SetDurability(int durability, int maxDurability)
        {
            if (durabilityFill == null)
                return;

            bool show = maxDurability > 0;
            durabilityFill.enabled = show;
            if (!show)
                return;

            float fill = maxDurability <= 0 ? 0f : Mathf.Clamp01(durability / (float)maxDurability);
            durabilityFill.fillAmount = fill;
        }

        private void HandleClick()
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            onClicked?.Invoke(itemId);
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

            ItemHoverBus.PublishHover(itemId);
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
