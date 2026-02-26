using System;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Where an equippable item can be worn/held.
    /// MVP slots only (easy to extend later).
    /// </summary>
    public enum EquipSlot
    {
        None = 0,

        MainHand = 10,
        OffHand = 11,

        Head = 20,
        Chest = 21,
        Legs = 22,
        Feet = 23,
        Hands = 24,
        Back = 25,
        Ring = 26,
        Necklace = 27,
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
        Sickle,
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