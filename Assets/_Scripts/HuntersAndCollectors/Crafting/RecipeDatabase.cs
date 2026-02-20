using System;
using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// ScriptableObject lookup database for recipes by recipe id.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Crafting/Recipe Database", fileName = "RecipeDatabase")]
    public sealed class RecipeDatabase : ScriptableObject
    {
        [SerializeField] private List<RecipeDef> recipes = new();
        private readonly Dictionary<string, RecipeDef> byId = new(StringComparer.Ordinal);
        private bool initialized;

        /// <summary>
        /// Tries to resolve a recipe by id.
        /// </summary>
        public bool TryGet(string recipeId, out RecipeDef def)
        {
            if (!initialized)
            {
                byId.Clear();
                foreach (var recipe in recipes)
                {
                    if (recipe == null || string.IsNullOrWhiteSpace(recipe.RecipeId)) continue;
                    byId[recipe.RecipeId] = recipe;
                }
                initialized = true;
            }
            return byId.TryGetValue(recipeId ?? string.Empty, out def);
        }
    }
}
