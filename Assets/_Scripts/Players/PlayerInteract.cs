using System;
using HuntersAndCollectors.Graves;
using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Storage;
using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
using HuntersAndCollectors.UI.Storage;
using HuntersAndCollectors.World;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    public sealed class PlayerInteract : NetworkBehaviour
    {
        public enum FocusTargetType
        {
            None,
            ResourceNode,
            Vendor,
            Chest,
            Grave,
            Drop,
            AreaTransfer
        }

        [Header("UI")]
        [SerializeField] private VendorWindowUI vendorUI;
        [SerializeField] private ChestWindowUI chestUI;

        [Header("Camera")]
        [Tooltip("Camera used for center-screen interaction targeting. If null, the script falls back to Camera.main.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Player-side origin used for reach checks. This should stay near the character, not on the camera.")]
        [SerializeField] private Transform viewOrigin;

        [Header("Interact Settings")]
        [Tooltip("Max distance the player can interact from their body/view origin.")]
        [SerializeField] private float interactRange = 2.5f;

        [Tooltip("How far the center-screen aim ray can search for a candidate target before local reach filtering is applied.")]
        [SerializeField] private float aimProbeDistance = 12f;

        [Tooltip("Only objects on these layers are preferred for interaction targeting.")]
        [SerializeField] private LayerMask interactableMask;

        [Header("Harvesting")]
        [SerializeField] private HarvestingNet harvestingNet;

        public ResourceNodeNet CurrentNodeFocus => currentNodeFocus;
        public VendorInteractable CurrentVendorFocus => currentVendorFocus;
        public StorageNet CurrentChestFocus => currentChestFocus;
        public GraveNet CurrentGraveFocus => currentGraveFocus;
        public ResourceDrop CurrentDropFocus => currentDropFocus;
        public AreaTransferInteractable CurrentAreaTransferFocus => currentAreaTransferFocus;
        public FocusTargetType CurrentFocusType => currentFocusType;

        public Camera InteractCamera => playerCamera;
        public float InteractRange => interactRange;
        public LayerMask InteractableMask => interactableMask;

        private ResourceNodeNet currentNodeFocus;
        private VendorInteractable currentVendorFocus;
        private StorageNet currentChestFocus;
        private GraveNet currentGraveFocus;
        private ResourceDrop currentDropFocus;
        private AreaTransferInteractable currentAreaTransferFocus;
        private FocusTargetType currentFocusType;

        private bool harvestHoldActive;
        private double nextPrimarySwingAttemptClientTime;
        private double nextCrossSceneInteractionLogTime;
        private PlayerNetworkRoot playerRoot;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            playerRoot = GetComponent<PlayerNetworkRoot>();

            vendorUI = FindObjectOfType<VendorWindowUI>(true);
            chestUI = FindObjectOfType<ChestWindowUI>(true);

            if (interactableMask.value == 0)
            {
                int layer = LayerMask.NameToLayer("Interactable");
                if (layer >= 0)
                    interactableMask = 1 << layer;
            }

            EnsurePlayerCamera();
            EnsureViewOrigin();

            if (harvestingNet == null)
                harvestingNet = GetComponent<HarvestingNet>();

            if (harvestingNet == null)
                Debug.LogWarning("[PlayerInteract] HarvestingNet not found on player prefab.", this);
        }

        private void OnDisable()
        {
            harvestHoldActive = false;
            nextPrimarySwingAttemptClientTime = 0d;
            ClearCurrentFocus();
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            RefreshFocus();
        }

        public void RefreshFocus()
        {
            ClearCurrentFocus();

            if (!TryGetReachableInteractHit(out RaycastHit hit))
                return;

            currentAreaTransferFocus = ResolveAreaTransferFromHit(hit);
            if (currentAreaTransferFocus != null)
            {
                currentFocusType = FocusTargetType.AreaTransfer;
                return;
            }

            currentVendorFocus = ResolveVendorFromHit(hit);
            if (currentVendorFocus != null)
            {
                currentFocusType = FocusTargetType.Vendor;
                return;
            }

            currentChestFocus = ResolveChestFromHit(hit);
            if (currentChestFocus != null)
            {
                currentFocusType = FocusTargetType.Chest;
                return;
            }

            currentGraveFocus = ResolveGraveFromHit(hit);
            if (currentGraveFocus != null)
            {
                currentFocusType = FocusTargetType.Grave;
                return;
            }

            currentDropFocus = hit.collider != null ? hit.collider.GetComponentInParent<ResourceDrop>() : null;
            if (currentDropFocus != null)
            {
                currentFocusType = FocusTargetType.Drop;
                return;
            }

            currentNodeFocus = hit.collider != null ? hit.collider.GetComponentInParent<ResourceNodeNet>() : null;
            if (currentNodeFocus != null)
                currentFocusType = FocusTargetType.ResourceNode;
        }

        public bool BeginPrimaryInput()
        {
            harvestHoldActive = false;
            nextPrimarySwingAttemptClientTime = 0d;

            RefreshFocus();

            if (harvestingNet != null && currentDropFocus != null)
            {
                harvestingNet.RequestPickup(currentDropFocus);
                return true;
            }

            if (harvestingNet != null && currentNodeFocus != null)
            {
                harvestingNet.RequestHitNode(currentNodeFocus);
                float interval = harvestingNet.GetOwnerExpectedSwingIntervalSeconds(currentNodeFocus);
                nextPrimarySwingAttemptClientTime = Time.timeAsDouble + Mathf.Max(0.01f, interval);
                harvestHoldActive = true;
                return true;
            }

            return false;
        }

        public void TickHeldPrimaryInput()
        {
            if (!harvestHoldActive)
                return;

            double now = Time.timeAsDouble;
            if (now < nextPrimarySwingAttemptClientTime)
                return;

            RefreshFocus();

            if (currentNodeFocus == null || currentVendorFocus != null || currentChestFocus != null || currentGraveFocus != null || currentDropFocus != null || currentAreaTransferFocus != null)
            {
                harvestHoldActive = false;
                nextPrimarySwingAttemptClientTime = 0d;
                return;
            }

            if (harvestingNet == null)
            {
                harvestHoldActive = false;
                nextPrimarySwingAttemptClientTime = 0d;
                return;
            }

            harvestingNet.RequestHitNode(currentNodeFocus);
            float interval = harvestingNet.GetOwnerExpectedSwingIntervalSeconds(currentNodeFocus);
            nextPrimarySwingAttemptClientTime = now + Mathf.Max(0.01f, interval);
        }

        public void EndPrimaryInput()
        {
            harvestHoldActive = false;
            nextPrimarySwingAttemptClientTime = 0d;
        }

        public bool TryInteractPressed()
        {
            RefreshFocus();

            if (currentAreaTransferFocus != null)
                return TryUseAreaTransfer(currentAreaTransferFocus);

            if (currentVendorFocus != null)
            {
                if (vendorUI == null)
                {
                    Debug.LogWarning("[PlayerInteract] No VendorWindowUI found in scene.", this);
                    return false;
                }

                vendorUI.Open(currentVendorFocus);
                return true;
            }

            if (currentChestFocus != null)
            {
                if (chestUI == null)
                {
                    Debug.LogWarning("[PlayerInteract] No ChestWindowUI found in scene.", this);
                    return false;
                }

                chestUI.Open(currentChestFocus);
                return true;
            }

            if (currentGraveFocus != null)
            {
                currentGraveFocus.RequestRecoverAllServerRpc();
                return true;
            }

            if (currentDropFocus != null && harvestingNet != null)
            {
                harvestingNet.RequestPickup(currentDropFocus);
                return true;
            }

            return false;
        }


        private bool TryUseAreaTransfer(AreaTransferInteractable areaTransfer)
        {
            if (areaTransfer == null)
                return false;

            AreaTransferDef transferDef = areaTransfer.TransferDef;
            if (transferDef == null)
            {
                Debug.LogWarning($"[PlayerInteract] Area transfer focus '{areaTransfer.name}' is missing AreaTransferDef.", areaTransfer);
                return false;
            }

            if (string.IsNullOrWhiteSpace(transferDef.TransferId))
            {
                Debug.LogWarning($"[PlayerInteract] Area transfer focus '{areaTransfer.name}' is missing TransferId.", areaTransfer);
                return false;
            }

            string sourceSceneName = areaTransfer.gameObject.scene.IsValid()
                ? areaTransfer.gameObject.scene.name
                : string.Empty;

            Debug.Log($"[PlayerInteract] Interact detected on AreaTransfer '{transferDef.TransferId}' in scene '{sourceSceneName}'.", areaTransfer);
            RequestAreaTransferUseServerRpc(transferDef.TransferId.Trim(), sourceSceneName);
            return true;
        }

        [ServerRpc]
        private void RequestAreaTransferUseServerRpc(string transferId, string sourceSceneName, ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                Debug.LogWarning(
                    $"[PlayerInteract] Rejected area transfer request because senderClientId={rpcParams.Receive.SenderClientId} does not own player clientId={OwnerClientId}.",
                    this);
                return;
            }

            PlayerNetworkRoot playerRoot = GetComponent<PlayerNetworkRoot>();
            if (playerRoot == null)
            {
                Debug.LogWarning("[PlayerInteract] Area transfer request rejected because PlayerNetworkRoot is missing.", this);
                return;
            }

            AreaTransferService.EnsureInstance().ServerRequestTransferById(playerRoot, transferId, sourceSceneName);
        }
        public bool TryGetPromptText(out string promptText)
        {
            promptText = string.Empty;

            switch (currentFocusType)
            {
                case FocusTargetType.AreaTransfer:
                    if (currentAreaTransferFocus == null)
                        return false;
                    promptText = currentAreaTransferFocus.BuildPromptText();
                    return true;

                case FocusTargetType.Vendor:
                    promptText = "E: Open Vendor";
                    return true;

                case FocusTargetType.Chest:
                    promptText = "E: Open Chest";
                    return true;

                case FocusTargetType.Grave:
                    promptText = "E: Recover Grave";
                    return true;

                case FocusTargetType.Drop:
                    promptText = "E: Pick Up";
                    return true;

                case FocusTargetType.ResourceNode:
                    if (currentNodeFocus == null)
                        return false;

                    promptText = currentNodeFocus.ResourceType switch
                    {
                        ResourceType.Wood => "LMB: Chop Tree",
                        ResourceType.Stone => "LMB: Mine Rock",
                        ResourceType.Fiber => "LMB: Gather",
                        _ => "LMB: Harvest"
                    };
                    return true;

                default:
                    return false;
            }
        }

        private void EnsurePlayerCamera()
        {
            if (playerCamera != null)
                return;

            playerCamera = Camera.main;
            if (playerCamera != null)
                Debug.Log("[PlayerInteract] Resolved Camera.main at runtime.", this);
        }

        private void EnsureViewOrigin()
        {
            if (viewOrigin != null)
                return;

            viewOrigin = transform.Find("ViewOrigin");
            if (viewOrigin == null)
                viewOrigin = transform;
        }

        private bool TryGetReachableInteractHit(out RaycastHit bestHit)
        {
            bestHit = default;

            if (!TryBuildCenterScreenRay(out Ray aimRay))
                return false;

            float probeDistance = Mathf.Max(interactRange, aimProbeDistance);
            RaycastHit[] maskedHits = Physics.RaycastAll(
                aimRay,
                probeDistance,
                interactableMask.value != 0 ? interactableMask : Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            if (TryPickClosestUsableHit(maskedHits, out bestHit))
                return true;

            RaycastHit[] fallbackHits = Physics.RaycastAll(
                aimRay,
                probeDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);

            return TryPickClosestUsableHit(fallbackHits, out bestHit);
        }

        private bool TryBuildCenterScreenRay(out Ray aimRay)
        {
            EnsurePlayerCamera();
            aimRay = default;

            if (playerCamera == null)
                return false;

            aimRay = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }

        private bool TryPickClosestUsableHit(RaycastHit[] hits, out RaycastHit bestHit)
        {
            bestHit = default;

            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            Vector3 interactionOrigin = ResolveInteractionOrigin();

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsSupportedFocusHit(hit))
                    continue;

                if (!IsHitInOwnerActiveScene(hit))
                    continue;

                if (!IsHitWithinReach(hit, interactionOrigin))
                    continue;

                bestHit = hit;
                return true;
            }

            return false;
        }

        private bool IsSupportedFocusHit(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;

            if (ResolveAreaTransferFromHit(hit) != null)
                return true;

            if (ResolveVendorFromHit(hit) != null)
                return true;

            if (ResolveChestFromHit(hit) != null)
                return true;

            if (ResolveGraveFromHit(hit) != null)
                return true;

            if (hit.collider.GetComponentInParent<ResourceDrop>() != null)
                return true;

            return hit.collider.GetComponentInParent<ResourceNodeNet>() != null;
        }

        private bool IsHitWithinReach(RaycastHit hit, Vector3 interactionOrigin)
        {
            if (hit.collider == null)
                return false;

            Vector3 closestPoint = hit.collider.ClosestPoint(interactionOrigin);
            float distance = Vector3.Distance(interactionOrigin, closestPoint);
            return distance <= Mathf.Max(0.01f, interactRange);
        }


        private bool IsHitInOwnerActiveScene(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;

            string targetSceneName = hit.collider.gameObject.scene.name;
            string activeSceneName = ResolveOwnerActiveSceneName();

            if (string.IsNullOrWhiteSpace(activeSceneName) || string.Equals(targetSceneName, activeSceneName, StringComparison.Ordinal))
                return true;

            if (Time.unscaledTimeAsDouble >= nextCrossSceneInteractionLogTime)
            {
                nextCrossSceneInteractionLogTime = Time.unscaledTimeAsDouble + 1d;
                Debug.Log($"[PlayerInteract] Interaction denied because target scene '{targetSceneName}' does not match active scene '{activeSceneName}'.", this);
            }

            return false;
        }

        private string ResolveOwnerActiveSceneName()
        {
            if (playerRoot == null)
                playerRoot = GetComponent<PlayerNetworkRoot>();

            if (playerRoot != null && !string.IsNullOrWhiteSpace(playerRoot.CurrentWorldSceneName))
                return playerRoot.CurrentWorldSceneName;

            return gameObject.scene.IsValid() ? gameObject.scene.name : string.Empty;
        }
        private Vector3 ResolveInteractionOrigin()
        {
            EnsureViewOrigin();
            return viewOrigin != null ? viewOrigin.position : transform.position;
        }

        private void ClearCurrentFocus()
        {
            currentNodeFocus = null;
            currentVendorFocus = null;
            currentChestFocus = null;
            currentGraveFocus = null;
            currentDropFocus = null;
            currentAreaTransferFocus = null;
            currentFocusType = FocusTargetType.None;
        }

        public static VendorInteractable ResolveVendorFromHit(RaycastHit hit)
        {
            return ResolveComponentFromHitHierarchy<VendorInteractable>(hit);
        }

        private static StorageNet ResolveChestFromHit(RaycastHit hit)
        {
            return ResolveComponentFromHitHierarchy<StorageNet>(hit);
        }

        private static GraveNet ResolveGraveFromHit(RaycastHit hit)
        {
            return ResolveComponentFromHitHierarchy<GraveNet>(hit);
        }

        private static AreaTransferInteractable ResolveAreaTransferFromHit(RaycastHit hit)
        {
            return ResolveComponentFromHitHierarchy<AreaTransferInteractable>(hit);
        }

        private static T ResolveComponentFromHitHierarchy<T>(RaycastHit hit) where T : Component
        {
            if (hit.collider == null)
                return null;

            T component = hit.collider.GetComponent<T>();
            if (component != null)
                return component;

            component = hit.collider.GetComponentInParent<T>();
            if (component != null)
                return component;

            component = hit.collider.GetComponentInChildren<T>();
            if (component != null)
                return component;

            NetworkObject networkRoot = hit.collider.GetComponentInParent<NetworkObject>();
            if (networkRoot != null)
            {
                component = networkRoot.GetComponentInChildren<T>(true);
                if (component != null)
                    return component;
            }

            Transform root = hit.collider.transform.root;
            return root != null ? root.GetComponentInChildren<T>(true) : null;
        }
    }
}




