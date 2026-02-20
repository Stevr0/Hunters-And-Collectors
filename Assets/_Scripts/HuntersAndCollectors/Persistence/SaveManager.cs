using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    /// <summary>
    /// Server-only save coordinator that manages periodic autosave and trigger-based saves.
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        // Editor wiring checklist: place in server scene and assign autosave interval.
        [SerializeField] private float autosaveSeconds = 60f;
        private readonly PlayerSaveService playerSaveService = new();
        private readonly ShardSaveService shardSaveService = new();
        private float nextAutosaveTime;

        private void Update()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            if (Time.time < nextAutosaveTime) return;
            nextAutosaveTime = Time.time + autosaveSeconds;
            SaveAll();
        }

        /// <summary>
        /// Triggers full player and shard saves from the server context.
        /// </summary>
        public void SaveAll()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            shardSaveService.SaveShard("Shard_Default");
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                var root = client.PlayerObject != null ? client.PlayerObject.GetComponent<Players.PlayerNetworkRoot>() : null;
                if (root != null) playerSaveService.SavePlayer(root);
            }
        }
    }
}
