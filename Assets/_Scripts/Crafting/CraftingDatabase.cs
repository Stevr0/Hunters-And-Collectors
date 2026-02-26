using System;
using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// CraftingDatabase
    /// ------------------------------------------------------------
    /// Holds all recipes available in the game (MVP).
    /// Later you can filter by skills/unlocks/bench proximity etc.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Crafting/Database", fileName = "CraftingDatabase")]
    public sealed class CraftingDatabase : ScriptableObject
    {
        [SerializeField] private List<CraftingRecipeDef> recipes = new();

        // Runtime dictionary for fast lookup by recipeId.
        private readonly Dictionary<string, CraftingRecipeDef> byId = new(StringComparer.Ordinal);
        private bool initialized;

        private void OnEnable() => initialized = false;
        private void OnValidate() => initialized = false;

        private void EnsureInit()
        {
            if (initialized) return;
            initialized = true;

            byId.Clear();
            foreach (var r in recipes)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.RecipeId))
                    continue;

                // If duplicates exist, last one wins (but we warn).
                if (byId.ContainsKey(r.RecipeId))
                    Debug.LogWarning($"[CraftingDatabase] Duplicate RecipeId '{r.RecipeId}' detected.", r);

                byId[r.RecipeId] = r;
            }
        }

        public IReadOnlyList<CraftingRecipeDef> AllRecipes => recipes;

        public bool TryGet(string recipeId, out CraftingRecipeDef recipe)
        {
            EnsureInit();
            return byId.TryGetValue(recipeId ?? string.Empty, out recipe);
        }
    }
}