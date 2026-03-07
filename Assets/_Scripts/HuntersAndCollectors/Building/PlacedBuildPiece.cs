using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// PlacedBuildPiece
    /// --------------------------------------------------------------------
    /// Lightweight runtime marker for a placed world structure.
    ///
    /// Unified item model rule:
    /// - This tracks which inventory item created the structure.
    /// - It does not represent an inventory entry itself.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlacedBuildPiece : NetworkBehaviour
    {
        [SerializeField] private string sourceItemId;

        /// <summary>
        /// Stable source item id used to place this structure (eg: IT_Floor).
        /// </summary>
        public string SourceItemId => sourceItemId;

        /// <summary>
        /// Backward-compatible alias for older code still reading BuildPieceId.
        /// </summary>
        public string BuildPieceId => sourceItemId;

        /// <summary>
        /// SERVER ONLY helper so placement system can stamp the source item id.
        /// </summary>
        public void ServerSetSourceItemId(string value)
        {
            if (!IsServer)
                return;

            sourceItemId = value ?? string.Empty;
        }

        /// <summary>
        /// Backward-compatible setter wrapper.
        /// </summary>
        public void ServerSetBuildPieceId(string value)
        {
            ServerSetSourceItemId(value);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            Debug.Log($"[PlacedBuildPiece][SERVER] Spawned sourceItemId={sourceItemId} at pos={transform.position}", this);
        }
    }
}
