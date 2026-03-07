using HuntersAndCollectors.Items;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// BuildPlacementController
    /// --------------------------------------------------------------------
    /// Local-only first-pass build placement UX controller.
    ///
    /// Authority split:
    /// - This script handles local preview + input only.
    /// - Authoritative spawn still happens through BuildingNet (server validated).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BuildingNet))]
    public sealed class BuildPlacementController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BuildingNet buildingNet;
        [SerializeField] private Camera placementCamera;

        [Header("Preview Raycast")]
        [SerializeField] private LayerMask placementSurfaceMask = ~0;
        [Min(1f)]
        [SerializeField] private float maxRayDistance = 512f;

        [Header("Preview Validation")]
        [SerializeField] private LayerMask placementBlockerMask = ~0;
        [Min(0.01f)]
        [SerializeField] private float overlapCheckRadius = 0.25f;

        [Header("Grid / Rotation")]
        [Min(0.1f)]
        [SerializeField] private float gridSnapSize = 2f;
        [Min(1f)]
        [SerializeField] private float rotationStepDegrees = 15f;

        private ItemDef activeItemDef;
        private GameObject activeGhostObject;
        private BuildGhostView activeGhostView;

        private bool isPlacementActive;
        private bool hasPreviewHit;
        private bool isPreviewValid;

        private float currentYaw;
        private Vector3 previewWorldPosition;

        /// <summary>
        /// Raised when placement mode enters/exits so UI can dim/restore safely.
        /// </summary>
        public event System.Action<bool> PlacementModeChanged;

        public bool IsPlacementActive => isPlacementActive;
        public bool IsLocalOwner => buildingNet != null && buildingNet.IsOwner;

        private void Awake()
        {
            if (buildingNet == null)
                buildingNet = GetComponent<BuildingNet>();

            if (placementCamera == null)
                placementCamera = Camera.main;
        }

        private void OnDisable()
        {
            // Safety: if this object disables mid-placement, cleanly exit and restore UI state.
            EndPlacementMode();
        }

        private void Update()
        {
            if (!IsLocalOwner)
                return;

            if (!isPlacementActive)
                return;

            TickRotationInput();
            TickPreviewRaycast();
            TickGhostTransformAndColor();
            TickConfirmCancelInput();
        }

        /// <summary>
        /// Starts local ghost placement mode for a placeable item.
        /// </summary>
        public bool BeginPlacement(ItemDef itemDef)
        {
            if (!IsLocalOwner)
                return false;

            if (itemDef == null)
                return false;

            if (!itemDef.IsPlaceable)
                return false;

            if (itemDef.PlaceablePrefab == null)
                return false;

            if (isPlacementActive)
                EndPlacementMode();

            activeItemDef = itemDef;
            currentYaw = 0f;
            hasPreviewHit = false;
            isPreviewValid = false;

            SpawnGhostForItem(itemDef);

            isPlacementActive = true;
            PlacementModeChanged?.Invoke(true);

            Debug.Log($"[BuildPlacement][CLIENT] Placement mode started for itemId={itemDef.ItemId}", this);
            return true;
        }

        /// <summary>
        /// Cancels active placement mode and restores UI state.
        /// </summary>
        public void CancelPlacement()
        {
            if (!isPlacementActive)
                return;

            Debug.Log("[BuildPlacement][CLIENT] Placement mode cancelled.", this);
            EndPlacementMode();
        }

        private void TickRotationInput()
        {
            if (activeItemDef == null || !activeItemDef.AllowYawRotation)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            float scrollDelta = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollDelta) < 0.01f)
                return;

            float step = scrollDelta > 0f ? rotationStepDegrees : -rotationStepDegrees;
            currentYaw = Mathf.Repeat(currentYaw + step, 360f);
        }

        private void TickPreviewRaycast()
        {
            hasPreviewHit = false;
            isPreviewValid = false;

            Camera cam = placementCamera != null ? placementCamera : Camera.main;
            if (cam == null)
                return;

            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, placementSurfaceMask, QueryTriggerInteraction.Ignore))
                return;

            hasPreviewHit = true;

            // First-pass grid rule: snap X/Z to a simple configurable grid.
            Vector3 snapped = hit.point;
            float safeGrid = Mathf.Max(0.1f, gridSnapSize);
            snapped.x = Mathf.Round(snapped.x / safeGrid) * safeGrid;
            snapped.z = Mathf.Round(snapped.z / safeGrid) * safeGrid;

            Vector3 offset = activeItemDef != null ? activeItemDef.PlacementOffset : Vector3.zero;
            previewWorldPosition = snapped + offset;

            isPreviewValid = !HasBlockingOverlap(previewWorldPosition);
        }

        private void TickGhostTransformAndColor()
        {
            if (activeGhostObject == null)
                return;

            if (hasPreviewHit)
            {
                activeGhostObject.transform.position = previewWorldPosition;
                activeGhostObject.transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
            }

            if (activeGhostView != null)
                activeGhostView.SetPreviewValid(hasPreviewHit && isPreviewValid);
        }

        private void TickConfirmCancelInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (!hasPreviewHit || !isPreviewValid)
            {
                Debug.Log("[BuildPlacement][CLIENT] Placement click ignored: local preview invalid.", this);
                return;
            }

            if (buildingNet == null)
            {
                Debug.LogWarning("[BuildPlacement][CLIENT] Placement request failed: BuildingNet missing.", this);
                EndPlacementMode();
                return;
            }

            string itemId = activeItemDef != null ? activeItemDef.ItemId : string.Empty;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                Debug.LogWarning("[BuildPlacement][CLIENT] Placement request failed: active item id invalid.", this);
                EndPlacementMode();
                return;
            }

            // Client sends request only; server still validates final placement.
            buildingNet.RequestPlaceItem(itemId, previewWorldPosition, currentYaw);
            Debug.Log($"[BuildPlacement][CLIENT] Placement request sent: itemId={itemId} pos={previewWorldPosition} rotY={currentYaw}", this);

            // First pass behavior: exit after one placement request.
            EndPlacementMode();
        }

        private bool HasBlockingOverlap(Vector3 worldPosition)
        {
            Collider[] hits = Physics.OverlapSphere(
                worldPosition,
                Mathf.Max(0.01f, overlapCheckRadius),
                placementBlockerMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i];
                if (hit == null)
                    continue;

                // Ignore player-own hierarchy colliders.
                if (hit.transform.IsChildOf(transform))
                    continue;

                // Ignore ghost object colliders if any remained enabled.
                if (activeGhostObject != null && hit.transform.IsChildOf(activeGhostObject.transform))
                    continue;

                return true;
            }

            return false;
        }

        private void SpawnGhostForItem(ItemDef itemDef)
        {
            DestroyGhostIfExists();

            GameObject ghostSource = itemDef.GhostPrefab != null
                ? itemDef.GhostPrefab
                : (itemDef.PlaceablePrefab != null ? itemDef.PlaceablePrefab.gameObject : null);

            if (ghostSource == null)
                return;

            activeGhostObject = Instantiate(ghostSource, Vector3.zero, Quaternion.identity);
            activeGhostObject.name = $"Ghost_{itemDef.ItemId}";

            // Ghost is local-only. Ensure we never accidentally network-spawn this visual clone.
            NetworkObject networkObject = activeGhostObject.GetComponent<NetworkObject>();
            if (networkObject != null)
                networkObject.enabled = false;

            // Disable colliders so the ghost never blocks its own overlap checks/raycasts.
            Collider[] colliders = activeGhostObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            activeGhostView = activeGhostObject.GetComponent<BuildGhostView>();
            if (activeGhostView == null)
                activeGhostView = activeGhostObject.AddComponent<BuildGhostView>();

            activeGhostView.SetPreviewValid(false);
        }

        private void EndPlacementMode()
        {
            bool wasActive = isPlacementActive;

            isPlacementActive = false;
            activeItemDef = null;
            hasPreviewHit = false;
            isPreviewValid = false;
            currentYaw = 0f;

            DestroyGhostIfExists();

            if (wasActive)
            {
                PlacementModeChanged?.Invoke(false);
                Debug.Log("[BuildPlacement][CLIENT] Placement mode ended. Inventory state restored.", this);
            }
        }

        private void DestroyGhostIfExists()
        {
            if (activeGhostObject != null)
                Destroy(activeGhostObject);

            activeGhostObject = null;
            activeGhostView = null;
        }
    }
}
