using HuntersAndCollectors.Inventory;
using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Networking.DTO
{
    /// <summary>
    /// Server snapshot payload for full inventory grid state replication.
    ///
    /// Serialization is manual on purpose so field order stays deterministic across NGO.
    /// </summary>
    public struct InventorySnapshot : INetworkSerializable
    {
        public struct SlotDto : INetworkSerializable
        {
            public bool IsEmpty;
            public InventorySlotContentType ContentType;
            public FixedString64Bytes ItemId;
            public int Quantity;

            // Instance durability (0 for non-instance stacks).
            public int Durability;
            public int MaxDurability;

            // Legacy attribute bonuses.
            public int BonusStrength;
            public int BonusDexterity;
            public int BonusIntelligence;

            // Maker's mark.
            public FixedString64Bytes CraftedBy;

            // New instance-roll payload.
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
            public byte AffixA;
            public byte AffixB;
            public byte AffixC;
            public byte ResistanceAffix;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                // Keep stable serialization order.
                serializer.SerializeValue(ref IsEmpty);

                byte contentTypeByte = (byte)ContentType;
                serializer.SerializeValue(ref contentTypeByte);
                if (serializer.IsReader)
                    ContentType = (InventorySlotContentType)contentTypeByte;

                serializer.SerializeValue(ref ItemId);
                serializer.SerializeValue(ref Quantity);
                serializer.SerializeValue(ref Durability);
                serializer.SerializeValue(ref MaxDurability);
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
                serializer.SerializeValue(ref AffixA);
                serializer.SerializeValue(ref AffixB);
                serializer.SerializeValue(ref AffixC);
                serializer.SerializeValue(ref ResistanceAffix);
            }
        }

        public int W;
        public int H;
        public SlotDto[] Slots;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref W);
            serializer.SerializeValue(ref H);

            var count = Slots == null ? 0 : Slots.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                var expected = W * H;
                if (count != expected)
                    count = expected;

                Slots = new SlotDto[count];
            }

            for (var i = 0; i < count; i++)
            {
                var s = Slots[i];
                s.NetworkSerialize(serializer);
                Slots[i] = s;
            }
        }
    }
}
