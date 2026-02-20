using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Handles owner harvest requests and applies validated server rewards.
    ///
    /// MVP wiring:
    /// - ResourceNodeRegistry is AUTO-resolved from the active scene at runtime.
    /// - PlayerInventoryNet and KnownItemsNet are auto-resolved from the Player prefab.
    ///
    /// Server authority:
    /// - Only the server validates nodes, consumes nodes, and grants items.
    /// - Result is sent only to the requesting client (owner).
    /// </summary>
    public sealed class HarvestingNet : NetworkBehaviour
    {
        // Optional: allow manual assignment if you ever want it, but we auto-resolve if null.
        [SerializeField] private ResourceNodeRegistry nodeRegistry;

        private PlayerInventoryNet inventory;
        private KnownItemsNet knownItems;

        public override void OnNetworkSpawn()
        {
            // These components live on the Player prefab.
            inventory = GetComponent<PlayerInventoryNet>();
            knownItems = GetComponent<KnownItemsNet>();

            // IMPORTANT: Resolve registry on the server (authoritative).
            // We also resolve on host/client so debugging is easier, but server is what matters.
            if (nodeRegistry == null)
                nodeRegistry = FindObjectOfType<ResourceNodeRegistry>(true);
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestHarvestServerRpc(string nodeId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            // Target result only to the owner who requested the harvest.
            var toOwner = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            // Lazy re-resolve (covers cases where player spawns before registry Awake)
            if (nodeRegistry == null)
                nodeRegistry = FindObjectOfType<ResourceNodeRegistry>(true);

            if (nodeRegistry == null || inventory == null || knownItems == null)
            {
                HarvestResultClientRpc(Fail(FailureReason.InvalidRequest), toOwner);
                return;
            }

            if (string.IsNullOrWhiteSpace(nodeId) || !nodeRegistry.TryGet(nodeId, out var node) || node == null)
            {
                HarvestResultClientRpc(Fail(FailureReason.NodeNotHarvestable), toOwner);
                return;
            }

            if (!node.IsHarvestable())
            {
                HarvestResultClientRpc(Fail(FailureReason.OnCooldown), toOwner);
                return;
            }

            // Attempt to grant items first. If it doesn't fit, do NOT consume node.
            var remainder = inventory.AddItemServer(node.DropItemId, node.DropQuantity);
            if (remainder > 0)
            {
                HarvestResultClientRpc(Fail(FailureReason.NotEnoughInventorySpace), toOwner);
                return;
            }

            // Success: mark known + consume node
            knownItems.EnsureKnown(node.DropItemId);
            node.Consume();

            HarvestResultClientRpc(Ok(), toOwner);
        }

        [ClientRpc]
        private void HarvestResultClientRpc(HarvestResult result, ClientRpcParams rpcParams = default)
        {
            // TODO UI hook:
            // result.Result.Success
            // result.Result.Reason
        }

        // --------------------------------------------------------------------
        // Result builders (matches your ActionResult-based DTO pattern)
        // --------------------------------------------------------------------

        private static HarvestResult Fail(FailureReason reason)
        {
            return new HarvestResult
            {
                Result = new ActionResult { Success = false, Reason = reason }
            };
        }

        private static HarvestResult Ok()
        {
            return new HarvestResult
            {
                Result = new ActionResult { Success = true, Reason = FailureReason.None }
            };
        }
    }
}
