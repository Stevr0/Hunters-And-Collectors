using System;
using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Storage;
using HuntersAndCollectors.UI.Storage;
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
        [SerializeField] private ChestWindowUI chestUI;

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
        private ChestContainerNet currentChestFocus;
        private ResourceDrop currentDropFocus;

        private bool primaryHeld;
        private double nextPrimarySwingAttemptClientTime;

        public VendorInteractable CurrentVendorFocus => currentVendorFocus;
        public ChestContainerNet CurrentChestFocus => currentChestFocus;
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
            chestUI = FindObjectOfType<ChestWindowUI>(true);

            if (interactableMask.value == 0)
            {
                int layer = LayerMask.NameToLayer("Interactable");
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

            if (currentVendorFocus != null || currentChestFocus != null || currentDropFocus != null || currentNodeFocus == null)
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

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            if (!TryGetBestInteractHit(ray, out RaycastHit hit))
                return;

            VendorInteractable vendor = ResolveVendorFromHit(hit);
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

            ChestContainerNet chest = ResolveChestFromHit(hit);
            if (chest != null)
            {
                if (chestUI == null)
                {
                    Debug.LogWarning("[PlayerInteract] No ChestWindowUI found in scene.");
                    return;
                }

                chestUI.Open(chest);
                return;
            }

            ResourceDrop pickup = hit.collider.GetComponentInParent<ResourceDrop>();
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
            currentChestFocus = null;
            currentDropFocus = null;

            if (playerCamera == null)
                return;

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            if (!TryGetBestInteractHit(ray, out RaycastHit hit))
                return;

            currentVendorFocus = ResolveVendorFromHit(hit);
            if (currentVendorFocus != null)
                return;

            currentChestFocus = ResolveChestFromHit(hit);
            if (currentChestFocus != null)
                return;

            currentDropFocus = hit.collider.GetComponentInParent<ResourceDrop>();
            if (currentDropFocus != null)
                return;

            currentNodeFocus = hit.collider.GetComponentInParent<ResourceNodeNet>();
        }

        /// <summary>
        /// Finds the closest usable interaction hit in range.
        /// This avoids false negatives when the first collider hit is not the actual interactable component.
        /// </summary>
        private bool TryGetBestInteractHit(Ray ray, out RaycastHit bestHit)
        {
            bestHit = default;

            RaycastHit[] maskedHits = Physics.RaycastAll(
                ray,
                interactRange,
                interactableMask,
                QueryTriggerInteraction.Collide);

            if (TryPickClosestUsableHit(maskedHits, out bestHit))
                return true;

            // Fallback for early setup where interactable layers may not yet be configured.
            RaycastHit[] allHits = Physics.RaycastAll(
                ray,
                interactRange,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            return TryPickClosestUsableHit(allHits, out bestHit);
        }

        /// <summary>
        /// Chooses the nearest hit that has any currently supported interaction target.
        /// </summary>
        private bool TryPickClosestUsableHit(RaycastHit[] hits, out RaycastHit bestHit)
        {
            bestHit = default;

            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null)
                    continue;

                if (ResolveVendorFromHit(hit) != null)
                {
                    bestHit = hit;
                    return true;
                }

                if (ResolveChestFromHit(hit) != null)
                {
                    bestHit = hit;
                    return true;
                }

                if (hit.collider.GetComponentInParent<ResourceDrop>() != null)
                {
                    bestHit = hit;
                    return true;
                }

                if (hit.collider.GetComponentInParent<ResourceNodeNet>() != null)
                {
                    bestHit = hit;
                    return true;
                }
            }

            return false;
        }

        private static VendorInteractable ResolveVendorFromHit(RaycastHit hit)
        {
            if (hit.collider == null)
                return null;

            VendorInteractable vendor = hit.collider.GetComponentInParent<VendorInteractable>();
            if (vendor != null)
                return vendor;

            return hit.collider.GetComponentInChildren<VendorInteractable>();
        }

        private static ChestContainerNet ResolveChestFromHit(RaycastHit hit)
        {
            if (hit.collider == null)
                return null;

            ChestContainerNet chest = hit.collider.GetComponentInParent<ChestContainerNet>();
            if (chest != null)
                return chest;

            return hit.collider.GetComponentInChildren<ChestContainerNet>();
        }
    }
}
