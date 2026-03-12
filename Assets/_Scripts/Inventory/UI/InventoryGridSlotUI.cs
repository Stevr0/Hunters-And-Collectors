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
        PlayerInventory = 0,
        StorageInventory = 1,

        // Backward-compatible aliases for existing serialized values/usages.
        Player = PlayerInventory,
        Chest = StorageInventory
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
        [SerializeField] private Image dropHitboxImage;
        [SerializeField] private UIDragDropBroker dragDrop;

        [Header("Container Context")]
        [SerializeField] private InventoryContainerType containerType = InventoryContainerType.PlayerInventory;
        [SerializeField] private InventoryDragController containerDragController;

        [Header("Durability")]
        [SerializeField] private Image durabilityBackground;
        [SerializeField] private Image durabilityFill;

        [Header("Equipped Visual")]
        [Tooltip("Optional border/overlay image shown when this inventory slot is reference-equipped.")]
        [SerializeField] private Image equippedStateImage;
        [SerializeField] private Color equippedStateColor = new Color(0.92f, 0.78f, 0.24f, 0.9f);
        [SerializeField] private bool tintIconWhenEquipped = true;
        [SerializeField] private Color equippedIconTint = new Color(1f, 0.97f, 0.78f, 1f);

        [Header("Debug")]
        [SerializeField] private bool debugHover;
        [SerializeField] private bool debugDragTrace = true;

        private string itemId = string.Empty;
        private int quantity;
        private ItemTooltipData tooltipData;
        private bool isReferenceEquipped;

        public int SlotIndex { get; private set; } = -1;
        public string ItemId => itemId;
        public int Quantity => quantity;
        public InventoryContainerType ContainerType => containerType;

        private System.Action<int, string, int> onClicked;
        private System.Action<int, string> onRightClicked;

        // Optional gating callback so windows can disable drag in specific modes.
        private System.Func<bool> canStartDragResolver;

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
        /// </summary>
        public void SetContainerDragController(InventoryDragController controller)
        {
            containerDragController = controller;
        }

        public void BindCanStartDrag(System.Func<bool> resolver)
        {
            canStartDragResolver = resolver;
        }

        /// <summary>
        /// Presentation-only marker for hybrid equipment reference slots.
        /// This never mutates authoritative inventory state.
        /// </summary>
        public void SetReferenceEquipped(bool equipped)
        {
            isReferenceEquipped = equipped;
            ApplyEquippedVisual();
        }

        private bool CanStartDrag()
        {
            if (canStartDragResolver == null)
                return true;

            return canStartDragResolver();
        }

        private bool ShouldUseContainerDragController()
        {
            // Only use chest transfer routing when an active chest session exists.
            return containerDragController != null && containerDragController.HasActiveChest;
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

            EnsureDropHitboxGraphic();
            TryAutoBindDurabilityRefs();
            ResetVisuals();
        }

        public void BindClick(System.Action<int, string, int> onClick)
        {
            onClicked = onClick;
        }

        public void BindRightClick(System.Action<int, string> onRightClick)
        {
            onRightClicked = onRightClick;
        }

        public void SetEmpty()
        {
            itemId = string.Empty;
            quantity = 0;
            tooltipData = default;
            ResetVisuals();

            // Keep the slot raycastable so empty slots can still act as valid drop targets.
            // Click handlers already guard against empty itemIds.
            if (button != null)
                button.interactable = true;
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
                    button.interactable = true;
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
            if (eventData == null || string.IsNullOrWhiteSpace(itemId))
                return;

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                int clicks = Mathf.Max(1, eventData.clickCount);
                onClicked?.Invoke(SlotIndex, itemId, clicks);
                return;
            }

            if (eventData.button == PointerEventData.InputButton.Right)
                onRightClicked?.Invoke(SlotIndex, itemId);
        }


        private void EnsureDropHitboxGraphic()
        {
            if (dropHitboxImage == null)
                dropHitboxImage = GetComponent<Image>();

            if (dropHitboxImage == null)
                dropHitboxImage = gameObject.AddComponent<Image>();

            // Transparent but raycastable catch-all target so OnDrop still fires even when
            // icon/quantity visuals are disabled for empty slots.
            dropHitboxImage.color = new Color(1f, 1f, 1f, 0f);
            dropHitboxImage.raycastTarget = true;
        }
        private void ApplyEquippedVisual()
        {
            if (equippedStateImage != null)
            {
                equippedStateImage.enabled = isReferenceEquipped;
                if (isReferenceEquipped)
                    equippedStateImage.color = equippedStateColor;
            }

            if (iconImage != null)
            {
                iconImage.color = !tintIconWhenEquipped
                    ? Color.white
                    : (isReferenceEquipped ? equippedIconTint : Color.white);
            }
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

            ApplyEquippedVisual();
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

            if (!CanStartDrag())
            {
                if (debugDragTrace)
                    Debug.Log($"[InventoryDragTrace][Slot] BeginDrag blocked slot={SlotIndex} container={containerType} reason=CanStartDragFalse");
                return;
            }

            if (debugDragTrace)
                Debug.Log($"[InventoryDragTrace][Slot] BeginDrag sourceIndex={SlotIndex} itemId={itemId} qty={quantity} container={containerType}");

            if (ShouldUseContainerDragController())
            {
                containerDragController.BeginDrag(this, itemId, quantity, iconImage != null ? iconImage.sprite : null);
                return;
            }

            dragDrop?.BeginDragFromInventory(this, itemId, quantity, iconImage != null ? iconImage.sprite : null);
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (ShouldUseContainerDragController())
            {
                containerDragController.CancelDrag();
                return;
            }

            dragDrop?.EndDrag(eventData);
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (!CanStartDrag())
            {
                if (debugDragTrace)
                    Debug.Log($"[InventoryDragTrace][Slot] Drop blocked targetIndex={SlotIndex} container={containerType} reason=CanStartDragFalse");
                return;
            }

            if (debugDragTrace)
                Debug.Log($"[InventoryDragTrace][Slot] OnDrop targetIndex={SlotIndex} container={containerType}");

            if (ShouldUseContainerDragController())
            {
                containerDragController.CompleteDropOnSlot(containerType, SlotIndex);
                return;
            }

            dragDrop?.CompleteDropOnInventorySlot(SlotIndex);
        }
    }
}




