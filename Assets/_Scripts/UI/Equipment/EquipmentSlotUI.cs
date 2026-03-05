using System;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
        [SerializeField] private bool debugDurability = true;

        private string _equippedItemIdCached = string.Empty;
        private Sprite _equippedIconCached;
        private ItemTooltipData _tooltipDataCached;
        private EquipmentWindowUI ownerWindow;

        private static Sprite fallbackWhiteSprite;

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

        public void Bind(EquipmentWindowUI window)
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

        public void SetTooltipData(ItemTooltipData tooltipData)
        {
            _tooltipDataCached = tooltipData;
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

        public void SetDurability(int durability, int maxDurability)
        {
            if (durabilityFill == null || durabilityBackground == null)
                TryAutoBindDurabilityRefs();

            bool show = maxDurability > 0;
            SetDurabilityObjectsVisible(show);

            if (!show || durabilityFill == null)
                return;

            float fill = Mathf.Clamp01(durability / (float)Mathf.Max(1, maxDurability));
            durabilityFill.fillAmount = fill;
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
            if (durabilityBackground == null)
                TryAutoBindDurabilityRefs();

            if (durabilityBackground != null)
            {
                durabilityBackground.gameObject.SetActive(visible);
                durabilityBackground.enabled = visible;
            }

            // DurabilityFill is a CHILD of the background, so we never toggle it directly.
            // When the BG is hidden, the Fill is hidden automatically.
        }

        private void TryAutoBindDurabilityRefs()
        {
            if (durabilityBackground == null)
            {
                Transform bg = transform.Find("DurabilityBG") ?? transform.Find("Durability");
                if (bg != null)
                    durabilityBackground = bg.GetComponent<Image>();

                if (durabilityBackground == null)
                {
                    Image[] all = GetComponentsInChildren<Image>(true);
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null)
                            continue;

                        string name = all[i].name;
                        if (name.IndexOf("dur", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            name.IndexOf("fill", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            durabilityBackground = all[i];
                            break;
                        }
                    }
                }
            }

            if (durabilityFill == null)
            {
                // 1) Direct child (your prefab case: Slot_Mainhand/DurabilityFill)
                Transform t = transform.Find("DurabilityFill") ?? transform.Find("DurFill");
                if (t != null)
                    durabilityFill = t.GetComponent<Image>();
            }

            if (durabilityFill == null)
            {
                // 2) If it's under BG for any prefab variants (older layouts)
                Transform t = transform.Find("DurabilityBG/DurabilityFill")
                             ?? transform.Find("DurabilityBG/DurFill");
                if (t != null)
                    durabilityFill = t.GetComponent<Image>();
            }

            if (durabilityFill == null)
            {
                // 3) Final fallback: search any child image with "fill" in name (includes inactive)
                var images = GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i];
                    if (img == null) continue;

                    string n = img.name;
                    if (n.IndexOf("fill", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        n.IndexOf("dur", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        durabilityFill = img;
                        break;
                    }
                }
            }
        }

        private static Image FindBestFillCandidate(Image[] images, Image background)
        {
            if (images == null || images.Length == 0)
                return null;

            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null || img == background)
                    continue;

                string n = img.name;
                bool hasDur = n.IndexOf("dur", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasFill = n.IndexOf("fill", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasFill || hasDur)
                    return img;
            }

            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img != null && img != background)
                    return img;
            }

            return null;
        }

        private void EnsureFillLayout()
        {
            if (durabilityBackground == null || durabilityFill == null)
                return;

            RectTransform bgRt = durabilityBackground.rectTransform;
            RectTransform fillRt = durabilityFill.rectTransform;
            if (bgRt == null || fillRt == null)
                return;

            if (fillRt.parent != bgRt)
                fillRt.SetParent(bgRt, false);

            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            Rect bgRect = bgRt.rect;
            if (bgRect.width < 1f || bgRect.height < 1f)
                Debug.LogWarning($"[DurUI][Equip] Background rect too small for slot={slot}: {bgRect.width:0.#}x{bgRect.height:0.#}");
        }

        private static Sprite GetFallbackWhiteSprite()
        {
            if (fallbackWhiteSprite != null)
                return fallbackWhiteSprite;

            Texture2D tex = Texture2D.whiteTexture;
            fallbackWhiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return fallbackWhiteSprite;
        }

        private void HandleClick()
        {
            ownerWindow?.OnSlotClicked(slot);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (debugHover)
                Debug.Log($"[EquipmentSlotUI] Hover enter slot={slot} itemId='{_equippedItemIdCached}'");

            if (string.IsNullOrWhiteSpace(_equippedItemIdCached))
            {
                ItemHoverBus.PublishClear();
                return;
            }

            ItemHoverBus.PublishHover(_tooltipDataCached);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ItemHoverBus.PublishClear();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (string.IsNullOrWhiteSpace(_equippedItemIdCached))
                return;

            dragDrop?.BeginDragFromEquipment(slot, _equippedItemIdCached, _equippedIconCached);
        }

        public void OnDrag(PointerEventData eventData) { }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragDrop?.CancelDrag();
        }

        public void OnDrop(PointerEventData eventData)
        {
            dragDrop?.CompleteDropOnEquipmentSlot(slot);
        }
    }
}







