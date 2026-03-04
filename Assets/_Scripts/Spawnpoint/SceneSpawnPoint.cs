using UnityEngine;

namespace HuntersAndCollectors.Bootstrap
{
    /// <summary>
    /// SceneSpawnPoint
    /// -----------------------------------------------------
    /// A simple marker in a gameplay scene that tells the SERVER
    /// where players should be placed after that scene loads.
    ///
    /// IMPORTANT:
    /// - This is NOT networked.
    /// - The server finds it after scene load completes and teleports players.
    /// </summary>
    public sealed class SceneSpawnPoint : MonoBehaviour
    {
        [Tooltip("Spawn id used to select a spawn point (lets you add more later).")]
        [SerializeField] private string spawnId = "Heartstone";

        public string SpawnId => spawnId;
    }
}