using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using UnityEngine;

namespace HuntersAndCollectors.Stats
{
    /// <summary>
    /// EffectiveStatsCalculator
    /// -----------------------------------------------------------------------------
    /// Single source of truth for effective stat math.
    ///
    /// Design intent:
    /// - Keep all formulas in one shared place to prevent UI/server drift.
    /// - Remain null-safe so callers can use this during spawn timing gaps.
    /// - Avoid LINQ/allocation-heavy patterns in hot paths.
    /// </summary>
    public static class EffectiveStatsCalculator
    {
        private const float DefaultBaseMoveSpeedMult = 1f;
        private const float DefaultBaseDamage = 0f;
        private const float DefaultBaseDefence = 0f;
        private const float DefaultBaseSwingSpeed = 1f;

        private static readonly EquipSlot[] EquippedSlots =
        {
            EquipSlot.MainHand,
            EquipSlot.OffHand,
            EquipSlot.Head,
            EquipSlot.Chest,
            EquipSlot.Legs,
            EquipSlot.Feet
        };

        /// <summary>
        /// Computes effective totals from base + equipment + skills.
        ///
        /// Attribute-to-vitals rules:
        /// - MaxHealth  = Strength * 2
        /// - MaxStamina = Dexterity * 2
        /// - MaxMana    = Intelligence * 2
        /// </summary>
        public static EffectiveStats Compute(PlayerBaseStats baseStats, PlayerEquipmentNet equipment, SkillsNet skills, ItemDatabase itemDatabase)
        {
            // -----------------------------------------------------------------
            // Step 1: Start from base attributes + base combat values.
            // -----------------------------------------------------------------
            int baseStrength = baseStats != null ? Mathf.Max(0, baseStats.BaseStrength) : 0;
            int baseDexterity = baseStats != null ? Mathf.Max(0, baseStats.BaseDexterity) : 0;
            int baseIntelligence = baseStats != null ? Mathf.Max(0, baseStats.BaseIntelligence) : 0;

            float baseMoveSpeedMult = baseStats != null ? Mathf.Max(0.0001f, baseStats.BaseMoveSpeedMult) : DefaultBaseMoveSpeedMult;
            float baseDamage = baseStats != null ? Mathf.Max(0f, baseStats.BaseDamage) : DefaultBaseDamage;
            float baseDefence = baseStats != null ? Mathf.Max(0f, baseStats.BaseDefence) : DefaultBaseDefence;
            float baseSwingSpeed = baseStats != null ? Mathf.Max(0.0001f, baseStats.BaseSwingSpeed) : DefaultBaseSwingSpeed;

            // -----------------------------------------------------------------
            // Step 2: Aggregate equipped item contributions.
            // -----------------------------------------------------------------
            float equipDamage = 0f;
            float equipDefence = 0f;
            float equipMoveMult = 1f;
            float mainHandSwing = 1f;

            int equipStr = 0;
            int equipDex = 0;
            int equipInt = 0;

            ItemDef mainHandDef = null;

            if (equipment != null)
            {
                for (int i = 0; i < EquippedSlots.Length; i++)
                {
                    EquipSlot slot = EquippedSlots[i];
                    string itemId = equipment.GetEquippedItemId(slot);
                    if (string.IsNullOrWhiteSpace(itemId))
                        continue;

                    if (!TryResolveItemDef(itemId, equipment, itemDatabase, out ItemDef def) || def == null)
                        continue;

                    equipDamage += def.Damage;
                    equipDefence += def.Defence;
                    equipMoveMult *= def.MovementSpeed <= 0f ? 1f : def.MovementSpeed;

                    equipStr += Mathf.Max(0, def.Strength);
                    equipDex += Mathf.Max(0, def.Dexterity);
                    equipInt += Mathf.Max(0, def.Intelligence);

                    if (slot == EquipSlot.MainHand)
                    {
                        mainHandDef = def;
                        mainHandSwing = def.SwingSpeed <= 0f ? 1f : def.SwingSpeed;
                    }
                }
            }

            // -----------------------------------------------------------------
            // Step 3: Resolve active combat skill from main-hand tool tag.
            // -----------------------------------------------------------------
            string activeCombatSkillId = string.Empty;
            if (mainHandDef != null)
                activeCombatSkillId = ResolveCombatSkillIdFromToolTags(mainHandDef.ToolTags);

            // -----------------------------------------------------------------
            // Step 4: Apply skill multipliers (MVP tuning constants).
            // -----------------------------------------------------------------
            int runningLevel = 0;
            int weaponLevel = 0;

            if (skills != null)
            {
                runningLevel = Mathf.Clamp(skills.GetLevel(SkillId.Running), 0, 100);

                if (!string.IsNullOrWhiteSpace(activeCombatSkillId))
                    weaponLevel = Mathf.Clamp(skills.GetLevel(activeCombatSkillId), 0, 100);
            }

            float moveSkillMult = 1f + (runningLevel / 100f) * 0.20f;
            float damageSkillMult = 1f + (weaponLevel / 100f) * 0.25f;
            float swingSkillMult = 1f + (weaponLevel / 100f) * 0.15f;

            // -----------------------------------------------------------------
            // Step 5: Combine totals + derive max vitals from attributes.
            // -----------------------------------------------------------------
            EffectiveStats result = new EffectiveStats();

            result.Strength = baseStrength + equipStr;
            result.Dexterity = baseDexterity + equipDex;
            result.Intelligence = baseIntelligence + equipInt;

            result.MaxHealth = result.Strength * 2;
            result.MaxStamina = result.Dexterity * 2;
            result.MaxMana = result.Intelligence * 2;

            result.Damage = (baseDamage + equipDamage) * damageSkillMult;
            result.Defence = baseDefence + equipDefence;
            result.MoveSpeedMult = baseMoveSpeedMult * equipMoveMult * moveSkillMult;
            result.SwingSpeed = baseSwingSpeed * mainHandSwing * swingSkillMult;

            result.ActiveCombatSkillId = activeCombatSkillId;
            return result;
        }

        private static bool TryResolveItemDef(string itemId, PlayerEquipmentNet equipment, ItemDatabase itemDatabase, out ItemDef def)
        {
            def = null;
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out def) && def != null)
                return true;

            if (equipment != null && equipment.TryGetItemDef(itemId, out def) && def != null)
                return true;

            def = null;
            return false;
        }

        private static string ResolveCombatSkillIdFromToolTags(ToolTag[] tags)
        {
            if (HasToolTag(tags, ToolTag.Axe))
                return SkillId.CombatAxe;

            if (HasToolTag(tags, ToolTag.Pickaxe))
                return SkillId.CombatPickaxe;

            if (HasToolTag(tags, ToolTag.Knife))
                return SkillId.CombatKnife;

            if (HasToolTag(tags, ToolTag.Club))
                return SkillId.CombatClub;

            return string.Empty;
        }

        private static bool HasToolTag(ToolTag[] tags, ToolTag wanted)
        {
            if (tags == null || tags.Length == 0)
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == wanted)
                    return true;
            }

            return false;
        }
    }
}
