using Unity.Netcode;

namespace HuntersAndCollectors.Networking.DTO
{
    /// <summary>
    /// Client checkout payload referencing chest slot indices and quantities.
    /// </summary>
    public struct CheckoutRequest : INetworkSerializable
    {
        public struct CheckoutLine : INetworkSerializable
        {
            public int SlotIndex;
            public int Quantity;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref SlotIndex);
                serializer.SerializeValue(ref Quantity);
            }
        }

        public CheckoutLine[] Lines;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            var count = Lines == null ? 0 : Lines.Length;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader) Lines = new CheckoutLine[count];
            for (var i = 0; i < count; i++)
            {
                var line = Lines[i];
                line.NetworkSerialize(serializer);
                Lines[i] = line;
            }
        }
    }
}
