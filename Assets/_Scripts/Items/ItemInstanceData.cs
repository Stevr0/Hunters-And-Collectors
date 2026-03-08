using Unity.Collections;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Runtime per-item instance data mirrored into inventory/equipment transport paths.
    ///
    /// This struct remains the lightweight bridge used by existing equipment APIs,
    /// but now also carries first-pass rolled stat and durability information so the
    /// runtime model supports true crafted item instances.
    /// </summary>
    public struct ItemInstanceData
    {
        // Legacy bonus fields used by current equipment and tooltip flows.
        public int BonusStrength;
        public int BonusDexterity;
        public int BonusIntelligence;

        // Maker's mark, set by server crafting pipeline.
        public FixedString64Bytes CraftedBy;

        // New first-pass item-instance metadata.
        public long InstanceId;
        public float RolledDamage;
        public float RolledDefence;
        public float RolledSwingSpeed;
        public float RolledMovementSpeed;
        public int MaxDurability;
        public int CurrentDurability;

        public bool HasAnyBonus =>
            BonusStrength != 0 || BonusDexterity != 0 || BonusIntelligence != 0;

        public bool HasCrafter => CraftedBy.Length > 0;

        public bool HasRolledStats =>
            RolledDamage > 0f || RolledDefence > 0f || RolledSwingSpeed > 0f || RolledMovementSpeed > 0f;
    }
}
