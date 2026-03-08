using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// ShelterState
    /// --------------------------------------------------------------------
    /// First-pass server-authoritative shelter completion tracker.
    ///
    /// Scope of this MVP:
    /// - Uses simple piece counts near the main HeartStone.
    /// - Does not attempt enclosed-room detection or structural rules.
    /// - Exposes a single completion flag for future systems (ex: vendor unlock).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShelterState : MonoBehaviour
    {
        private enum ShelterPieceType
        {
            None = 0,
            Floor = 1,
            Wall = 2,
            Roof = 3,
            Door = 4
        }

        [Header("Identity")]
        [SerializeField] private string shelterId = "SHELTER_MAIN";

        [Header("Shelter Rule")]
        [Min(0f)]
        [SerializeField] private float shelterCheckRadius = 20f;
        [Min(0)]
        [SerializeField] private int requiredFloorCount = 1;
        [Min(0)]
        [SerializeField] private int requiredWallCount = 3;
        [Min(0)]
        [SerializeField] private int requiredRoofCount = 1;

        [Header("Item Id Mapping")]
        [SerializeField] private string floorItemId = "IT_Floor";
        [SerializeField] private string wallItemId = "IT_Wall";
        [SerializeField] private string roofItemId = "IT_Roof";
        [SerializeField] private string doorItemId = "IT_Door";

        [Header("Vendor Unlock")]
        [SerializeField] private GameObject vendorObject;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;
        [SerializeField] private int lastFloorCount;
        [SerializeField] private int lastWallCount;
        [SerializeField] private int lastRoofCount;
        [SerializeField] private int lastDoorCount;
        [SerializeField] private bool isComplete;

        /// <summary>
        /// Public read-only shelter completion state.
        /// This value is authoritative on server.
        /// </summary>
        public bool IsComplete => isComplete;

        private void OnEnable()
        {
            // Re-evaluate when enabled so scene startup can establish initial state.
            ServerReevaluateShelter();
        }

        /// <summary>
        /// SERVER ONLY: Re-evaluates shelter completion from placed pieces near main HeartStone.
        /// </summary>
        public void ServerReevaluateShelter()
        {
            if (!IsServerActive())
                return;

            if (debugLogs)
                Debug.Log("[ShelterState][SERVER] Re-evaluating shelter...", this);

            if (HeartStoneRegistry.Instance == null ||
                !HeartStoneRegistry.Instance.TryGetMain(out HeartStoneNet mainHeartStone) ||
                mainHeartStone == null)
            {
                lastFloorCount = 0;
                lastWallCount = 0;
                lastRoofCount = 0;
                lastDoorCount = 0;

                if (debugLogs)
                    Debug.LogWarning("[ShelterState][SERVER] No HeartStone found. Shelter incomplete.", this);

                SetCompletionState(false);
                return;
            }

            CountShelterPiecesNearHeartStone(mainHeartStone.transform.position);

            if (debugLogs)
            {
                Debug.Log($"[ShelterState][SERVER] Counts: floor={lastFloorCount} wall={lastWallCount} roof={lastRoofCount} door={lastDoorCount}", this);
            }

            bool completed =
                lastFloorCount >= requiredFloorCount &&
                lastWallCount >= requiredWallCount &&
                lastRoofCount >= requiredRoofCount;

            SetCompletionState(completed);
        }

        private void CountShelterPiecesNearHeartStone(Vector3 heartStonePosition)
        {
            lastFloorCount = 0;
            lastWallCount = 0;
            lastRoofCount = 0;
            lastDoorCount = 0;

            PlacedBuildPiece[] pieces = FindObjectsByType<PlacedBuildPiece>(FindObjectsSortMode.None);
            float radius = Mathf.Max(0f, shelterCheckRadius);
            float sqrRadius = radius * radius;

            for (int i = 0; i < pieces.Length; i++)
            {
                PlacedBuildPiece piece = pieces[i];
                if (piece == null)
                    continue;

                if (!IsWithinRadiusXZ(piece.transform.position, heartStonePosition, sqrRadius))
                    continue;

                if (!TryClassifyPiece(piece.SourceItemId, out ShelterPieceType pieceType))
                    continue;

                switch (pieceType)
                {
                    case ShelterPieceType.Floor:
                        lastFloorCount++;
                        break;
                    case ShelterPieceType.Wall:
                        lastWallCount++;
                        break;
                    case ShelterPieceType.Roof:
                        lastRoofCount++;
                        break;
                    case ShelterPieceType.Door:
                        lastDoorCount++;
                        break;
                }
            }
        }

        private bool TryClassifyPiece(string itemId, out ShelterPieceType pieceType)
        {
            pieceType = ShelterPieceType.None;

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            if (string.Equals(itemId, floorItemId, System.StringComparison.OrdinalIgnoreCase))
            {
                pieceType = ShelterPieceType.Floor;
                return true;
            }

            if (string.Equals(itemId, wallItemId, System.StringComparison.OrdinalIgnoreCase))
            {
                pieceType = ShelterPieceType.Wall;
                return true;
            }

            if (string.Equals(itemId, roofItemId, System.StringComparison.OrdinalIgnoreCase))
            {
                pieceType = ShelterPieceType.Roof;
                return true;
            }

            if (string.Equals(itemId, doorItemId, System.StringComparison.OrdinalIgnoreCase))
            {
                pieceType = ShelterPieceType.Door;
                return true;
            }

            return false;
        }

        private void SetCompletionState(bool nextIsComplete)
        {
            if (isComplete == nextIsComplete)
                return;

            isComplete = nextIsComplete;
            ApplyVendorVisibilityForCompletion();

            if (!debugLogs)
                return;

            if (isComplete)
                Debug.Log("[ShelterState][SERVER] Shelter completed. Vendor enabled.", this);
            else
                Debug.Log("[ShelterState][SERVER] Shelter lost completion. Vendor disabled.", this);
        }

        /// <summary>
        /// Server-only vendor visibility toggle.
        /// - Complete shelter => vendor active.
        /// - Incomplete shelter => vendor inactive.
        ///
        /// Important NGO detail:
        /// If a scene NetworkObject starts disabled, it is not spawned automatically.
        /// So when shelter completes we also ensure the vendor NetworkObject is spawned.
        /// </summary>
        private void ApplyVendorVisibilityForCompletion()
        {
            if (!IsServerActive())
                return;

            if (vendorObject == null)
                return;

            if (isComplete)
            {
                if (!vendorObject.activeSelf)
                    vendorObject.SetActive(true);

                NetworkObject vendorNetworkObject = vendorObject.GetComponent<NetworkObject>();
                if (vendorNetworkObject != null && !vendorNetworkObject.IsSpawned)
                {
                    // This is not instantiating a new object. It spawns the existing scene vendor object.
                    vendorNetworkObject.Spawn(destroyWithScene: true);
                }

                return;
            }

            if (vendorObject.activeSelf)
                vendorObject.SetActive(false);
        }

        private static bool IsWithinRadiusXZ(Vector3 worldPos, Vector3 center, float sqrRadius)
        {
            float dx = worldPos.x - center.x;
            float dz = worldPos.z - center.z;
            float sqrDistance = (dx * dx) + (dz * dz);
            return sqrDistance <= sqrRadius;
        }

        private static bool IsServerActive()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsServer;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(shelterId))
                shelterId = "SHELTER_MAIN";

            if (shelterCheckRadius < 0f)
                shelterCheckRadius = 0f;

            if (requiredFloorCount < 0)
                requiredFloorCount = 0;

            if (requiredWallCount < 0)
                requiredWallCount = 0;

            if (requiredRoofCount < 0)
                requiredRoofCount = 0;
        }
#endif
    }
}

