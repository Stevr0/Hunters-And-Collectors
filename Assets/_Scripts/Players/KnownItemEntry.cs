using System;
using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Replicated known item row.
    /// </summary>
    public struct KnownItemEntry : INetworkSerializable, IEquatable<KnownItemEntry>
    {
        public FixedString64Bytes ItemId;
        public int BasePrice;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref BasePrice);
        }

        public bool Equals(KnownItemEntry other)
        {
            return ItemId.Equals(other.ItemId) && BasePrice == other.BasePrice;
        }

        public override bool Equals(object obj)
        {
            return obj is KnownItemEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + ItemId.GetHashCode();
                hash = (hash * 31) + BasePrice.GetHashCode();
                return hash;
            }
        }
    }
}