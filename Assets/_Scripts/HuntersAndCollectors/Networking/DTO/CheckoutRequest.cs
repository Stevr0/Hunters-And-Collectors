using Unity.Netcode;

namespace HuntersAndCollectors.Networking.DTO
{
    /// <summary>
    /// Client checkout payload referencing chest slot indices and quantities.
    /// 
    /// Security / validation notes:
    /// - Client must never send price or itemId (server derives from chest contents).
    /// - We clamp line count to prevent malicious large allocations.
    /// - Per-line values are sanitized (no negative slot indices, no non-positive quantities).
    /// </summary>
    public struct CheckoutRequest : INetworkSerializable
    {
        // Cart size safety cap. Adjust if your vendor chest can exceed this.
        private const int MaxLines = 64;

        public struct CheckoutLine : INetworkSerializable
        {
            public int SlotIndex;
            public int Quantity;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref SlotIndex);
                serializer.SerializeValue(ref Quantity);

                // NOTE: We don't mutate here because this method runs in both read and write.
                // Sanitization is done in the outer deserialize block where we know IsReader.
            }
        }

        public CheckoutLine[] Lines;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            var count = Lines == null ? 0 : Lines.Length;
            serializer.SerializeValue(ref count);

            if (serializer.IsReader)
            {
                // Prevent malicious allocations
                if (count < 0) count = 0;
                if (count > MaxLines) count = MaxLines;

                Lines = new CheckoutLine[count];

                for (var i = 0; i < count; i++)
                {
                    var line = new CheckoutLine();
                    line.NetworkSerialize(serializer);

                    // Sanitize minimal invariants
                    if (line.SlotIndex < 0) line.SlotIndex = 0;
                    if (line.Quantity < 1) line.Quantity = 1;

                    Lines[i] = line;
                }

                return;
            }

            // Writer path
            for (var i = 0; i < count; i++)
            {
                var line = Lines[i];
                line.NetworkSerialize(serializer);
            }
        }
    }
}
