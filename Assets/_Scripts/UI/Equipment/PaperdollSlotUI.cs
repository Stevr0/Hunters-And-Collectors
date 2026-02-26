using HuntersAndCollectors.Items;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using HuntersAndCollectors.Players;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// UI component for ONE equipment slot.
    /// Displays an icon and forwards clicks to the window.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PaperdollSlotUI : MonoBehaviour,
        IBeginDragHandler, IEndDragHandler, IDragHandler, IDropHandler
    {
        [Header("Slot Identity")]
        [SerializeField] private EquipSlot slot;

        [Header("UI References")]
        [SerializeField] private Button button;
        [SerializeField] private Image iconImage;
        [SerializeField] private Image emptyBackground;

        [SerializeField] private UIDragDropBroker dragDrop;
        [SerializeField] private PlayerEquipmentNet equipmentNet; // optional auto-find
        private string _equippedItemIdCached = string.Empty;
        private Sprite _equippedIconCached = null;

        private PaperdollWindowUI ownerWindow;

        public EquipSlot Slot => slot;

        private void Reset()
        {
            button = GetComponent<Button>();
        }

        public void Bind(PaperdollWindowUI window)
        {
            ownerWindow = window;

            if (dragDrop == null)
                dragDrop = FindObjectOfType<UIDragDropBroker>(true);

            if (equipmentNet == null)
            {
                var all = FindObjectsOfType<PlayerEquipmentNet>(true);
                foreach (var eq in all)
                    if (eq != null && eq.IsOwner) { equipmentNet = eq; break; }
            }

            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }
        }

        public void SetInteractable(bool canInteract)
        {
            if (button != null)
                button.interactable = canInteract;
        }

        public void SetEquippedItemCache(string itemId, Sprite icon)
        {
            _equippedItemIdCached = itemId ?? string.Empty;
            _equippedIconCached = icon;
        }

        public void SetIcon(Sprite spriteOrNull)
        {
            bool isEmpty = spriteOrNull == null;

            if (iconImage != null)
            {
                iconImage.sprite = spriteOrNull;
                iconImage.enabled = !isEmpty;
            }

            if (emptyBackground != null)
                emptyBackground.enabled = isEmpty;
        }

        private void HandleClick()
        {
            if (ownerWindow != null)
                ownerWindow.OnSlotClicked(slot);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // Only drag if something is equipped here
            if (string.IsNullOrWhiteSpace(_equippedItemIdCached))
                return;

            dragDrop?.BeginDragFromPaperdoll(slot, _equippedItemIdCached, _equippedIconCached);
        }

        public void OnDrag(PointerEventData eventData) { }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragDrop?.CancelDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            // Inventory item dropped onto this equipment slot
            dragDrop?.CompleteDropOnPaperdollSlot(slot);
        }
    }
}