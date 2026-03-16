using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Runtime per-item instance data mirrored into inventory/equipment transport paths.
    ///
    /// This struct remains the lightweight bridge used by existing equipment APIs,
    /// but now also carries first-pass rolled stat and durability information so the
    /// runtime model supports true crafted item instances.
    /// </summary>
    public struct ItemInstanceData : INetworkSerializable
    {
        // Legacy bonus fields used by current equipment and tooltip flows.
        public int BonusStrength;
        public int BonusDexterity;
        public int BonusIntelligence;

        // Maker's mark, set by server crafting pipeline.
        public FixedString64Bytes CraftedBy;

        // New first-pass item-instance metadata.
        public long InstanceId;
        public float RolledDamage;
        public float RolledDefence;
        public float RolledSwingSpeed;
        public float RolledMovementSpeed;
        public float RolledCastSpeed;
        public int RolledBlockValue;
        public int MaxDurability;
        public int CurrentDurability;

        // First-pass affix payload.
        public int DamageBonus;
        public int DefenceBonus;
        public float AttackSpeedBonus;
        public float CastSpeedBonus;
        public float CritChanceBonus;
        public int BlockValueBonus;
        public int StatusPowerBonus;
        public int TrapPowerBonus;

        // First-pass resistance payload.
        public int PhysicalResist;
        public int FireResist;
        public int FrostResist;
        public int PoisonResist;
        public int LightningResist;

        // Compact affix ids for tooltip display.
        public ItemAffixId AffixA;
        public ItemAffixId AffixB;
        public ItemAffixId AffixC;
        public ResistanceAffixId ResistanceAffix;

        public bool HasAnyBonus =>
            BonusStrength != 0 || BonusDexterity != 0 || BonusIntelligence != 0;

        public bool HasCrafter => CraftedBy.Length > 0;

        public bool HasRolledStats =>
            RolledDamage > 0f || RolledDefence > 0f || RolledSwingSpeed > 0f || RolledMovementSpeed > 0f
            || RolledCastSpeed > 0f || RolledBlockValue > 0;

        public bool HasAffixes =>
            AffixA != ItemAffixId.None || AffixB != ItemAffixId.None || AffixC != ItemAffixId.None || ResistanceAffix != ResistanceAffixId.None;

        public bool HasExtendedBonuses =>
            DamageBonus != 0 || DefenceBonus != 0 || AttackSpeedBonus != 0f || CastSpeedBonus != 0f || CritChanceBonus != 0f
            || BlockValueBonus != 0 || StatusPowerBonus != 0 || TrapPowerBonus != 0
            || PhysicalResist != 0 || FireResist != 0 || FrostResist != 0 || PoisonResist != 0 || LightningResist != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref BonusStrength);
            serializer.SerializeValue(ref BonusDexterity);
            serializer.SerializeValue(ref BonusIntelligence);
            serializer.SerializeValue(ref CraftedBy);

            serializer.SerializeValue(ref InstanceId);
            serializer.SerializeValue(ref RolledDamage);
            serializer.SerializeValue(ref RolledDefence);
            serializer.SerializeValue(ref RolledSwingSpeed);
            serializer.SerializeValue(ref RolledMovementSpeed);
            serializer.SerializeValue(ref RolledCastSpeed);
            serializer.SerializeValue(ref RolledBlockValue);
            serializer.SerializeValue(ref MaxDurability);
            serializer.SerializeValue(ref CurrentDurability);

            serializer.SerializeValue(ref DamageBonus);
            serializer.SerializeValue(ref DefenceBonus);
            serializer.SerializeValue(ref AttackSpeedBonus);
            serializer.SerializeValue(ref CastSpeedBonus);
            serializer.SerializeValue(ref CritChanceBonus);
            serializer.SerializeValue(ref BlockValueBonus);
            serializer.SerializeValue(ref StatusPowerBonus);
            serializer.SerializeValue(ref TrapPowerBonus);
            serializer.SerializeValue(ref PhysicalResist);
            serializer.SerializeValue(ref FireResist);
            serializer.SerializeValue(ref FrostResist);
            serializer.SerializeValue(ref PoisonResist);
            serializer.SerializeValue(ref LightningResist);

            byte affixA = (byte)AffixA;
            byte affixB = (byte)AffixB;
            byte affixC = (byte)AffixC;
            byte resistanceAffix = (byte)ResistanceAffix;
            serializer.SerializeValue(ref affixA);
            serializer.SerializeValue(ref affixB);
            serializer.SerializeValue(ref affixC);
            serializer.SerializeValue(ref resistanceAffix);

            if (serializer.IsReader)
            {
                AffixA = (ItemAffixId)affixA;
                AffixB = (ItemAffixId)affixB;
                AffixC = (ItemAffixId)affixC;
                ResistanceAffix = (ResistanceAffixId)resistanceAffix;
            }
        }
    }
}
