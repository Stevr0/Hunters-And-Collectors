using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.World
{
    /// <summary>
    /// Networked world interaction component used for entrances, exits, doors, and passages.
    /// Clients only request use; the server validates requirements and executes the transfer.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AreaTransferInteractable : NetworkBehaviour
    {
        [Header("Transfer")]
        [Tooltip("Authored transfer definition that describes the destination, requirements, and optional unlocks.")]
        [SerializeField] private AreaTransferDef transferDef;

        [Header("Interaction")]
        [Tooltip("Maximum distance allowed between the player and this transfer when the server validates use.")]
        [Min(0.1f)]
        [SerializeField] private float interactionRange = 3f;

        [Tooltip("Optional prompt override. Leave blank to use the transfer display name.")]
        [SerializeField] private string promptOverride = string.Empty;

        public AreaTransferDef TransferDef => transferDef;
        public float InteractionRange => interactionRange;

        public string BuildPromptText()
        {
            if (!string.IsNullOrWhiteSpace(promptOverride))
                return promptOverride.Trim();

            if (transferDef != null && !string.IsNullOrWhiteSpace(transferDef.DisplayName))
                return $"E: Enter {transferDef.DisplayName.Trim()}";

            return "E: Travel";
        }

        public bool IsPlayerWithinRange(PlayerNetworkRoot playerRoot)
        {
            if (playerRoot == null)
                return false;

            Collider ownCollider = GetComponentInChildren<Collider>(true);
            if (ownCollider == null)
                return Vector3.Distance(playerRoot.transform.position, transform.position) <= interactionRange;

            Vector3 closestPoint = ownCollider.ClosestPoint(playerRoot.transform.position);
            return Vector3.Distance(playerRoot.transform.position, closestPoint) <= interactionRange;
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestUseTransferServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (transferDef == null)
            {
                Debug.LogWarning($"[AreaTransferInteractable] '{name}' is missing AreaTransferDef.", this);
                return;
            }

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client) ||
                client.PlayerObject == null)
            {
                Debug.LogWarning($"[AreaTransferInteractable] Could not resolve PlayerObject for clientId={rpcParams.Receive.SenderClientId}.", this);
                return;
            }

            PlayerNetworkRoot playerRoot = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
            if (playerRoot == null)
            {
                Debug.LogWarning($"[AreaTransferInteractable] PlayerObject for clientId={rpcParams.Receive.SenderClientId} is missing PlayerNetworkRoot.", this);
                return;
            }

            AreaTransferService.EnsureInstance().ServerRequestTransfer(playerRoot, this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            interactionRange = Mathf.Max(0.1f, interactionRange);
            promptOverride = string.IsNullOrWhiteSpace(promptOverride) ? string.Empty : promptOverride.Trim();
        }
#endif
    }
}
