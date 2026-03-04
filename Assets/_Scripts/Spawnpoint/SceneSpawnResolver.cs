using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Responsible for relocating players after a scene load.
/// Server authoritative.
/// </summary>

namespace HuntersAndCollectors.Bootstrap 
{
    public sealed class SceneSpawnResolver : MonoBehaviour
    {
        public static void MovePlayerToSpawn(PlayerNetworkRoot player)
        {
            if (!NetworkManager.Singleton.IsServer)
                return;

            // Find spawn point in active scene
            var spawn = Object.FindObjectOfType<SceneSpawnPoint>();

            if (spawn == null)
            {
                Debug.LogError("No SceneSpawnPoint found in scene!");
                return;
            }

            // Teleport player safely
            var controller = player.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false; // disable before reposition

            player.transform.SetPositionAndRotation(
                spawn.transform.position,
                spawn.transform.rotation
            );

            if (controller != null)
                controller.enabled = true;
        }
    }

}

