using HuntersAndCollectors.Items;
using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
using HuntersAndCollectors.Harvesting;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    public sealed class PlayerInteract : NetworkBehaviour
    {
        private PlayerInputActions input;

        [Header("UI")]
        [SerializeField] private VendorWindowUI vendorUI;

        [Header("Camera")]
        [Tooltip("Camera used for reticle raycasting. If null, will use Camera.main.")]
        [SerializeField] private Camera playerCamera;

        [Header("Interact Settings")]
        [Tooltip("Max distance the player can interact.")]
        [SerializeField] private float interactRange = 2.5f;

        [Tooltip("Only objects on these layers can be interacted with.")]
        [SerializeField] private LayerMask interactableMask;

        [Header("Harvesting")]
        [SerializeField] private HarvestingNet harvestingNet;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            // Find UI even if it's inactive
            vendorUI = FindObjectOfType<VendorWindowUI>(true);

            // Default layer if mask not set
            if (interactableMask.value == 0)
            {
                var layer = LayerMask.NameToLayer("Interactable");
                if (layer >= 0)
                    interactableMask = 1 << layer;
            }

            // Camera for reticle ray
            if (playerCamera == null)
                playerCamera = Camera.main;

            if (playerCamera == null)
                Debug.LogWarning("[PlayerInteract] No camera assigned and Camera.main not found.");

            if (harvestingNet == null)
                harvestingNet = GetComponent<HarvestingNet>();

            if (harvestingNet == null)
                Debug.LogWarning("[PlayerInteract] HarvestingNet not found on player prefab.");

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
            if (playerCamera == null)
                return;

            // Ray straight out of the camera = centre of screen reticle
            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            // Only hit interactable layer objects, within range
            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableMask, QueryTriggerInteraction.Collide))
                return;

            Debug.Log($"[PlayerInteract] Ray hit: {hit.collider.name} (root: {hit.collider.transform.root.name})");

            // ------------------------------------------------------------
            // 1) Vendor interaction (keep your existing behaviour)
            // ------------------------------------------------------------
            var vendor = hit.collider.GetComponentInParent<VendorInteractable>();
            if (vendor != null)
            {
                if (vendorUI == null)
                {
                    Debug.LogWarning("[PlayerInteract] No VendorWindowUI found in scene.");
                    return;
                }

                vendorUI.Open(vendor);
                return;
            }

            // ------------------------------------------------------------
            // 2) World pickup interaction (NEW)
            // ------------------------------------------------------------
            var pickup = hit.collider.GetComponentInParent<ResourceDrop>();
            if (pickup != null)
            {
                if (harvestingNet == null)
                    return;

                harvestingNet.RequestPickup(pickup);
                return;
            }

            // ------------------------------------------------------------
            // 3) Resource node interaction (NEW)
            // ------------------------------------------------------------
            var node = hit.collider.GetComponentInParent<ResourceNodeNet>();
            if (node != null)
            {
                if (harvestingNet == null)
                    return;

                harvestingNet.RequestHarvest(node);
                return;
            }

            // Later: you can add other interactables here (chests, doors, harvest nodes, etc.)
        }


    }
}