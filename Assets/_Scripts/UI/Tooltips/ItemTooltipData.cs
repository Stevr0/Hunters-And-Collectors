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
        public int ItemTier;
        public HuntersAndCollectors.Items.CombatItemFamily CombatFamily;
        public HuntersAndCollectors.Items.ItemStatBias ItemStatBias;

        public float Damage;
        public float Defence;
        public int AttackBonus;
        public float SwingSpeed;
        public float MoveSpeed;
        public float CastSpeed;
        public int BlockValue;
        public float CritChance;
        public int StatusPower;
        public int TrapPower;

        // Total attributes (template + instance bonus).
        public int Strength;
        public int Dexterity;
        public int Intelligence;

        // Instance-only bonuses.
        public int BonusStrength;
        public int BonusDexterity;
        public int BonusIntelligence;

        public int Durability;
        public int MaxDurability;
        public string CraftedBy;

        // Instance-roll values (0 when not applicable).
        public long InstanceId;
        public float RolledDamage;
        public float RolledDefence;
        public float RolledSwingSpeed;
        public float RolledMovementSpeed;
        public float RolledCastSpeed;
        public int RolledBlockValue;

        public int DamageBonus;
        public int DefenceBonus;
        public float AttackSpeedBonus;
        public float CastSpeedBonus;
        public float CritChanceBonus;
        public int BlockValueBonus;
        public int StatusPowerBonus;
        public int TrapPowerBonus;

        public int PhysicalResist;
        public int FireResist;
        public int FrostResist;
        public int PoisonResist;
        public int LightningResist;

        public HuntersAndCollectors.Items.ItemAffixId AffixA;
        public HuntersAndCollectors.Items.ItemAffixId AffixB;
        public HuntersAndCollectors.Items.ItemAffixId AffixC;
        public HuntersAndCollectors.Items.ResistanceAffixId ResistanceAffix;
    }
}
