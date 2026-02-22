using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// PlayerInteract (Reticle Raycast Version)
    /// ------------------------------------------------------------
    /// What changed vs your current version:
    /// - Instead of finding the nearest vendor in an OverlapSphere,
    ///   we raycast forward from the player's camera (center reticle aim).
    /// - The player can ONLY interact when:
    ///     1) The reticle is pointing at a vendor (ray hits it)
    ///     2) The vendor is within interactRange
    ///
    /// Why this is good:
    /// - "Aim at target + press interact" feels consistent and precise.
    /// - It scales easily to other interactables later (chests, harvest nodes, doors).
    /// </summary>
    public sealed class PlayerInteract : NetworkBehaviour
    {
        private PlayerInputActions input;

        [Header("UI")]
        [SerializeField] private VendorWindowUI vendorUI;

        [Header("Camera / Ray Origin")]
        [Tooltip("Camera used to raycast from the centre of the screen. If empty, we'll try to use Camera.main.")]
        [SerializeField] private Camera playerCamera;

        [Header("Interact Settings")]
        [Tooltip("Max distance the player can interact with vendors.")]
        [SerializeField] private float interactRange = 2.5f;

        [Tooltip("Only objects on these layers can be interacted with (raycast will ignore everything else).")]
        [SerializeField] private LayerMask interactableMask;

        public override void OnNetworkSpawn()
        {
            // Only the local player should read input and run raycasts.
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            // Find UI even if it's inactive
            vendorUI = FindObjectOfType<VendorWindowUI>(true);

            // If you forget to set the mask in inspector, default to a layer named "Interactable".
            if (interactableMask.value == 0)
            {
                int layer = LayerMask.NameToLayer("Interactable");
                if (layer >= 0)
                    interactableMask = 1 << layer;
            }

            // Camera:
            // In many multiplayer setups, each player has their own camera.
            // If you assign it in the inspector, great.
            // Otherwise we try Camera.main (works if the local camera is tagged MainCamera).
            if (playerCamera == null)
                playerCamera = Camera.main;

            if (playerCamera == null)
                Debug.LogWarning("[PlayerInteract] No camera assigned and Camera.main not found. Assign Player Camera in inspector.");

            // Input setup (your existing pattern)
            input = new PlayerInputActions();
            input.Player.Interact.performed += _ => TryInteract();
            input.Enable();
        }

        private void OnDisable()
        {
            input?.Disable();
        }

        private void TryInteract()
        {
            if (vendorUI == null)
            {
                Debug.LogWarning("[PlayerInteract] No VendorWindowUI found in scene.");
                return;
            }

            if (playerCamera == null)
            {
                Debug.LogWarning("[PlayerInteract] No camera available for raycast.");
                return;
            }

            // Ray from camera forward = exactly where the centre reticle points.
            // This is the key change that makes it a "pointer/reticle" interaction system.
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            // Only hit objects on interactableMask.
            // This prevents you hitting terrain, props, etc.
            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableMask, QueryTriggerInteraction.Collide))
            {
                // Nothing interactable under the reticle within range.
                return;
            }

            // We may hit a child collider, so search upward for VendorInteractable.
            VendorInteractable vendor = hit.collider.GetComponentInParent<VendorInteractable>();
            if (vendor == null)
            {
                // Ray hit something on the interactable layer, but it wasn't a vendor.
                // (Later, you can expand this to other interactable types.)
                return;
            }

            // We already limited the raycast by interactRange,
            // but you can keep this extra check if you want more explicit safety.
            float distance = Vector3.Distance(playerCamera.transform.position, vendor.transform.position);
            if (distance > interactRange)
                return;

            // Open vendor UI bound to THIS vendor.
            vendorUI.Open(vendor);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Visualize interaction distance (sphere around player).
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactRange);

            // Visualize ray direction (only when camera is assigned).
            if (playerCamera != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(playerCamera.transform.position, playerCamera.transform.forward * interactRange);
            }
        }
#endif
    }
}
