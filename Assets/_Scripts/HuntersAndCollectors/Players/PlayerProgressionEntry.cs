using System;
using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Lightweight replicated row for first-pass unlock-only progression flags.
    /// </summary>
    [Serializable]
    public struct PlayerProgressionEntry : INetworkSerializable, IEquatable<PlayerProgressionEntry>
    {
        public FixedString64Bytes FlagId;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref FlagId);
        }

        public bool Equals(PlayerProgressionEntry other)
        {
            return FlagId.Equals(other.FlagId);
        }
    }
}
