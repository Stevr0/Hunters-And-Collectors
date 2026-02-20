using System;
using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Skills
{
    /// <summary>
    /// Network-safe skill row (replicated in NetworkList).
    /// Must be unmanaged + IEquatable for NetworkList.
    /// </summary>
    public struct SkillEntry : INetworkSerializable, IEquatable<SkillEntry>
    {
        public FixedString64Bytes Id;
        public int Level;
        public int Xp;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref Xp);
        }

        /// <summary>
        /// Required by NetworkList to efficiently detect changes.
        /// We define equality as "same id + same values".
        /// </summary>
        public bool Equals(SkillEntry other)
        {
            return Id.Equals(other.Id) && Level == other.Level && Xp == other.Xp;
        }

        public override bool Equals(object obj)
        {
            return obj is SkillEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Combine hashes deterministically
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Id.GetHashCode();
                hash = (hash * 31) + Level.GetHashCode();
                hash = (hash * 31) + Xp.GetHashCode();
                return hash;
            }
        }
    }
}
