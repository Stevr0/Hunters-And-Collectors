using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.Text;
using UnityEditor;
#endif

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
        private void OnValidate()
        {
            initialized = false;
#if UNITY_EDITOR
            ValidateRecipes();
#endif
        }

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

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only safety check that warns if recipe assets exist on disk but
        /// have not been registered in the database yet. This keeps runtime
        /// behavior unchanged while making missing content easier to spot.
        /// </summary>
        private void ValidateRecipes()
        {
            const string recipesFolder = "Assets/_ScriptableObjects/Recipes";

            var registeredAssets = new HashSet<CraftingRecipeDef>();
            var duplicateRefs = new List<string>();
            foreach (var recipe in recipes)
            {
                if (recipe == null)
                    continue;

                if (!registeredAssets.Add(recipe))
                    duplicateRefs.Add(recipe.name);
            }

            if (duplicateRefs.Count > 0)
            {
                Debug.LogWarning(
                    $"[CraftingDatabase] Duplicate recipe references found in '{name}': {string.Join(", ", duplicateRefs)}",
                    this);
            }

            var missingRecipes = new List<string>();
            var recipeGuids = AssetDatabase.FindAssets("t:CraftingRecipeDef", new[] { recipesFolder });
            foreach (var guid in recipeGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var recipe = AssetDatabase.LoadAssetAtPath<CraftingRecipeDef>(path);
                if (recipe == null || registeredAssets.Contains(recipe))
                    continue;

                missingRecipes.Add(recipe.name);
            }

            if (missingRecipes.Count == 0)
                return;

            missingRecipes.Sort(StringComparer.Ordinal);
            var message = new StringBuilder();
            message.AppendLine("[CraftingDatabase] Recipe assets exist on disk but are missing from the database:");
            foreach (var recipeName in missingRecipes)
                message.AppendLine($" - {recipeName}");

            Debug.LogWarning(message.ToString(), this);
        }
#endif
    }
}
