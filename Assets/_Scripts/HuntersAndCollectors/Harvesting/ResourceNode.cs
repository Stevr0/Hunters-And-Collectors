using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Scene resource node with harvest drop and cooldown state.
    ///
    /// MVP rules:
    /// - Cooldown is driven by server time (NGO ServerTime).
    /// - Only the server should call Consume().
    /// - NodeId must be unique in the scene (registry enforces this).
    /// </summary>
    public sealed class ResourceNode : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique id in the scene. Must be unique across all ResourceNodes.")]
        [SerializeField] private string nodeId = "NODE_001";

        [Header("Drop")]
        [SerializeField] private string dropItemId = "it_wood";
        [SerializeField] private int dropQuantity = 1;

        [Header("Respawn")]
        [SerializeField] private float respawnSeconds = 30f;

        // Server-authoritative cooldown timestamp (in server time seconds).
        private float nextHarvestServerTime;

        public string NodeId => nodeId;
        public string DropItemId => dropItemId;
        public int DropQuantity => dropQuantity < 1 ? 1 : dropQuantity;

        /// <summary>
        /// Returns current server time (or best-known server time on clients).
        /// </summary>
        private static float ServerTimeNow()
        {
            // If NetworkManager isn't running (editor scene tests), fall back to local Time.time
            if (NetworkManager.Singleton == null)
                return Time.time;

            return (float)NetworkManager.Singleton.ServerTime.Time;
        }

        /// <summary>
        /// True when node is currently harvestable.
        /// Uses server time to avoid per-client Time.time desync.
        /// </summary>
        public bool IsHarvestable()
        {
            return ServerTimeNow() >= nextHarvestServerTime;
        }

        /// <summary>
        /// Consumes node and starts cooldown.
        /// IMPORTANT: should only be called by the server.
        /// </summary>
        public void Consume()
        {
            // Defensive: if a client accidentally calls this, ignore.
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
                return;

            var cooldown = respawnSeconds < 0f ? 0f : respawnSeconds;
            nextHarvestServerTime = ServerTimeNow() + cooldown;
        }
    }
}
