using System;
using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// Shared crafted-combat roll helper.
    ///
    /// The server crafting pipeline calls this for instance combat gear so family-appropriate
    /// affixes and resistances are generated in one place using the authored ItemDef settings.
    /// </summary>
    public static class CraftedCombatRollService
    {
        public static void ApplyCraftedModifiers(ItemDef def, float profileScore, float skill01, System.Random rng, ref ItemInstance instance, ref ItemInstanceData data)
        {
            if (def == null || rng == null || !CombatItemCatalog.SupportsCraftedCombatRolls(def))
                return;

            int tier = Mathf.Max(1, def.ItemTier);
            ResolveBudgets(def, tier, out int minAffixes, out int maxAffixes, out float bonusAffixChance, out float resistanceChance);

            int affixCount = minAffixes;
            for (int i = minAffixes; i < maxAffixes; i++)
            {
                float finalChance = Mathf.Clamp01(bonusAffixChance + (skill01 * 0.12f));
                if ((float)rng.NextDouble() <= finalChance)
                    affixCount++;
            }

            var allowedAffixes = CombatItemCatalog.GetAllowedAffixes(def);
            var usedAffixes = new HashSet<ItemAffixId>();
            for (int i = 0; i < affixCount && allowedAffixes.Count > 0; i++)
            {
                ItemAffixId affix = PickUniqueAffix(allowedAffixes, usedAffixes, rng);
                if (affix == ItemAffixId.None)
                    break;

                float magnitude01 = BuildMagnitude(profileScore, skill01, rng, tier, resistanceRoll: false);
                ApplyAffix(affix, magnitude01, tier, ref data);
                StoreAffixId(i, affix, ref data);
                usedAffixes.Add(affix);
            }

            var allowedResists = CombatItemCatalog.GetAllowedResistanceAffixes(def);
            float finalResistanceChance = Mathf.Clamp01(resistanceChance + (skill01 * 0.1f));
            if (allowedResists.Count > 0 && (float)rng.NextDouble() <= finalResistanceChance)
            {
                ResistanceAffixId resistanceAffix = allowedResists[rng.Next(allowedResists.Count)];
                float resistanceMagnitude01 = BuildMagnitude(profileScore, skill01, rng, tier, resistanceRoll: true);
                ApplyResistanceAffix(resistanceAffix, resistanceMagnitude01, tier, ref data);
                data.ResistanceAffix = resistanceAffix;
            }

            // Family identity is also reinforced through a small implicit stat bump on combat gear.
            ApplyImplicitIdentity(def, profileScore, tier, ref data, ref instance);
        }

        private static void ResolveBudgets(ItemDef def, int tier, out int minAffixes, out int maxAffixes, out float bonusAffixChance, out float resistanceChance)
        {
            minAffixes = def.MinAffixCount;
            maxAffixes = def.MaxAffixCount;
            bonusAffixChance = def.BonusAffixChance;
            resistanceChance = def.ResistanceAffixChance;

            if (minAffixes == 0 && maxAffixes == 0)
            {
                switch (tier)
                {
                    case 1:
                        minAffixes = 0;
                        maxAffixes = 1;
                        bonusAffixChance = 0.12f;
                        resistanceChance = 0.08f;
                        break;
                    case 2:
                        minAffixes = 1;
                        maxAffixes = 1;
                        bonusAffixChance = 0.22f;
                        resistanceChance = 0.12f;
                        break;
                    case 3:
                        minAffixes = 1;
                        maxAffixes = 2;
                        bonusAffixChance = 0.3f;
                        resistanceChance = 0.22f;
                        break;
                    case 4:
                        minAffixes = 2;
                        maxAffixes = 2;
                        bonusAffixChance = 0.35f;
                        resistanceChance = 0.35f;
                        break;
                    default:
                        minAffixes = 2;
                        maxAffixes = 3;
                        bonusAffixChance = 0.5f;
                        resistanceChance = 0.5f;
                        break;
                }
            }

            minAffixes = Mathf.Max(0, minAffixes);
            maxAffixes = Mathf.Max(minAffixes, maxAffixes == 0 ? minAffixes : maxAffixes);
            if (bonusAffixChance < 0f)
                bonusAffixChance = 0.25f;
            if (resistanceChance < 0f)
                resistanceChance = 0.15f;
        }

        private static float BuildMagnitude(float profileScore, float skill01, System.Random rng, int tier, bool resistanceRoll)
        {
            float tierBias = Mathf.Clamp01((tier - 1) / 5f);
            float jitter = ((float)rng.NextDouble() * 2f - 1f) * (resistanceRoll ? 0.08f : 0.12f);
            float quality = Mathf.Clamp01(profileScore + jitter + (skill01 * 0.15f) + (tierBias * 0.1f));
            return quality;
        }

        private static ItemAffixId PickUniqueAffix(IReadOnlyList<ItemAffixId> allowed, HashSet<ItemAffixId> used, System.Random rng)
        {
            if (allowed == null || allowed.Count == 0)
                return ItemAffixId.None;

            var available = new List<ItemAffixId>(allowed.Count);
            for (int i = 0; i < allowed.Count; i++)
            {
                ItemAffixId affix = allowed[i];
                if (affix == ItemAffixId.None || used.Contains(affix))
                    continue;

                available.Add(affix);
            }

            if (available.Count == 0)
                return ItemAffixId.None;

            return available[rng.Next(available.Count)];
        }

        private static void StoreAffixId(int index, ItemAffixId affix, ref ItemInstanceData data)
        {
            switch (index)
            {
                case 0: data.AffixA = affix; break;
                case 1: data.AffixB = affix; break;
                case 2: data.AffixC = affix; break;
            }
        }

        private static void ApplyAffix(ItemAffixId affix, float quality01, int tier, ref ItemInstanceData data)
        {
            switch (affix)
            {
                case ItemAffixId.Strong:
                    data.BonusStrength += RollInt(1, 1 + tier, quality01);
                    break;
                case ItemAffixId.Keen:
                    data.BonusDexterity += RollInt(1, 1 + tier, quality01);
                    break;
                case ItemAffixId.Wise:
                    data.BonusIntelligence += RollInt(1, 1 + tier, quality01);
                    break;
                case ItemAffixId.Brutal:
                    data.DamageBonus += RollInt(1, 2 + tier, quality01);
                    break;
                case ItemAffixId.Guarded:
                    data.DefenceBonus += RollInt(1, 1 + tier, quality01);
                    break;
                case ItemAffixId.Swift:
                    data.AttackSpeedBonus += RollFloat(0.02f, 0.02f + (tier * 0.01f), quality01);
                    break;
                case ItemAffixId.Focused:
                    data.CastSpeedBonus += RollFloat(0.03f, 0.03f + (tier * 0.0125f), quality01);
                    break;
                case ItemAffixId.Warding:
                    data.BlockValueBonus += RollInt(1, 1 + tier, quality01);
                    break;
                case ItemAffixId.Venomous:
                    data.StatusPowerBonus += RollInt(1, 1 + tier, quality01);
                    break;
                case ItemAffixId.Trapper:
                    data.TrapPowerBonus += RollInt(1, 1 + tier, quality01);
                    break;
            }
        }

        private static void ApplyResistanceAffix(ResistanceAffixId affix, float quality01, int tier, ref ItemInstanceData data)
        {
            int value = RollInt(2, 4 + (tier * 2), quality01);
            switch (affix)
            {
                case ResistanceAffixId.Ironward:
                    data.PhysicalResist += value;
                    break;
                case ResistanceAffixId.Emberward:
                    data.FireResist += value;
                    break;
                case ResistanceAffixId.Frostward:
                    data.FrostResist += value;
                    break;
                case ResistanceAffixId.Venomward:
                    data.PoisonResist += value;
                    break;
                case ResistanceAffixId.Stormward:
                    data.LightningResist += value;
                    break;
            }
        }

        private static void ApplyImplicitIdentity(ItemDef def, float profileScore, int tier, ref ItemInstanceData data, ref ItemInstance instance)
        {
            int identityStatBonus = RollInt(0, Mathf.Max(1, tier / 2), profileScore);
            switch (def.ItemStatBias)
            {
                case ItemStatBias.Strength:
                    data.BonusStrength += identityStatBonus;
                    break;
                case ItemStatBias.Dexterity:
                    data.BonusDexterity += identityStatBonus;
                    break;
                case ItemStatBias.Intelligence:
                    data.BonusIntelligence += identityStatBonus;
                    break;
                case ItemStatBias.Defensive:
                    data.DefenceBonus += RollInt(0, Mathf.Max(1, tier / 2), profileScore);
                    if (def.ResolveBlockValueMax() > 0)
                        instance.RolledBlockValue = Mathf.Max(instance.RolledBlockValue, RollInt(def.ResolveBlockValueMin(), def.ResolveBlockValueMax(), profileScore));
                    break;
                case ItemStatBias.Utility:
                    if (def.ResolveCastSpeedMax() > 0f)
                        instance.RolledCastSpeed = Mathf.Max(instance.RolledCastSpeed, RollFloat(def.ResolveCastSpeedMin(), def.ResolveCastSpeedMax(), profileScore));
                    break;
            }
        }

        private static int RollInt(int min, int max, float quality01)
        {
            int safeMin = Mathf.Min(min, max);
            int safeMax = Mathf.Max(min, max);
            return Mathf.RoundToInt(Mathf.Lerp(safeMin, safeMax, Mathf.Clamp01(quality01)));
        }

        private static float RollFloat(float min, float max, float quality01)
        {
            float safeMin = Mathf.Min(min, max);
            float safeMax = Mathf.Max(min, max);
            return Mathf.Lerp(safeMin, safeMax, Mathf.Clamp01(quality01));
        }
    }
}
