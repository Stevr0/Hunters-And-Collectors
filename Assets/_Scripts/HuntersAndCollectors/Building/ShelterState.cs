using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Tracks shelter completion and triggers vendor spawn logic on the server.
    /// </summary>
    public sealed class ShelterState : NetworkBehaviour
    {
        // Editor wiring checklist: attach to shelter root object and assign vendor prefabs/spawn points.
        [SerializeField] private bool isComplete;
        [SerializeField] private string ownerPlayerKey = string.Empty;

        /// <summary>
        /// Marks shelter complete using server authority and stores owner key.
        /// </summary>
        public void MarkComplete(string ownerKey)
        {
            if (!IsServer) return;
            isComplete = true;
            ownerPlayerKey = ownerKey;
            // TODO: Spawn vendor NPC and chest when prefab flow is finalized.
        }
    }
}
