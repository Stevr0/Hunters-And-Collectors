namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// UI-only tooltip payload that combines template (ItemDef) and per-instance values.
    ///
    /// This object is read-only from UI perspective; server data should already be
    /// replicated into the source snapshot/equipment state before building it.
    /// </summary>
    public struct ItemTooltipData
    {
        public string ItemId;

        public string DisplayName;
        public string Description;

        public float Damage;
        public float Defence;
        public int AttackBonus;
        public float SwingSpeed;
        public float MoveSpeed;

        // Total attributes (template + instance bonus).
        public int Strength;
        public int Dexterity;
        public int Intelligence;

        // Instance-only bonuses.
        public int BonusStrength;
        public int BonusDexterity;
        public int BonusIntelligence;

        public int Durability;
        public string CraftedBy;
    }
}

