using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Vendors.UI
{
    /// <summary>
    /// VendorRowUI
    /// --------------------------------------------------------------------
    /// One visual row in the vendor list.
    /// It doesn't know about networking itself — it only raises button clicks.
    /// VendorWindowUI wires up the click events.
    /// </summary>
    public sealed class VendorRowUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text quantityText;
        [SerializeField] private Button buy1Button;
        [SerializeField] private Button buy5Button;

        /// <summary>Which chest slot index this row represents (important for checkout).</summary>
        public int SlotIndex { get; private set; }

        /// <summary>Current quantity shown (used to disable buttons if 0).</summary>
        public int Quantity { get; private set; }

        /// <summary>
        /// Called by VendorWindowUI to populate this row and attach callbacks.
        /// </summary>
        public void Bind(int slotIndex, string displayName, int quantity, System.Action onBuy1, System.Action onBuy5)
        {
            SlotIndex = slotIndex;
            Quantity = quantity;

            if (itemNameText) itemNameText.text = displayName;
            if (quantityText) quantityText.text = quantity.ToString();

            // Clear old listeners so we don't stack them when UI re-renders.
            if (buy1Button)
            {
                buy1Button.onClick.RemoveAllListeners();
                buy1Button.onClick.AddListener(() => onBuy1?.Invoke());
                buy1Button.interactable = quantity >= 1;
            }

            if (buy5Button)
            {
                buy5Button.onClick.RemoveAllListeners();
                buy5Button.onClick.AddListener(() => onBuy5?.Invoke());
                buy5Button.interactable = quantity >= 1;
            }
        }
    }
}
