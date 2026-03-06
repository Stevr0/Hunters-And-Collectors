using System;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Where an equippable item can be worn/held.
    /// Canonical player slots: Belt, Boots, Chest, Gloves, Helmet, Legs, Shoulders, MainHand, OffHand.
    /// </summary>
    public enum EquipSlot
    {
        None = 0,

        MainHand = 10,
        OffHand = 11,

        Helmet = 20,
        Chest = 21,
        Legs = 22,
        Boots = 23,
        Gloves = 24,
        Shoulders = 25,
        Belt = 26,

        [Obsolete("Use Helmet")]
        Head = Helmet,

        [Obsolete("Use Boots")]
        Feet = Boots,

        [Obsolete("Use Gloves")]
        Hands = Gloves,

        [Obsolete("Use Shoulders")]
        Back = Shoulders,

        [Obsolete("Use Belt")]
        Ring = Belt,

        [Obsolete("Use Belt")]
        Necklace = Belt,
    }

    /// <summary>
    /// How an item occupies hands.
    /// </summary>
    public enum Handedness
    {
        None = 0,
        MainHand = 1,
        OffHand = 2,
        BothHands = 3,
    }

    /// <summary>
    /// Gameplay tags used for "tool checks" (axe equipped, pickaxe equipped, etc.).
    /// Keep this small in MVP and grow it over time.
    /// </summary>
    public enum ToolTag
    {
        None = 0,
        Axe = 1,
        Pickaxe = 2,
        Knife = 3,
        Hammer = 4,
        FishingRod = 5,
        Sickle = 6,
        Club = 8,
    }

    /// <summary>
    /// Optional future-facing stat modifier placeholder.
    /// This is NOT wired to any stats system yet - just metadata on ItemDef.
    /// </summary>
    [Serializable]
    public struct StatModifier
    {
        [Tooltip("Your future stat id (string for flexibility in MVP). Example: 'Strength'")]
        public string StatId;

        [Tooltip("Additive bonus applied while equipped.")]
        public int Add;
    }
}
