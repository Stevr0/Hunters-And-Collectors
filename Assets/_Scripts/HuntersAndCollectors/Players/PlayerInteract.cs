using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Players
{
    public sealed class PlayerInteract : NetworkBehaviour
    {
        private PlayerInputActions input;

        [Header("UI")]
        [SerializeField] private VendorWindowUI vendorUI;

        [Header("Interact Settings")]
        [Tooltip("How far the player can interact with vendors.")]
        [SerializeField] private float interactRange = 2.5f;

        [Tooltip("Only objects on these layers can be interacted with.")]
        [SerializeField] private LayerMask interactableMask;

        // Reuse an array to avoid allocations each interact press (MVP performance win).
        private readonly Collider[] hitBuffer = new Collider[16];

        public override void OnNetworkSpawn()
        {
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
                var layer = LayerMask.NameToLayer("Interactable");
                if (layer >= 0)
                    interactableMask = 1 << layer;
            }

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

            // Grab our position (center of player). If you have a dedicated "interaction point",
            // you can use that transform instead.
            var origin = transform.position;

            // Find nearby colliders on the Interactable layer
            int hitCount = Physics.OverlapSphereNonAlloc(
                origin,
                interactRange,
                hitBuffer,
                interactableMask,
                QueryTriggerInteraction.Collide
            );

            if (hitCount <= 0)
            {
                Debug.Log("[PlayerInteract] No vendor in range.");
                return;
            }

            VendorInteractable bestVendor = null;
            float bestDistSqr = float.MaxValue;

            // Pick the nearest VendorInteractable found
            for (int i = 0; i < hitCount; i++)
            {
                var col = hitBuffer[i];
                if (col == null) continue;

                // VendorInteractable might be on parent, so search upward.
                var vendor = col.GetComponentInParent<VendorInteractable>();
                if (vendor == null) continue;

                float dSqr = (vendor.transform.position - origin).sqrMagnitude;
                if (dSqr < bestDistSqr)
                {
                    bestDistSqr = dSqr;
                    bestVendor = vendor;
                }
            }

            if (bestVendor == null)
            {
                Debug.Log("[PlayerInteract] No vendor in range (hit colliders, but none were VendorInteractable).");
                return;
            }

            // Open the UI bound to THIS vendor
            vendorUI.Open(bestVendor);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, interactRange);
        }
#endif
    }
}
