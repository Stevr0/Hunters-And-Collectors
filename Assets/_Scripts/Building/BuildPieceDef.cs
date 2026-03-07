using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// BuildPieceDef
    /// --------------------------------------------------------------------
    /// Lightweight data definition for one placeable building piece.
    ///
    /// First-pass scope:
    /// - Holds an ID used by network placement requests.
    /// - Holds a display name for simple menus/debug.
    /// - Holds the NetworkObject prefab to spawn on server approval.
    /// - Includes a tiny placement offset and yaw-rotation toggle for future use.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Building/Build Piece Def", fileName = "BuildPieceDef")]
    public sealed class BuildPieceDef : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string buildPieceId;
        [SerializeField] private string displayName;

        [Header("Prefab")]
        [SerializeField] private NetworkObject prefab;

        [Header("Optional Placement")]
        [SerializeField] private Vector3 placementOffset;
        [SerializeField] private bool allowYawRotation = true;

        public string BuildPieceId => buildPieceId;
        public string DisplayName => displayName;
        public NetworkObject Prefab => prefab;
        public Vector3 PlacementOffset => placementOffset;
        public bool AllowYawRotation => allowYawRotation;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Keep editor data tidy so runtime issues are easier to diagnose.
            if (buildPieceId != null)
                buildPieceId = buildPieceId.Trim();

            if (displayName != null)
                displayName = displayName.Trim();

            if (string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(buildPieceId))
                displayName = buildPieceId;

            // Hard validation warnings requested for first-pass authoring safety.
            if (string.IsNullOrWhiteSpace(buildPieceId))
                Debug.LogWarning($"[BuildPieceDef] BuildPieceId is empty on asset '{name}'.", this);

            if (prefab == null)
                Debug.LogWarning($"[BuildPieceDef] Prefab is null on asset '{name}'.", this);
        }
#endif
    }
}
