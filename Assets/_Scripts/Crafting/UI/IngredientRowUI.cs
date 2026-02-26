using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// IngredientRowUI (Grid Controller)
    /// ------------------------------------------------------------
    /// Displays up to 5 ingredients for the currently selected recipe.
    ///
    /// Design:
    /// - 1 row, 5 fixed cells.
    /// - Each ingredient occupies one cell (Icon + Name + Required).
    /// - Unused cells are cleared/hidden.
    ///
    /// Note:
    /// - We bind using CraftingRecipeDef.Ingredient (your actual type).
    /// </summary>
    public sealed class IngredientRowUI : MonoBehaviour
    {
        [Header("Slots (expected size = 5)")]
        [Tooltip("Assign 5 slot instances in left-to-right order.")]
        [SerializeField] private IngredientSlotUI[] slots = new IngredientSlotUI[5];

        [Header("Behaviour")]
        [Tooltip("If true, unused slots are hidden. If false, they remain visible but empty.")]
        [SerializeField] private bool hideEmptySlots = false;

        /// <summary>
        /// Bind a recipe ingredient list into the 1x5 grid.
        /// </summary>
        public void Bind(IReadOnlyList<HuntersAndCollectors.Crafting.CraftingRecipeDef.Ingredient> ingredients)
        {
            // Always clear all slots first
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    slots[i].Clear(hideEmptySlots);
            }

            if (ingredients == null)
                return;

            // Fill slots in order (MVP: extra ingredients are ignored)
            int count = Mathf.Min(ingredients.Count, slots.Length);

            for (int i = 0; i < count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                    continue;

                var ing = ingredients[i];

                if (ing.Item == null)
                    continue;

                int required = Mathf.Max(1, ing.Quantity);
                slot.Set(ing.Item.DisplayName, ing.Item.Icon, required);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Friendly editor-time validation to catch mistakes early.
            if (slots == null) return;

            if (slots.Length != 5)
                Debug.LogWarning($"[IngredientRowUI] '{name}' slots array should be size 5 (is {slots.Length}).", this);

            // Also warn if any entries are missing
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    Debug.LogWarning($"[IngredientRowUI] '{name}' slot[{i}] is not assigned.", this);
            }
        }
#endif
    }
}