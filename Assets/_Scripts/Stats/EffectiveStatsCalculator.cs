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
            EquipSlot.Helmet,
            EquipSlot.Chest,
            EquipSlot.Legs,
            EquipSlot.Boots,
            EquipSlot.Gloves,
            EquipSlot.Shoulders,
            EquipSlot.Belt
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
            float equipCastSpeed = 0f;
            float equipCritChance = 0f;

            int equipStr = 0;
            int equipDex = 0;
            int equipInt = 0;
            int equipBlock = 0;
            int equipStatusPower = 0;
            int equipTrapPower = 0;
            int physicalResist = 0;
            int fireResist = 0;
            int frostResist = 0;
            int poisonResist = 0;
            int lightningResist = 0;

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

                    ItemInstanceData instanceData = equipment.GetEquippedInstanceData(slot);

                    equipDamage += ResolveDamage(def, instanceData);
                    equipDefence += ResolveDefence(def, instanceData);
                    equipMoveMult *= ResolveMoveSpeed(def, instanceData);
                    equipCastSpeed += ResolveCastSpeed(def, instanceData);
                    equipCritChance += Mathf.Max(0f, instanceData.CritChanceBonus);
                    equipBlock += ResolveBlockValue(def, instanceData);
                    equipStatusPower += Mathf.Max(0, instanceData.StatusPowerBonus);
                    equipTrapPower += Mathf.Max(0, instanceData.TrapPowerBonus);
                    physicalResist += Mathf.Max(0, instanceData.PhysicalResist);
                    fireResist += Mathf.Max(0, instanceData.FireResist);
                    frostResist += Mathf.Max(0, instanceData.FrostResist);
                    poisonResist += Mathf.Max(0, instanceData.PoisonResist);
                    lightningResist += Mathf.Max(0, instanceData.LightningResist);

                    equipStr += Mathf.Max(0, def.Strength) + Mathf.Max(0, instanceData.BonusStrength);
                    equipDex += Mathf.Max(0, def.Dexterity) + Mathf.Max(0, instanceData.BonusDexterity);
                    equipInt += Mathf.Max(0, def.Intelligence) + Mathf.Max(0, instanceData.BonusIntelligence);

                    if (slot == EquipSlot.MainHand)
                    {
                        mainHandDef = def;
                        mainHandSwing = ResolveSwingSpeed(def, instanceData);
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
            result.SwingSpeed = (baseSwingSpeed * mainHandSwing * swingSkillMult) + 0f;
            result.CastSpeed = equipCastSpeed;
            result.CritChance = equipCritChance;
            result.BlockValue = equipBlock;
            result.StatusPower = equipStatusPower;
            result.TrapPower = equipTrapPower;
            result.PhysicalResist = physicalResist;
            result.FireResist = fireResist;
            result.FrostResist = frostResist;
            result.PoisonResist = poisonResist;
            result.LightningResist = lightningResist;

            result.ActiveCombatSkillId = activeCombatSkillId;
            return result;
        }

        private static float ResolveDamage(ItemDef def, ItemInstanceData instanceData)
        {
            float baseValue = instanceData.RolledDamage > 0f ? instanceData.RolledDamage : def.Damage;
            return Mathf.Max(0f, baseValue + instanceData.DamageBonus);
        }

        private static float ResolveDefence(ItemDef def, ItemInstanceData instanceData)
        {
            float baseValue = instanceData.RolledDefence > 0f ? instanceData.RolledDefence : def.Defence;
            return Mathf.Max(0f, baseValue + instanceData.DefenceBonus);
        }

        private static float ResolveSwingSpeed(ItemDef def, ItemInstanceData instanceData)
        {
            float baseValue = instanceData.RolledSwingSpeed > 0f ? instanceData.RolledSwingSpeed : Mathf.Max(0.01f, def.SwingSpeed);
            return Mathf.Max(0.01f, baseValue + instanceData.AttackSpeedBonus);
        }

        private static float ResolveMoveSpeed(ItemDef def, ItemInstanceData instanceData)
        {
            float baseValue = instanceData.RolledMovementSpeed > 0f ? instanceData.RolledMovementSpeed : def.MovementSpeed;
            return baseValue <= 0f ? 1f : baseValue;
        }

        private static float ResolveCastSpeed(ItemDef def, ItemInstanceData instanceData)
        {
            float baseValue = instanceData.RolledCastSpeed > 0f ? instanceData.RolledCastSpeed : def.CastSpeed;
            return Mathf.Max(0f, baseValue + instanceData.CastSpeedBonus);
        }

        private static int ResolveBlockValue(ItemDef def, ItemInstanceData instanceData)
        {
            int baseValue = instanceData.RolledBlockValue > 0 ? instanceData.RolledBlockValue : def.BlockValue;
            return Mathf.Max(0, baseValue + instanceData.BlockValueBonus);
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
