using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// RecipeListItemUI
    /// ------------------------------------------------------------
    /// A clickable recipe row in the left list.
    /// </summary>
    public sealed class RecipeListItemUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Button button;

        private CraftingRecipeDef _recipe;

        public void Bind(CraftingRecipeDef recipe, System.Action onClicked)
        {
            _recipe = recipe;

            if (label != null)
            {
                // Show output item name for now (e.g. Axe)
                label.text = recipe.OutputItem != null ? recipe.OutputItem.DisplayName : recipe.name;
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => onClicked?.Invoke());
            }
        }
    }
}
