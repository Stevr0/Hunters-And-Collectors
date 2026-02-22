using System.Collections.Generic;
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

        // --------------------------------------------------------------------
        // EQUIPMENT (NEW)
        // --------------------------------------------------------------------

        [Header("Equipment (optional)")]
        [Tooltip("If None, the item cannot be equipped.")]
        public EquipSlot EquipSlot = EquipSlot.None;

        [Tooltip("How the item occupies hands (only relevant for MainHand/OffHand items).")]
        public Handedness Handedness = Handedness.None;

        [Tooltip("Tool tags used by skill checks (Axe equipped, Pickaxe equipped, etc.).")]
        public List<ToolTag> ToolTags = new();

        [Tooltip("Optional stat modifiers applied while equipped (not implemented yet).")]
        public List<StatModifier> StatModifiers = new();

        /// <summary>
        /// Convenience: is this item equippable at all?
        /// </summary>
        public bool IsEquippable => EquipSlot != EquipSlot.None;
    }
}
