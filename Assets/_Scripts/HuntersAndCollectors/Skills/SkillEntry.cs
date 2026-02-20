using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Skills
{
    public struct SkillEntry : INetworkSerializable
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
    }
}
