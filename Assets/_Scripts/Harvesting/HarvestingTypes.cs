using System;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Resource families supported by harvesting.
    /// </summary>
    public enum ResourceType
    {
        Wood = 0,
        Stone = 1,
        Fiber = 2,
        Meat,
    }

    /// <summary>
    /// Minimal tool taxonomy used for harvest gating.
    /// </summary>
    public enum ToolType
    {
        None = 0,
        Axe = 1,
        Pickaxe = 2,
        Sickle = 3,
        Knife = 4,
    }

    /// <summary>
    /// Serialized rare-drop row stored directly on a ResourceNode.
    /// </summary>
    [Serializable]
    public struct RareDropEntry
    {
        [Tooltip("Stable item id to award (e.g. IT_Resin).")]
        public string ItemId;

        [Min(1)]
        [Tooltip("Quantity to award when this rare roll succeeds.")]
        public int Quantity;

        [Range(0f, 1f)]
        [Tooltip("Chance at skill level 0 (0.10 = 10%).")]
        public float BaseChance;

        [Range(0f, 1f)]
        [Tooltip("Absolute cap used when scaling with skill. Leave 0 to default to 20%.")]
        public float MaxChance;

        /// <summary>Normalized canonical item id.</summary>
        public string CanonicalItemId => ItemId?.Trim() ?? string.Empty;

        /// <summary>Returns a final probability in [0,1] after applying skill scaling.</summary>
        public float EvaluateChance01(int skillLevel)
        {
            var level = Mathf.Clamp(skillLevel, 0, 100);
            var clampedBase = Mathf.Clamp01(BaseChance);
            var cap = MaxChance <= 0f ? 0.2f : Mathf.Clamp01(MaxChance);
            var bonus = clampedBase; // doubles at 100 as per spec example
            var scaled = clampedBase + (level / 100f) * bonus;
            return Mathf.Min(cap, Mathf.Clamp01(scaled));
        }

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(CanonicalItemId) && Quantity > 0 && BaseChance > 0f;
        }
    }
}
