using System;
using System.Collections.Generic;
namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// High-level combat family used to bias crafted instance rolls.
    /// This is intentionally content-facing so designers can author families directly in ItemDef.
    /// </summary>
    public enum CombatItemFamily : byte
    {
        None = 0,
        Sword = 1,
        Axe = 2,
        Mace = 3,
        Bow = 4,
        Dagger = 5,
        Spear = 6,
        Staff = 7,
        Wand = 8,
        Focus = 9,
        Shield = 10,
        Cloak = 11,
        Wraps = 12,
        Charm = 13,
        Relic = 14,
        Catalyst = 15,
        Lantern = 16,
    }

    /// <summary>
    /// Broad attribute identity. The game remains classless; this only biases item generation.
    /// </summary>
    public enum ItemStatBias : byte
    {
        None = 0,
        Strength = 1,
        Dexterity = 2,
        Intelligence = 3,
        Defensive = 4,
        Utility = 5,
    }

    public enum ItemAffixId : byte
    {
        None = 0,
        Strong = 1,
        Keen = 2,
        Wise = 3,
        Brutal = 4,
        Guarded = 5,
        Swift = 6,
        Focused = 7,
        Warding = 8,
        Venomous = 9,
        Trapper = 10,
    }

    public enum ResistanceAffixId : byte
    {
        None = 0,
        Ironward = 1,
        Emberward = 2,
        Frostward = 3,
        Venomward = 4,
        Stormward = 5,
    }

    /// <summary>
    /// Shared metadata helpers for item-family defaults and tooltip labels.
    /// The defaults here let older/newer items opt in without requiring a custom editor.
    /// </summary>
    public static class CombatItemCatalog
    {
        private static readonly ItemAffixId[] StrengthWeaponAffixes =
        {
            ItemAffixId.Strong,
            ItemAffixId.Brutal,
            ItemAffixId.Guarded,
            ItemAffixId.Warding
        };

        private static readonly ItemAffixId[] DexterityWeaponAffixes =
        {
            ItemAffixId.Keen,
            ItemAffixId.Brutal,
            ItemAffixId.Swift,
            ItemAffixId.Venomous
        };

        private static readonly ItemAffixId[] IntelligenceWeaponAffixes =
        {
            ItemAffixId.Wise,
            ItemAffixId.Focused,
            ItemAffixId.Venomous,
            ItemAffixId.Trapper
        };

        private static readonly ItemAffixId[] DefensiveAffixes =
        {
            ItemAffixId.Strong,
            ItemAffixId.Guarded,
            ItemAffixId.Warding,
            ItemAffixId.Swift
        };

        private static readonly ItemAffixId[] UtilityAffixes =
        {
            ItemAffixId.Wise,
            ItemAffixId.Focused,
            ItemAffixId.Warding,
            ItemAffixId.Venomous,
            ItemAffixId.Trapper
        };

        private static readonly ResistanceAffixId[] DefaultResistanceAffixes =
        {
            ResistanceAffixId.Ironward,
            ResistanceAffixId.Emberward,
            ResistanceAffixId.Frostward,
            ResistanceAffixId.Venomward,
            ResistanceAffixId.Stormward
        };

        public static bool SupportsCraftedCombatRolls(ItemDef def)
        {
            if (def == null || !def.IsInstanceItem)
                return false;

            if (!def.IsEquippable)
                return false;

            return def.CombatFamily != CombatItemFamily.None
                || def.ItemStatBias != ItemStatBias.None
                || def.ResolveDamageMax() > 0f
                || def.ResolveDefenceMax() > 0f
                || def.ResolveBlockValueMax() > 0
                || def.ResolveCastSpeedMax() > 0f;
        }

        public static IReadOnlyList<ItemAffixId> GetAllowedAffixes(ItemDef def)
        {
            if (def == null)
                return Array.Empty<ItemAffixId>();

            if (def.AllowedAffixes != null && def.AllowedAffixes.Length > 0)
                return def.AllowedAffixes;

            return def.ItemStatBias switch
            {
                ItemStatBias.Strength => StrengthWeaponAffixes,
                ItemStatBias.Dexterity => DexterityWeaponAffixes,
                ItemStatBias.Intelligence => IntelligenceWeaponAffixes,
                ItemStatBias.Defensive => DefensiveAffixes,
                ItemStatBias.Utility => UtilityAffixes,
                _ => ResolveFamilyDefaultAffixes(def.CombatFamily)
            };
        }

        public static IReadOnlyList<ResistanceAffixId> GetAllowedResistanceAffixes(ItemDef def)
        {
            if (def == null)
                return Array.Empty<ResistanceAffixId>();

            if (def.AllowedResistanceAffixes != null && def.AllowedResistanceAffixes.Length > 0)
                return def.AllowedResistanceAffixes;

            return DefaultResistanceAffixes;
        }

        public static string GetAffixDisplayName(ItemAffixId affixId)
        {
            return affixId switch
            {
                ItemAffixId.Strong => "Strong",
                ItemAffixId.Keen => "Keen",
                ItemAffixId.Wise => "Wise",
                ItemAffixId.Brutal => "Brutal",
                ItemAffixId.Guarded => "Guarded",
                ItemAffixId.Swift => "Swift",
                ItemAffixId.Focused => "Focused",
                ItemAffixId.Warding => "Warding",
                ItemAffixId.Venomous => "Venomous",
                ItemAffixId.Trapper => "Trapper",
                _ => string.Empty
            };
        }

        public static string GetResistanceDisplayName(ResistanceAffixId affixId)
        {
            return affixId switch
            {
                ResistanceAffixId.Ironward => "Ironward",
                ResistanceAffixId.Emberward => "Emberward",
                ResistanceAffixId.Frostward => "Frostward",
                ResistanceAffixId.Venomward => "Venomward",
                ResistanceAffixId.Stormward => "Stormward",
                _ => string.Empty
            };
        }

        private static IReadOnlyList<ItemAffixId> ResolveFamilyDefaultAffixes(CombatItemFamily family)
        {
            return family switch
            {
                CombatItemFamily.Sword or CombatItemFamily.Axe or CombatItemFamily.Mace => StrengthWeaponAffixes,
                CombatItemFamily.Bow or CombatItemFamily.Dagger or CombatItemFamily.Spear => DexterityWeaponAffixes,
                CombatItemFamily.Staff or CombatItemFamily.Wand or CombatItemFamily.Focus or CombatItemFamily.Catalyst => IntelligenceWeaponAffixes,
                CombatItemFamily.Shield or CombatItemFamily.Cloak or CombatItemFamily.Wraps => DefensiveAffixes,
                CombatItemFamily.Charm or CombatItemFamily.Relic or CombatItemFamily.Lantern => UtilityAffixes,
                _ => Array.Empty<ItemAffixId>()
            };
        }
    }
}
