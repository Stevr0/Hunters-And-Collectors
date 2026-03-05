using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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

        private bool primaryHeld;
        private double nextPrimarySwingAttemptClientTime;

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

            vendorUI = FindObjectOfType<VendorWindowUI>(true);

            if (interactableMask.value == 0)
            {
                var layer = LayerMask.NameToLayer("Interactable");
                if (layer >= 0)
                    interactableMask = 1 << layer;
            }

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
            input.Player.Primary.started += OnPrimaryStarted;
            input.Player.Primary.canceled += OnPrimaryCanceled;
            input.Enable();
        }

        private void OnDisable()
        {
            primaryHeld = false;
            nextPrimarySwingAttemptClientTime = 0d;

            if (input != null)
            {
                input.Player.Interact.performed -= OnInteractPerformed;
                input.Player.Primary.started -= OnPrimaryStarted;
                input.Player.Primary.canceled -= OnPrimaryCanceled;
                input.Disable();
            }
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            UpdateFocusNode();
            UpdateHeldPrimarySwing();
        }

        private void OnInteractPerformed(InputAction.CallbackContext ctx)
        {
            TryInteractTap();
        }

        private void OnPrimaryStarted(InputAction.CallbackContext ctx)
        {
            if (!ctx.started)
                return;

            primaryHeld = true;

            // Single-click still performs one immediate primary action.
            bool swungAtNode = HandlePrimaryAction();
            if (swungAtNode)
            {
                float interval = harvestingNet != null
                    ? harvestingNet.GetOwnerExpectedSwingIntervalSeconds(currentNodeFocus)
                    : 0.1f;

                nextPrimarySwingAttemptClientTime = Time.timeAsDouble + Mathf.Max(0.01f, interval);
            }
            else
            {
                nextPrimarySwingAttemptClientTime = Time.timeAsDouble;
            }
        }

        private void OnPrimaryCanceled(InputAction.CallbackContext ctx)
        {
            if (!ctx.canceled)
                return;

            primaryHeld = false;
            nextPrimarySwingAttemptClientTime = 0d;
        }

        private void UpdateHeldPrimarySwing()
        {
            if (!primaryHeld)
                return;

            double now = Time.timeAsDouble;
            if (now < nextPrimarySwingAttemptClientTime)
                return;

            if (!CanProcessPrimaryGameplayInput())
            {
                nextPrimarySwingAttemptClientTime = now + 0.05d;
                return;
            }

            if (playerCamera == null || harvestingNet == null)
            {
                nextPrimarySwingAttemptClientTime = now + 0.05d;
                return;
            }

            UpdateFocusNode();

            if (currentVendorFocus != null || currentDropFocus != null || currentNodeFocus == null)
            {
                nextPrimarySwingAttemptClientTime = now + 0.05d;
                return;
            }

            harvestingNet.RequestHitNode(currentNodeFocus);
            float interval = harvestingNet.GetOwnerExpectedSwingIntervalSeconds(currentNodeFocus);
            nextPrimarySwingAttemptClientTime = now + Mathf.Max(0.01f, interval);
        }

        private bool HandlePrimaryAction()
        {
            if (!CanProcessPrimaryGameplayInput())
                return false;

            if (playerCamera == null)
                return false;

            UpdateFocusNode();

            if (currentVendorFocus != null)
            {
                if (vendorUI == null)
                    vendorUI = FindObjectOfType<VendorWindowUI>(true);

                vendorUI?.Open(currentVendorFocus);
                return false;
            }

            if (harvestingNet != null && currentDropFocus != null)
            {
                harvestingNet.RequestPickup(currentDropFocus);
                return false;
            }

            if (harvestingNet != null && currentNodeFocus != null)
            {
                harvestingNet.RequestHitNode(currentNodeFocus);
                return true;
            }

            return false;
        }

        private bool CanProcessPrimaryGameplayInput()
        {
            if (InputState.GameplayLocked)
                return false;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return false;

            return true;
        }

        private void TryInteractTap()
        {
            if (playerCamera == null)
                return;

            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableMask, QueryTriggerInteraction.Collide))
                return;

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

            var pickup = hit.collider.GetComponentInParent<ResourceDrop>();
            if (pickup != null)
            {
                if (harvestingNet == null)
                    return;

                harvestingNet.RequestPickup(pickup);
            }
        }

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

