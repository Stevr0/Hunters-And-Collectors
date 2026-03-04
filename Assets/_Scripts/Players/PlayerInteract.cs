using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using HuntersAndCollectors.Input;
using UnityEngine.EventSystems;

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

        /// <summary>Node currently in the player's crosshair (updated every frame on owner).</summary>
        public ResourceNodeNet CurrentNodeFocus => currentNodeFocus;

        private ResourceNodeNet currentNodeFocus;

        private VendorInteractable currentVendorFocus;
        private ResourceDrop currentDropFocus;

        public VendorInteractable CurrentVendorFocus => currentVendorFocus;
        public ResourceDrop CurrentDropFocus => currentDropFocus;

        public Camera InteractCamera => playerCamera;
        public float InteractRange => interactRange;
        public LayerMask InteractableMask => interactableMask;



        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            // Find UI even if it's inactive.
            vendorUI = FindObjectOfType<VendorWindowUI>(true);

            // Default layer if mask not set.
            if (interactableMask.value == 0)
            {
                var layer = LayerMask.NameToLayer("Interactable");
                if (layer >= 0)
                    interactableMask = 1 << layer;
            }

            // Camera for reticle ray.
            if (playerCamera == null)
                playerCamera = Camera.main;

            if (playerCamera == null)
                Debug.LogWarning("[PlayerInteract] No camera assigned and Camera.main not found.");

            if (harvestingNet == null)
                harvestingNet = GetComponent<HarvestingNet>();

            if (harvestingNet == null)
                Debug.LogWarning("[PlayerInteract] HarvestingNet not found on player prefab.");

            input = new PlayerInputActions();
            input.Player.Interact.performed += OnInteractPerformed;
            input.Player.Primary.performed += OnPrimaryPerformed;

            input.Enable();
        }

        private void OnDisable()
        {
            if (input != null)
            {
                input.Player.Interact.performed -= OnInteractPerformed;
                input.Player.Primary.performed -= OnPrimaryPerformed;
                input.Disable();
            }
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            UpdateFocusNode();
        }

        // ------------------------------------------------------------
        // Input callbacks
        // ------------------------------------------------------------

        private void OnInteractPerformed(InputAction.CallbackContext ctx)
        {
            TryInteractTap();
        }

        private void OnPrimaryPerformed(InputAction.CallbackContext ctx)
        {
            if (!ctx.performed)
                return;

            HandlePrimaryAction();
        }

        private void HandlePrimaryAction()
        {
            // ------------------------------------------------------------
            // If UI is open, DO NOT process gameplay clicks.
            // This prevents left click from stealing vendor button clicks.
            // ------------------------------------------------------------
            if (InputState.GameplayLocked)
                return;

            // If the mouse is over any UI element, don't treat this click as gameplay.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (playerCamera == null)
                return;

            UpdateFocusNode();

            // IMPORTANT: Your focus priority is Vendor > Drop > Node,
            // but your action priority currently hits nodes FIRST.
            // That mismatch can cause surprises.
            // We'll align action priority with focus priority:
            // Vendor > Drop > Node

            if (currentVendorFocus != null)
            {
                if (vendorUI == null)
                    vendorUI = FindObjectOfType<VendorWindowUI>(true);

                vendorUI?.Open(currentVendorFocus);
                return;
            }

            if (harvestingNet != null && currentDropFocus != null)
            {
                harvestingNet.RequestPickup(currentDropFocus);
                return;
            }

            if (harvestingNet != null && currentNodeFocus != null)
            {
                harvestingNet.RequestHitNode(currentNodeFocus);
                return;
            }
        }

        // ------------------------------------------------------------
        // Tap interaction (vendor / pickup)
        // ------------------------------------------------------------

        private void TryInteractTap()
        {
            if (playerCamera == null)
                return;

            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableMask, QueryTriggerInteraction.Collide))
                return;

            // Vendor interaction (tap to open)
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

            // Drop pickup (tap)
            var pickup = hit.collider.GetComponentInParent<ResourceDrop>();
            if (pickup != null)
            {
                if (harvestingNet == null)
                    return;

                harvestingNet.RequestPickup(pickup);
                return;
            }

            // NOTE: We intentionally do NOT start harvesting on tap anymore.
            // Harvesting is a HOLD-only action driven by started/canceled.
        }

        // ------------------------------------------------------------
        // Focus / raycast helpers
        // ------------------------------------------------------------

        private void UpdateFocusNode()
        {
            currentNodeFocus = null;
            currentVendorFocus = null;
            currentDropFocus = null;

            if (playerCamera == null)
                return;

            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableMask, QueryTriggerInteraction.Collide))
                return;

            // Priority: Vendor > Drop > Node (match your tap behavior if you want)
            currentVendorFocus = hit.collider.GetComponentInParent<VendorInteractable>();
            if (currentVendorFocus != null)
                return;

            currentDropFocus = hit.collider.GetComponentInParent<ResourceDrop>();
            if (currentDropFocus != null)
                return;

            currentNodeFocus = hit.collider.GetComponentInParent<ResourceNodeNet>();
        }


    }
}