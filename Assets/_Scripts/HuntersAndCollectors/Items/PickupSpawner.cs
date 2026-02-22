using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Server-only spawner for world pickups.
    /// Spawns on network start.
    /// </summary>
    public class PickupSpawner : NetworkBehaviour
    {
        [SerializeField] private NetworkObject pickupPrefab;
        [SerializeField] private Vector3 spawnPosition;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            var instance = Instantiate(pickupPrefab, spawnPosition, Quaternion.identity);
            instance.Spawn();
        }
    }
}
