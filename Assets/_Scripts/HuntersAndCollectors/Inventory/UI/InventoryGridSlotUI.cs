using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// InventoryGridSlotUI
    /// ---------------------------------------------------------
    /// Visual+clickable UI for ONE inventory slot.
    ///
    /// Responsibilities:
    /// - Show icon (or hide if empty)
    /// - Show quantity (hide if 1 or empty)
    /// - Store itemId for click actions (equip/use later)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryGridSlotUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text qtyText;
        [SerializeField] private Button button;

        // Current item in this UI slot (empty string = empty).
        private string itemId = string.Empty;
        private int quantity;

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
        }

        /// <summary>
        /// Called by the window to wire click behavior.
        /// </summary>
        public void BindClick(System.Action<string> onClick)
        {
            onClicked = onClick;
        }

        /// <summary>
        /// Updates visuals for an empty slot.
        /// </summary>
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

            // Optional: disable button if empty so clicks do nothing.
            if (button != null)
                button.interactable = false;
        }

        /// <summary>
        /// Updates visuals for a filled slot.
        /// </summary>
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
                // Most inventory UIs hide "1" to reduce clutter.
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
            // Only fire if there's actually an item.
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            onClicked?.Invoke(itemId);
        }
    }
}
