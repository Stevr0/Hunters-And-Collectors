using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// ScriptableObject that defines immutable item metadata for a stable ItemId.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Items/Item Def", fileName = "ItemDef")]
    public sealed class ItemDef : ScriptableObject
    {
        /// <summary>Stable item identifier (for example: IT_Wood).</summary>
        public string ItemId = string.Empty;
        /// <summary>Localized display label used in UI.</summary>
        public string DisplayName = string.Empty;
        /// <summary>Icon sprite shown in inventory and vendor windows.</summary>
        public Sprite Icon;
        /// <summary>Maximum quantity that can be stored in one stack.</summary>
        public int MaxStack = 1;
        /// <summary>Category grouping used by gameplay and UI filtering.</summary>
        public ItemCategory Category = ItemCategory.Resource;
    }
}
