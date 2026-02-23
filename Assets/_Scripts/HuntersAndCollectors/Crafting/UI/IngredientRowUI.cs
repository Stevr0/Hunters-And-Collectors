using UnityEngine;
using TMPro;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// IngredientRowUI
    /// ------------------------------------------------------------
    /// Displays one ingredient row like:
    /// "1 Wood [3]" (required + owned)
    /// </summary>
    public sealed class IngredientRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;

        public void Bind(string itemName, int required, int owned)
        {
            if (text == null) return;

            // Match your screenshot feel: "1 Wood [0]"
            text.text = $"{required} {itemName} [{owned}]";
        }
    }
}
