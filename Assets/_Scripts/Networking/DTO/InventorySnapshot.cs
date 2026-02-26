using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Networking.DTO
{
    /// <summary>
    /// Server snapshot payload for full inventory grid state replication.
    /// </summary>
    public struct InventorySnapshot : INetworkSerializable
    {
        public struct SlotDto : INetworkSerializable
        {
            public bool IsEmpty;
            public FixedString64Bytes ItemId;
            public int Quantity;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref IsEmpty);
                serializer.SerializeValue(ref ItemId);
                serializer.SerializeValue(ref Quantity);
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
                // Validate count to prevent malformed packets
                var expected = W * H;
                if (count != expected)
                {
                    // Hard clamp to expected size
                    count = expected;
                }

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