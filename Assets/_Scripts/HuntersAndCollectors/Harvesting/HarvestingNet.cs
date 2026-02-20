using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Handles owner harvest requests and applies validated server rewards.
    /// </summary>
    public sealed class HarvestingNet : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab and assign node registry in scene bootstrap.
        [SerializeField] private ResourceNodeRegistry nodeRegistry;
        [SerializeField] private PlayerInventoryNet inventory;
        [SerializeField] private KnownItemsNet knownItems;

        [ServerRpc(RequireOwnership = true)]
        public void RequestHarvestServerRpc(string nodeId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || nodeRegistry == null)
            {
                HarvestResultClientRpc(new HarvestResult { Success = false, Reason = FailureReason.InvalidRequest });
                return;
            }

            if (!nodeRegistry.TryGet(nodeId, out var node)) { HarvestResultClientRpc(new HarvestResult { Success = false, Reason = FailureReason.NodeNotHarvestable }); return; }
            if (!node.IsHarvestable()) { HarvestResultClientRpc(new HarvestResult { Success = false, Reason = FailureReason.OnCooldown }); return; }

            var remainder = inventory.AddItemServer(node.DropItemId, node.DropQuantity);
            if (remainder > 0) { HarvestResultClientRpc(new HarvestResult { Success = false, Reason = FailureReason.NotEnoughInventorySpace }); return; }
            knownItems.EnsureKnown(node.DropItemId);
            node.Consume();
            HarvestResultClientRpc(new HarvestResult { Success = true, Reason = FailureReason.None });
        }

        [ClientRpc]
        private void HarvestResultClientRpc(HarvestResult result) { }
    }
}
