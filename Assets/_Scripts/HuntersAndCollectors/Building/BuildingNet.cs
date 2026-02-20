using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Receives build placement requests and spawns server-owned build objects.
    /// </summary>
    public sealed class BuildingNet : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab and configure build piece defs.
        [SerializeField] private List<BuildPieceDef> buildPieces = new();

        [ServerRpc(RequireOwnership = true)]
        public void RequestPlaceBuildPieceServerRpc(string buildPieceId, Vector3 pos, float rotY)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(buildPieceId)) return;
            foreach (var def in buildPieces)
            {
                if (def == null || def.BuildPieceId != buildPieceId || def.Prefab == null) continue;
                var instance = Object.Instantiate(def.Prefab, pos, Quaternion.Euler(0f, rotY, 0f));
                if (instance.TryGetComponent<NetworkObject>(out var networkObject)) networkObject.Spawn();
                return;
            }
        }
    }
}
