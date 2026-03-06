using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using UnityEngine;

namespace HuntersAndCollectors.Stats
{
    /// <summary>
    /// Single source of truth for effective stat math.
    ///
    /// Actor pipeline callers should pass primitive baseline values sourced from ActorDef.
    /// </summary>
    public static class EffectiveStatsCalculator
    {
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
        /// Computes effective totals from baseline + equipment + skills.
        ///
        /// Attribute-to-vitals rules:
        /// - MaxHealth  = Strength * 2
        /// - MaxStamina = Dexterity * 2
        /// - MaxMana    = Intelligence * 2
        /// </summary>
        public static EffectiveStats Compute(
            int baseStrength,
            int baseDexterity,
            int baseIntelligence,
            float baseMoveSpeedMult,
            float baseDamage,
            float baseDefence,
            float baseSwingSpeed,
            PlayerEquipmentNet equipment,
            SkillsNet skills,
            ItemDatabase itemDatabase)
        {
            baseStrength = Mathf.Max(0, baseStrength);
            baseDexterity = Mathf.Max(0, baseDexterity);
            baseIntelligence = Mathf.Max(0, baseIntelligence);
            baseMoveSpeedMult = Mathf.Max(0.0001f, baseMoveSpeedMult);
            baseDamage = Mathf.Max(0f, baseDamage);
            baseDefence = Mathf.Max(0f, baseDefence);
            baseSwingSpeed = Mathf.Max(0.0001f, baseSwingSpeed);

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

                    equipStr += Mathf.Max(0, def.Strength) + Mathf.Max(0, equipment.GetEquippedBonusStrength(slot));
                    equipDex += Mathf.Max(0, def.Dexterity) + Mathf.Max(0, equipment.GetEquippedBonusDexterity(slot));
                    equipInt += Mathf.Max(0, def.Intelligence) + Mathf.Max(0, equipment.GetEquippedBonusIntelligence(slot));

                    if (slot == EquipSlot.MainHand)
                    {
                        mainHandDef = def;
                        mainHandSwing = def.SwingSpeed <= 0f ? 1f : def.SwingSpeed;
                    }
                }
            }

            string activeCombatSkillId = SkillId.CombatUnarmed;
            if (mainHandDef != null)
                activeCombatSkillId = ResolveCombatSkillIdFromToolTags(mainHandDef.ToolTags);

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

            // Items without a combat tool tag still train/scale through unarmed for now.
            return SkillId.CombatUnarmed;
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



