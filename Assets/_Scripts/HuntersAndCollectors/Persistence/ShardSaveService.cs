using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    /// <summary>
    /// Handles server-side shard save load/write operations for world state data.
    /// </summary>
    public sealed class ShardSaveService
    {
        /// <summary>
        /// Writes shard JSON for shelters, vendor chests, and build pieces.
        /// </summary>
        public void SaveShard(string shardKey)
        {
            // TODO: Implement schemaVersion=1 shard save writing.
            Debug.Log($"TODO SaveShard {shardKey}");
        }

        /// <summary>
        /// Loads shard JSON and applies world state.
        /// </summary>
        public void LoadShard(string shardKey)
        {
            // TODO: Implement schemaVersion=1 shard load and apply.
            Debug.Log($"TODO LoadShard {shardKey}");
        }
    }
}
