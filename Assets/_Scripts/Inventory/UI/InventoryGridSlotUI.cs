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
    /// Strict rendering rule:
    /// - Always reset visuals first.
    /// - Then apply item visuals for current slot data.
    /// This prevents stale UI state leaking between refreshes.
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
        [SerializeField] private Image durabilityBackground;
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
            TryAutoBindDurabilityRefs();
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

            TryAutoBindDurabilityRefs();
            ResetVisuals();
        }

        private void OnEnable()
        {
            // Enforce a clean base state even if prefab defaults were saved incorrectly.
            ResetVisuals();
        }

        public void BindClick(System.Action<string> onClick)
        {
            onClicked = onClick;
        }

        public void SetEmpty()
        {
            itemId = string.Empty;
            quantity = 0;
            ResetVisuals();

            if (button != null)
                button.interactable = false;
        }

        public void SetItem(string newItemId, Sprite iconSprite, int qty, int durability, int maxDurability)
        {
            // Reset-first rule: clear all old state before applying this slot's data.
            ResetVisuals();

            itemId = newItemId ?? string.Empty;
            quantity = Mathf.Max(0, qty);

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

            // Durable item overlay is only valid for single durable items.
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
                Transform bg = transform.Find("DurabilityBG");
                if (bg == null)
                    bg = transform.Find("Durability");
                if (bg != null)
                    durabilityBackground = bg.GetComponent<Image>();
            }

            if (durabilityFill == null)
            {
                Transform fill = transform.Find("DurabilityBG/DurabilityFill");
                if (fill == null)
                    fill = transform.Find("DurabilityBG/DurFill");
                if (fill == null)
                    fill = transform.Find("DurabilityFill");
                if (fill == null)
                    fill = transform.Find("DurFill");
                if (fill != null)
                    durabilityFill = fill.GetComponent<Image>();
            }
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
