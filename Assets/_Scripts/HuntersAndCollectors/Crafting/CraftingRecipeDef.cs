using System;
using System.Collections.Generic;
using UnityEngine;
using HuntersAndCollectors.Items;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// CraftingRecipeDef
    /// ------------------------------------------------------------
    /// ScriptableObject definition for a recipe.
    ///
    /// Why SO recipes:
    /// - Easy to add more recipes without code changes.
    /// - Safe: uses ItemDef references, not strings.
    /// - UI can render icon/name directly from ItemDef later.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Crafting/Recipe", fileName = "CR_Axe")]
    public sealed class CraftingRecipeDef : ScriptableObject
    {
        [Serializable]
        public struct Ingredient
        {
            [Tooltip("Item required for this ingredient.")]
            public ItemDef Item;

            [Tooltip("Quantity required.")]
            [Min(1)] public int Quantity;
        }

        [Header("Identity")]
        public string RecipeId = "CR_Axe";

        [Header("Category")]
        public CraftingCategory Category = CraftingCategory.Tools;

        [Header("Output")]
        [Tooltip("The crafted item (e.g. Axe ItemDef).")]
        public ItemDef OutputItem;

        [Min(1)]
        public int OutputQuantity = 1;

        [Header("Ingredients")]
        public List<Ingredient> Ingredients = new();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(RecipeId))
                RecipeId = name;

            if (OutputQuantity < 1) OutputQuantity = 1;

            for (int i = 0; i < Ingredients.Count; i++)
            {
                if (Ingredients[i].Quantity < 1)
                {
                    var ing = Ingredients[i];
                    ing.Quantity = 1;
                    Ingredients[i] = ing;
                }
            }
        }
#endif
    }
}
