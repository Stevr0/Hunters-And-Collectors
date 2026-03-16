using System;
using HuntersAndCollectors.Items;

namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Runtime item-instance payload for non-stackable gear crafted with server RNG.
    ///
    /// Important:
    /// - This data belongs to one concrete item, not the static ItemDef template.
    /// - The server is the only authority allowed to create or mutate these values.
    /// </summary>
    [Serializable]
    public struct ItemInstance
    {
        // Optional lightweight id to help persistence/debugging correlate one item over time.
        public long InstanceId;

        // Stable template id for this instance.
        public string ItemId;

        // Server-rolled combat/movement values.
        public float RolledDamage;
        public float RolledDefence;
        public float RolledSwingSpeed;
        public float RolledMovementSpeed;
        public float RolledCastSpeed;
        public int RolledBlockValue;

        // Durability is instance state (rolled max + mutable current).
        public int MaxDurability;
        public int CurrentDurability;

        public bool IsValid => !string.IsNullOrWhiteSpace(ItemId);
    }
}
