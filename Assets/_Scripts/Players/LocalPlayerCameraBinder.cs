using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// LocalPlayerCameraBinder
    /// --------------------------------------------------------------------
    /// First-person camera wiring for NGO:
    /// - There should be ONE persistent camera in the game.
    /// - When the local player's NetworkObject spawns, this camera is
    ///   parented to the player's CameraMount.
    /// - Only binds for the owning client.
    ///
    /// Attach this to the persistent runtime Camera (bootstrap camera).
    /// </summary>
    public sealed class LocalPlayerCameraBinder : MonoBehaviour
    {
        [Tooltip("If your player prefab has a child transform named CameraMount, we'll attach to it.")]
        [SerializeField] private string cameraMountName = "CameraMount";

        private void OnEnable()
        {
            // Your event is the best hook because it fires exactly when the *local owner* is ready.
            PlayerNetworkRoot.LocalOwnerSpawned += HandleLocalOwnerSpawned;
        }

        private void OnDisable()
        {
            PlayerNetworkRoot.LocalOwnerSpawned -= HandleLocalOwnerSpawned;
        }

        private void HandleLocalOwnerSpawned(PlayerNetworkRoot localOwner)
        {
            // Find the CameraMount on the spawned local player.
            Transform mount = localOwner.transform.Find(cameraMountName);

            if (mount == null)
            {
                Debug.LogError($"[LocalPlayerCameraBinder] Could not find '{cameraMountName}' under {localOwner.name}. " +
                               $"Create a child transform named '{cameraMountName}' on the player prefab.");
                return;
            }

            // Parent the camera to the mount so it moves with the player.
            transform.SetParent(mount, worldPositionStays: false);

            // Snap local position/rotation to match mount exactly.
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // Ensure this camera is tagged correctly if you rely on Camera.main.
            gameObject.tag = "MainCamera";

            Debug.Log($"[LocalPlayerCameraBinder] Bound persistent camera to local player mount '{cameraMountName}'.");
        }
    }
}