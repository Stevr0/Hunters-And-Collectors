using HuntersAndCollectors.Players;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// Shared recipe visibility rules.
    ///
    /// Crafting UI can use this replicated known-item data to hide locked recipes,
    /// while server-side crafting validation remains unchanged and authoritative.
    /// </summary>
    public static class CraftingRecipeUnlockUtility
    {
        /// <summary>
        /// A recipe is unlocked when every required ingredient item is known.
        /// Output item knowledge is intentionally ignored.
        /// </summary>
        public static bool IsUnlocked(CraftingRecipeDef recipe, KnownItemsNet knownItems)
        {
            if (recipe == null)
                return false;

            if (knownItems == null)
                return false;

            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                CraftingRecipeDef.Ingredient ingredient = recipe.Ingredients[i];
                if (ingredient.Item == null || string.IsNullOrWhiteSpace(ingredient.Item.ItemId))
                    continue;

                if (!knownItems.IsKnown(ingredient.Item.ItemId))
                    return false;
            }

            return true;
        }
    }
}
