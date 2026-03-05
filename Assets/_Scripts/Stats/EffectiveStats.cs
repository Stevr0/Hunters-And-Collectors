namespace HuntersAndCollectors.Stats
{
    /// <summary>
    /// EffectiveStats
    /// -----------------------------------------------------------------------------
    /// Final result produced by EffectiveStatsCalculator.
    ///
    /// This data is intentionally plain and shared so both gameplay and UI can read
    /// the exact same computed values.
    /// </summary>
    public struct EffectiveStats
    {
        // Combat/movement totals.
        public float MoveSpeedMult;
        public float Damage;
        public float Defence;
        public float SwingSpeed;

        // Attribute totals (base + equipped item bonuses).
        public int Strength;
        public int Dexterity;
        public int Intelligence;

        // Derived max vitals from attributes.
        public int MaxHealth;
        public int MaxStamina;
        public int MaxMana;

        // Optional debug field for active weapon skill mapping.
        public string ActiveCombatSkillId;
    }
}
