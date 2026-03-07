using HuntersAndCollectors.Input;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.UI;
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

        [Header("Debug")]
        [SerializeField] private string latestLocalFailReason = string.Empty;

        private ItemDef activeItemDef;
        private GameObject activeGhostObject;
        private BuildGhostView activeGhostView;

        private bool isPlacementActive;
        private bool hasPreviewHit;
        private bool isPreviewValid;

        private float currentYaw;
        private Vector3 previewWorldPosition;

        // Local logging guard so preview logs only when validity/reason changes.
        private bool hasLoggedPreviewState;
        private bool lastLoggedPreviewValid;
        private string lastLoggedPreviewFailReason = string.Empty;

        /// <summary>
        /// Raised when placement mode enters/exits so UI can dim/restore safely.
        /// </summary>
        public event System.Action<bool> PlacementModeChanged;

        public bool IsPlacementActive => isPlacementActive;
        public bool IsLocalOwner => buildingNet != null && buildingNet.IsOwner;

        /// <summary>
        /// Optional inspector/debug visibility for why current preview is invalid.
        /// Empty when preview is valid.
        /// </summary>
        public string LatestLocalFailReason => latestLocalFailReason;

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
            latestLocalFailReason = string.Empty;
            hasLoggedPreviewState = false;
            lastLoggedPreviewFailReason = string.Empty;

            // UX request: while placing, hide character windows and allow gameplay movement/look.
            HideAllCharacterWindows();
            InputState.ForceUnlockGameplay();

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
            latestLocalFailReason = "No placement surface hit";

            Camera cam = placementCamera != null ? placementCamera : Camera.main;
            if (cam == null)
            {
                ReportLocalPreviewStateIfChanged(false, latestLocalFailReason);
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                ReportLocalPreviewStateIfChanged(false, latestLocalFailReason);
                return;
            }

            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, placementSurfaceMask, QueryTriggerInteraction.Ignore))
            {
                ReportLocalPreviewStateIfChanged(false, latestLocalFailReason);
                return;
            }

            hasPreviewHit = true;

            // First-pass grid rule: snap X/Z to a simple configurable grid.
            Vector3 snapped = hit.point;
            float safeGrid = Mathf.Max(0.1f, gridSnapSize);
            snapped.x = Mathf.Round(snapped.x / safeGrid) * safeGrid;
            snapped.z = Mathf.Round(snapped.z / safeGrid) * safeGrid;

            Vector3 offset = activeItemDef != null ? activeItemDef.PlacementOffset : Vector3.zero;
            previewWorldPosition = snapped + offset;

            isPreviewValid = EvaluateLocalPlacementValidity(previewWorldPosition, out string failReason);
            latestLocalFailReason = isPreviewValid ? string.Empty : failReason;

            ReportLocalPreviewStateIfChanged(isPreviewValid, latestLocalFailReason);
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
                Debug.Log($"[BuildPlacement][CLIENT] Placement click ignored: local preview invalid ({latestLocalFailReason}).", this);
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

        /// <summary>
        /// Local preview validation for ghost color guidance.
        /// This is not authoritative; server still re-validates before spawning.
        /// </summary>
        private bool EvaluateLocalPlacementValidity(Vector3 snappedWorldPos, out string failReason)
        {
            failReason = string.Empty;

            if (!hasPreviewHit)
            {
                failReason = "No placement surface hit";
                return false;
            }

            if (activeItemDef == null)
            {
                failReason = "No active placement item";
                return false;
            }

            if (!activeItemDef.IsPlaceable)
            {
                failReason = "Item is not placeable";
                return false;
            }

            if (activeItemDef.PlaceablePrefab == null)
            {
                failReason = "Placeable prefab missing";
                return false;
            }

            if (HasBlockingOverlap(snappedWorldPos))
            {
                failReason = "Blocked by overlap";
                return false;
            }

            if (HeartStoneRegistry.Instance == null)
            {
                failReason = "No HeartStone found";
                return false;
            }

            if (!HeartStoneRegistry.Instance.TryGetMain(out HeartStoneNet mainHeartStone) || mainHeartStone == null)
            {
                failReason = "No HeartStone found";
                return false;
            }

            if (mainHeartStone.IsShardDead)
            {
                failReason = "Shard is dead";
                return false;
            }

            if (mainHeartStone.IsWithinNoBuildRadius(snappedWorldPos))
            {
                failReason = "Inside HeartStone no-build radius";
                return false;
            }

            if (!mainHeartStone.IsWithinBuildRadius(snappedWorldPos))
            {
                failReason = "Outside HeartStone build radius";
                return false;
            }

            return true;
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
            latestLocalFailReason = string.Empty;
            hasLoggedPreviewState = false;
            lastLoggedPreviewFailReason = string.Empty;

            DestroyGhostIfExists();

            if (wasActive)
            {
                PlacementModeChanged?.Invoke(false);
                Debug.Log("[BuildPlacement][CLIENT] Placement mode ended.", this);
            }
        }

        private void DestroyGhostIfExists()
        {
            if (activeGhostObject != null)
                Destroy(activeGhostObject);

            activeGhostObject = null;
            activeGhostView = null;
        }

        private static void HideAllCharacterWindows()
        {
            CharacterWindowRootUI[] roots = FindObjectsOfType<CharacterWindowRootUI>(true);
            for (int i = 0; i < roots.Length; i++)
            {
                CharacterWindowRootUI root = roots[i];
                if (root == null || !root.IsOpen)
                    continue;

                root.Close();
            }
        }

        private void ReportLocalPreviewStateIfChanged(bool previewIsValid, string failReason)
        {
            string safeReason = failReason ?? string.Empty;

            if (hasLoggedPreviewState &&
                previewIsValid == lastLoggedPreviewValid &&
                string.Equals(safeReason, lastLoggedPreviewFailReason, System.StringComparison.Ordinal))
            {
                return;
            }

            hasLoggedPreviewState = true;
            lastLoggedPreviewValid = previewIsValid;
            lastLoggedPreviewFailReason = safeReason;

            if (previewIsValid)
                Debug.Log("[BuildPlacement] Local preview valid.", this);
            else
                Debug.Log($"[BuildPlacement] Local preview invalid: {safeReason}", this);
        }
    }
}
