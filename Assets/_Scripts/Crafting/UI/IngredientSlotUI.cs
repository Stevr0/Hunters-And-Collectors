using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// IngredientSlotUI
    /// ------------------------------------------------------------
    /// One cell in the ingredients grid (Icon + Name + Required).
    /// This is a purely visual component. No networking, no authority.
    /// </summary>
    public sealed class IngredientSlotUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text requiredText;

        /// <summary>
        /// Fill this slot with ingredient data.
        /// </summary>
        public void Set(string displayName, Sprite icon, int required)
        {
            if (nameText != null)
                nameText.text = displayName;

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            if (requiredText != null)
                requiredText.text = required.ToString();

            // Show used slots
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Clears the slot for recipes with fewer ingredients than grid capacity.
        /// </summary>
        public void Clear(bool hideWhenEmpty = true)
        {
            if (nameText != null)
                nameText.text = string.Empty;

            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }

            if (requiredText != null)
                requiredText.text = string.Empty;

            gameObject.SetActive(!hideWhenEmpty);
        }
    }
}