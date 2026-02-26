using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// RecipeListItemUI
    /// ------------------------------------------------------------
    /// Left-panel recipe entry.
    /// Shows: Output Icon + Output Name
    /// </summary>
    public sealed class RecipeListItemUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Button button;

        /// <summary>
        /// Bind this UI element to a recipe and a click callback.
        /// </summary>
        public void Bind(CraftingRecipeDef recipe, Action onClick)
        {
            if (recipe == null)
                return;

            // Label = Output item name (fallback to asset name)
            if (label != null)
                label.text = recipe.OutputItem != null ? recipe.OutputItem.DisplayName : recipe.name;

            // Icon = Output item icon (hide if missing)
            if (iconImage != null)
            {
                var icon = recipe.OutputItem != null ? recipe.OutputItem.Icon : null;
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }

            // Click wiring
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                if (onClick != null)
                    button.onClick.AddListener(() => onClick());
            }
        }
    }
}