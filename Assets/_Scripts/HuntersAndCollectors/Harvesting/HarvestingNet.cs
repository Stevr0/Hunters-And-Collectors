using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    public sealed class HarvestingNet : NetworkBehaviour
    {
        [Header("Scene Wiring")]
        [SerializeField] private ResourceNodeRegistry nodeRegistry;

        private PlayerInventoryNet inventory;
        private KnownItemsNet knownItems;

        public override void OnNetworkSpawn()
        {
            inventory = GetComponent<PlayerInventoryNet>();
            knownItems = GetComponent<KnownItemsNet>();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestHarvestServerRpc(string nodeId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            var toOwner = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

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

            // Apply reward first
            var remainder = inventory.AddItemServer(node.DropItemId, node.DropQuantity);

            if (remainder > 0)
            {
                HarvestResultClientRpc(Fail(FailureReason.NotEnoughInventorySpace), toOwner);
                return;
            }

            // Mark known + consume node
            knownItems.EnsureKnown(node.DropItemId);
            node.Consume();

            HarvestResultClientRpc(Ok(), toOwner);
        }

        [ClientRpc]
        private void HarvestResultClientRpc(HarvestResult result, ClientRpcParams rpcParams = default)
        {
            // TODO: hook UI feedback here (toast/sound)
            // result.Result.Success / result.Result.Reason
        }

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
