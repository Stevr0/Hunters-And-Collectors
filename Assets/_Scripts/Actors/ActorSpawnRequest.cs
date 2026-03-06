using System;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Standardized request payload for actor spawn operations.
    ///
    /// This keeps spawn entry points consistent and allows future queue/serialization extensions
    /// without changing every caller signature.
    /// </summary>
    [Serializable]
    public struct ActorSpawnRequest
    {
        public NetworkObject Prefab;
        public ActorDef ActorDef;
        public string SpawnPointId;

        public Vector3 Position;
        public Quaternion Rotation;
        public bool UseExplicitTransform;

        public ulong OwnerClientId;
        public bool HasOwner;
    }
}
