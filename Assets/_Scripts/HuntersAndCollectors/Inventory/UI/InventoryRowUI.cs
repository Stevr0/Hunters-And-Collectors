using TMPro;
using UnityEngine;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// InventoryRowUI
    /// ---------------------------------------------------------
    /// Displays a single inventory line in a scroll list.
    ///
    /// This is the player-inventory equivalent of VendorRowUI,
    /// but with NO buy buttons (read-only for MVP).
    /// </summary>
    public sealed class InventoryRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text qtyText;

        /// <summary>
        /// Bind one row to an item.
        /// </summary>
        public void Bind(string displayName, int quantity)
        {
            if (nameText) nameText.text = displayName;
            if (qtyText) qtyText.text = quantity.ToString();
        }
    }
}
