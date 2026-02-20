using System;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// Defines one crafting recipe with fixed input and output stack lines.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Crafting/Recipe Def", fileName = "RecipeDef")]
    public sealed class RecipeDef : ScriptableObject
    {
        [Serializable]
        public struct StackLine
        {
            public string ItemId;
            public int Quantity;
        }

        public string RecipeId = string.Empty;
        public StackLine[] Ingredients = Array.Empty<StackLine>();
        public StackLine[] Outputs = Array.Empty<StackLine>();
    }
}
