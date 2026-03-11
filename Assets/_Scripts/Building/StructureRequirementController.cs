using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Generic server-authoritative controller that evaluates placed build pieces against
    /// a reusable StructureRequirementDef and enables/disables scene references accordingly.
    ///
    /// This replaces the old shelter-only completion pattern with a reusable requirement-driven rule.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class StructureRequirementController : NetworkBehaviour
    {
        [Header("Definition")]
        [SerializeField] private StructureRequirementDef definition;
        [SerializeField] private Transform centerPoint;

        [Header("Unlock Objects")]
        [SerializeField] private GameObject[] unlockObjects = new GameObject[0];
        [SerializeField] private bool disableUnlockObjectsWhenIncomplete = true;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = true;
        [SerializeField] private string lastEvaluationSummary = string.Empty;

        private readonly NetworkVariable<bool> isComplete =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsComplete => isComplete.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                ServerReevaluate();
        }

        /// <summary>
        /// Simple first-pass integration helper used by placement and destruction systems.
        /// This intentionally avoids introducing a registry or event bus.
        /// </summary>
        public static void ServerReevaluateAllActive()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
                return;

            StructureRequirementController[] controllers = FindObjectsByType<StructureRequirementController>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                StructureRequirementController controller = controllers[i];
                if (controller == null)
                    continue;

                controller.ServerReevaluate();
            }
        }

        /// <summary>
        /// SERVER ONLY: recomputes completion from authoritative placed structure state.
        /// </summary>
        public void ServerReevaluate()
        {
            if (!IsServerActive())
                return;

            Transform effectiveCenter = centerPoint != null ? centerPoint : transform;
            if (definition == null)
            {
                lastEvaluationSummary = "Missing definition";
                if (debugLogs)
                    Debug.LogWarning("[StructureRequirement][SERVER] Re-evaluation skipped: definition missing.", this);

                SetCompletionState(false, "Missing definition");
                ApplyUnlockObjectsForState(false);
                return;
            }

            StructureRequirementEntry[] requiredEntries = definition.RequiredEntries ?? System.Array.Empty<StructureRequirementEntry>();
            string requirementId = string.IsNullOrWhiteSpace(definition.RequirementId) ? name : definition.RequirementId;
            float radius = Mathf.Max(0f, definition.Radius);
            float sqrRadius = radius * radius;

            if (debugLogs)
                Debug.Log($"[StructureRequirement][SERVER] Re-evaluating requirementId={requirementId}", this);

            var countsByItemId = CountMatchingPieces(requiredEntries, effectiveCenter.position, sqrRadius);
            bool nextIsComplete = true;
            var summaryLines = new List<string>(requiredEntries.Length);

            for (int i = 0; i < requiredEntries.Length; i++)
            {
                StructureRequirementEntry entry = requiredEntries[i];
                string sourceItemId = string.IsNullOrWhiteSpace(entry.SourceItemId) ? string.Empty : entry.SourceItemId.Trim();
                int requiredCount = Mathf.Max(1, entry.RequiredCount);
                int currentCount = countsByItemId.TryGetValue(sourceItemId, out int foundCount) ? foundCount : 0;

                summaryLines.Add($"{sourceItemId}:{currentCount}/{requiredCount}");

                if (debugLogs)
                    Debug.Log($"[StructureRequirement][SERVER] Count {sourceItemId} = {currentCount} / {requiredCount}", this);

                if (currentCount < requiredCount)
                    nextIsComplete = false;
            }

            lastEvaluationSummary = string.Join(", ", summaryLines);
            SetCompletionState(nextIsComplete, requirementId);
            ApplyUnlockObjectsForState(nextIsComplete);
        }

        private Dictionary<string, int> CountMatchingPieces(StructureRequirementEntry[] requiredEntries, Vector3 centerWorldPosition, float sqrRadius)
        {
            var wantedIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < requiredEntries.Length; i++)
            {
                string sourceItemId = requiredEntries[i].SourceItemId;
                if (string.IsNullOrWhiteSpace(sourceItemId))
                    continue;

                wantedIds.Add(sourceItemId.Trim());
            }

            var counts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            List<PlacedBuildPiece> pieces = PlacedBuildPieceRegistry.Snapshot();
            for (int i = 0; i < pieces.Count; i++)
            {
                PlacedBuildPiece piece = pieces[i];
                if (piece == null || piece.IsDestroyedOrPendingDestroy)
                    continue;

                if (!IsWithinRadiusXZ(piece.transform.position, centerWorldPosition, sqrRadius))
                    continue;

                string sourceItemId = string.IsNullOrWhiteSpace(piece.SourceItemId) ? string.Empty : piece.SourceItemId.Trim();
                if (!wantedIds.Contains(sourceItemId))
                    continue;

                counts[sourceItemId] = counts.TryGetValue(sourceItemId, out int currentCount)
                    ? currentCount + 1
                    : 1;
            }

            return counts;
        }

        private void SetCompletionState(bool nextIsComplete, string requirementId)
        {
            if (isComplete.Value == nextIsComplete)
                return;

            isComplete.Value = nextIsComplete;

            if (!debugLogs)
                return;

            if (nextIsComplete)
                Debug.Log($"[StructureRequirement][SERVER] Requirement complete: {requirementId}", this);
            else
                Debug.Log($"[StructureRequirement][SERVER] Requirement incomplete: {requirementId}", this);
        }

        /// <summary>
        /// Applies the server-decided completion result to scene references.
        /// This preserves the current shelter/vendor enable-disable behavior.
        /// </summary>
        private void ApplyUnlockObjectsForState(bool completed)
        {
            if (!IsServerActive() || unlockObjects == null)
                return;

            for (int i = 0; i < unlockObjects.Length; i++)
            {
                GameObject unlockObject = unlockObjects[i];
                if (unlockObject == null)
                    continue;

                if (completed)
                {
                    if (!unlockObject.activeSelf)
                        unlockObject.SetActive(true);

                    NetworkObject unlockNetworkObject = unlockObject.GetComponent<NetworkObject>();
                    if (unlockNetworkObject != null && !unlockNetworkObject.IsSpawned)
                        unlockNetworkObject.Spawn(destroyWithScene: true);

                    continue;
                }

                if (!disableUnlockObjectsWhenIncomplete)
                    continue;

                if (unlockObject.activeSelf)
                    unlockObject.SetActive(false);
            }
        }

        private static bool IsWithinRadiusXZ(Vector3 worldPosition, Vector3 centerPosition, float sqrRadius)
        {
            float dx = worldPosition.x - centerPosition.x;
            float dz = worldPosition.z - centerPosition.z;
            float sqrDistance = (dx * dx) + (dz * dz);
            return sqrDistance <= sqrRadius;
        }

        private static bool IsServerActive()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            return networkManager != null && networkManager.IsServer;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (centerPoint == null)
                centerPoint = transform;

            if (unlockObjects == null)
                unlockObjects = new GameObject[0];
        }
#endif
    }
}
