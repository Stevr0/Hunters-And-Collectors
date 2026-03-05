using Unity.Collections;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Runtime per-item instance data.
    ///
    /// This is NOT template data. It belongs to one concrete item instance and
    /// should follow that item through inventory/equipment/vendor transfers.
    /// </summary>
    public struct ItemInstanceData
    {
        public int BonusStrength;
        public int BonusDexterity;
        public int BonusIntelligence;

        // Maker's mark, set by server crafting pipeline.
        public FixedString64Bytes CraftedBy;

        public bool HasAnyBonus =>
            BonusStrength != 0 || BonusDexterity != 0 || BonusIntelligence != 0;

        public bool HasCrafter => CraftedBy.Length > 0;
    }
}
