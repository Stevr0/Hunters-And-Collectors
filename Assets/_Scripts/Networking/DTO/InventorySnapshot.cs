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
