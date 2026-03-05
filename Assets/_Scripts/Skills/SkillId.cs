namespace HuntersAndCollectors.Skills
{
    /// <summary>
    /// String-based skill identifiers matching persistence schema values.
    /// </summary>
    public static class SkillId
    {
        public const string Sales = "Sales";
        public const string Negotiation = "Negotiation";
        public const string Running = "Running";

        // Harvesting-centric skills
        public const string Woodcutting = "Woodcutting";
        public const string Mining = "Mining";
        public const string Foraging = "Foraging";

        // Crafting-centric skills
        public const string ToolCrafting = "ToolCrafting";
        public const string EquipmentCrafting = "EquipmentCrafting";
        public const string BuildingCrafting = "BuildingCrafting";

        // Combat tool skills (awarded on successful server-confirmed damage only)
        public const string CombatAxe = "Combat_Axe";
        public const string CombatPickaxe = "Combat_Pickaxe";
        public const string CombatKnife = "Combat_Knife";
        public const string CombatClub = "Combat_Club";
    }
}
