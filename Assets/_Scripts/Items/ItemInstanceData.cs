namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Runtime per-item attribute bonuses.
    ///
    /// This data is intentionally tiny and free of Unity-specific types so it can
    /// safely flow through network DTOs and save/persistence layers.
    /// </summary>
    public struct ItemInstanceData
    {
        public int BonusStrength;
        public int BonusDexterity;
        public int BonusIntelligence;

        public bool HasAnyBonus =>
            BonusStrength != 0 || BonusDexterity != 0 || BonusIntelligence != 0;
    }
}
