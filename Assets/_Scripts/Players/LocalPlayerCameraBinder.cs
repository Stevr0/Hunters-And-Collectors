using HuntersAndCollectors.CameraSystem;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// LocalPlayerCameraBinder
    /// --------------------------------------------------------------------
    /// Hooks the one persistent runtime camera up to the local owning player.
    ///
    /// Why this exists:
    /// - NGO may spawn players later than the bootstrap camera.
    /// - We only want the local owner to control a camera.
    /// - Camera assignment is a presentation concern, not gameplay authority.
    ///
    /// Current behaviour:
    /// - Listen for PlayerNetworkRoot.LocalOwnerSpawned.
    /// - Resolve a good follow target on that player.
    /// - If a ThirdPersonOrbitCamera exists on this same GameObject, assign the target to it.
    /// - If not, fall back to the older first-person style parenting behaviour.
    ///
    /// Attach this to the one persistent runtime camera object.
    /// </summary>
    public sealed class LocalPlayerCameraBinder : MonoBehaviour
    {
        [Tooltip("Preferred child transform used as the camera follow anchor. Example: CameraMount.")]
        [SerializeField] private string cameraMountName = "CameraMount";

        [Tooltip("Optional fallback child transform if the preferred camera mount does not exist.")]
        [SerializeField] private string fallbackMountName = "ViewOrigin";

        private ThirdPersonOrbitCamera orbitCamera;

        private void Awake()
        {
            orbitCamera = GetComponent<ThirdPersonOrbitCamera>();

            // Mark the persistent runtime camera as MainCamera immediately.
            // This avoids a spawn-order race where owner gameplay scripts ask for
            // Camera.main before the local owner bind event fires.
            gameObject.tag = "MainCamera";
        }

        private void OnEnable()
        {
            PlayerNetworkRoot.LocalOwnerSpawned += HandleLocalOwnerSpawned;
        }

        private void OnDisable()
        {
            PlayerNetworkRoot.LocalOwnerSpawned -= HandleLocalOwnerSpawned;
        }

        private void HandleLocalOwnerSpawned(PlayerNetworkRoot localOwner)
        {
            if (localOwner == null)
                return;

            Transform resolvedTarget = ResolveCameraTarget(localOwner.transform);

            // Keep Camera.main usage stable for systems that raycast from the main camera.
            gameObject.tag = "MainCamera";

            if (orbitCamera != null)
            {
                // Third-person path: keep the camera unparented and let the orbit script drive it.
                transform.SetParent(null, worldPositionStays: true);
                orbitCamera.SetFollowTarget(resolvedTarget);

                Debug.Log($"[LocalPlayerCameraBinder] Assigned ThirdPersonOrbitCamera target '{resolvedTarget.name}' for local owner '{localOwner.name}'.");
                return;
            }

            // Legacy fallback path: preserve the old behaviour if the orbit component is not present.
            transform.SetParent(resolvedTarget, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            Debug.Log($"[LocalPlayerCameraBinder] Orbit camera not found. Falling back to parenting camera to '{resolvedTarget.name}'.");
        }

        private Transform ResolveCameraTarget(Transform playerRoot)
        {
            if (playerRoot == null)
                return transform;

            if (!string.IsNullOrWhiteSpace(cameraMountName))
            {
                Transform preferred = playerRoot.Find(cameraMountName);
                if (preferred != null)
                    return preferred;
            }

            if (!string.IsNullOrWhiteSpace(fallbackMountName))
            {
                Transform fallback = playerRoot.Find(fallbackMountName);
                if (fallback != null)
                    return fallback;
            }

            Debug.LogWarning($"[LocalPlayerCameraBinder] Could not find '{cameraMountName}' or '{fallbackMountName}' under {playerRoot.name}. Falling back to player root.");
            return playerRoot;
        }
    }
}

