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
    ///
    /// Strict rendering rule:
    /// - Always reset visuals first.
    /// - Then apply the current equipped item state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentSlotUI : MonoBehaviour,
        IBeginDragHandler, IEndDragHandler, IDragHandler, IDropHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Slot Identity")]
        [SerializeField] private EquipSlot slot;

        [Header("UI References")]
        [SerializeField] private Button button;
        [SerializeField] private Image iconImage;
        [SerializeField] private Image emptyBackground;
        [SerializeField] private Image durabilityBackground;
        [SerializeField] private Image durabilityFill;

        [SerializeField] private UIDragDropBroker dragDrop;
        [SerializeField] private PlayerEquipmentNet equipmentNet;
        [SerializeField] private bool debugHover;

        private string _equippedItemIdCached = string.Empty;
        private Sprite _equippedIconCached;
        private PaperdollWindowUI ownerWindow;

        public EquipSlot Slot => slot;

        private void Reset()
        {
            button = GetComponent<Button>();
            TryAutoBindDurabilityRefs();
        }

        private void Awake()
        {
            TryAutoBindDurabilityRefs();
            ResetVisuals();
        }

        private void OnEnable()
        {
            // Force clean state when reopening windows / reusing pooled objects.
            ResetVisuals();
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
                {
                    if (eq != null && eq.IsOwner)
                    {
                        equipmentNet = eq;
                        break;
                    }
                }
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
            // Reset first, then apply icon state.
            ResetVisuals();

            bool isEmpty = spriteOrNull == null;
            if (iconImage != null)
            {
                iconImage.sprite = spriteOrNull;
                iconImage.enabled = !isEmpty;
            }

            if (emptyBackground != null)
                emptyBackground.enabled = isEmpty;
        }

        public void SetDurability(int durability, int maxDurability)
        {
            if (durabilityFill == null || durabilityBackground == null)
                TryAutoBindDurabilityRefs();

            bool show = maxDurability > 0;

            SetDurabilityObjectsVisible(show);
            if (!show)
            {
                if (durabilityFill != null)
                    durabilityFill.fillAmount = 1f;
                return;
            }

            if (durabilityFill != null)
                durabilityFill.fillAmount = Mathf.Clamp01(durability / (float)maxDurability);
        }

        public void ResetVisuals()
        {
            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }

            if (emptyBackground != null)
                emptyBackground.enabled = true;

            SetDurabilityObjectsVisible(false);

            if (durabilityFill != null)
                durabilityFill.fillAmount = 1f;
        }
        private void SetDurabilityObjectsVisible(bool visible)
        {
            // Toggle at GameObject level so prefab default states cannot leak.
            if (durabilityBackground != null)
                durabilityBackground.gameObject.SetActive(visible);

            if (durabilityFill != null)
                durabilityFill.gameObject.SetActive(visible);

            // If references are missing, try best-effort transform toggles by name.
            Transform bg = transform.Find("DurabilityBG") ?? transform.Find("Durability");
            if (bg != null)
                bg.gameObject.SetActive(visible);

            Transform fill = transform.Find("DurabilityBG/DurFill")
                              ?? transform.Find("DurabilityBG/DurabilityFill")
                              ?? transform.Find("DurabilityFill")
                              ?? transform.Find("DurFill");
            if (fill != null)
                fill.gameObject.SetActive(visible);
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
                Transform fillTransform = transform.Find("DurabilityBG/DurFill");
                if (fillTransform == null)
                    fillTransform = transform.Find("DurabilityBG/DurabilityFill");
                if (fillTransform == null)
                    fillTransform = transform.Find("DurabilityFill");
                if (fillTransform == null)
                    fillTransform = transform.Find("DurFill");

                if (fillTransform != null)
                    durabilityFill = fillTransform.GetComponent<Image>();
            }
        }

        private void HandleClick()
        {
            ownerWindow?.OnSlotClicked(slot);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (debugHover)
                Debug.Log($"[PaperdollSlotUI] Hover enter slot={slot} itemId='{_equippedItemIdCached}'");

            if (string.IsNullOrWhiteSpace(_equippedItemIdCached))
            {
                ItemHoverBus.PublishClear();
                return;
            }

            ItemHoverBus.PublishHover(_equippedItemIdCached);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ItemHoverBus.PublishClear();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
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
            dragDrop?.CompleteDropOnPaperdollSlot(slot);
        }
    }
}


