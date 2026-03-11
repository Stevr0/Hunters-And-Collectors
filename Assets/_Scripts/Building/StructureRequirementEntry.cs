using System;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// One requirement row in a structure requirement definition.
    /// Each row keeps the required item id and count together so inspector data stays coherent.
    /// </summary>
    [Serializable]
    public struct StructureRequirementEntry
    {
        [Tooltip("Stable SourceItemId from PlacedBuildPiece, for example IT_Floor or IT_Wall.")]
        public string SourceItemId;

        [Min(1)]
        [Tooltip("How many matching placed structures are required within the definition radius.")]
        public int RequiredCount;

        public bool IsValid => !string.IsNullOrWhiteSpace(SourceItemId) && RequiredCount >= 1;

        /// <summary>
        /// Keeps serialized data safe and predictable when edited in the inspector.
        /// </summary>
        public void Sanitize()
        {
            SourceItemId = string.IsNullOrWhiteSpace(SourceItemId) ? string.Empty : SourceItemId.Trim();
            RequiredCount = Mathf.Max(1, RequiredCount);
        }
    }
}
