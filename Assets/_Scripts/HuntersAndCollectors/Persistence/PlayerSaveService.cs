using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    /// <summary>
    /// Handles server-side player save load/write operations for schema version 1.
    /// </summary>
    public sealed class PlayerSaveService
    {
        /// <summary>
        /// Saves player runtime state to JSON file path for persistent progression.
        /// </summary>
        public void SavePlayer(PlayerNetworkRoot player)
        {
            // TODO: Implement schemaVersion=1 JSON writing per PERSISTENCE_SPEC.
            Debug.Log($"TODO SavePlayer for {player?.PlayerKey}");
        }

        /// <summary>
        /// Loads player state from JSON path and applies clamped values to network components.
        /// </summary>
        public void LoadPlayer(PlayerNetworkRoot player, ItemDatabase itemDatabase)
        {
            // TODO: Implement schemaVersion=1 JSON load/validation/clamping.
            Debug.Log($"TODO LoadPlayer for {player?.PlayerKey}");
        }
    }
}
