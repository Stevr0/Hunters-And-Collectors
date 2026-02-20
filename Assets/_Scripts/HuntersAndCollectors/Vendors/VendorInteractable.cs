using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// Network interactable entry point for opening vendor and requesting checkout.
    /// </summary>
    public sealed class VendorInteractable : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Vendor NPC prefab and assign chest + transaction service host object.
        [SerializeField] private VendorChestNet vendorChest;
        private readonly VendorTransactionService transactionService = new();

        [ServerRpc(RequireOwnership = true)]
        public void RequestOpenVendorServerRpc(ServerRpcParams serverRpcParams = default)
        {
            if (!IsServer || vendorChest == null) return;
            vendorChest.ForceBroadcastSnapshot();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestCheckoutServerRpc(CheckoutRequest request, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || vendorChest == null) return;
            if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out var sender)) return;
            var buyer = sender.PlayerObject.GetComponent<PlayerNetworkRoot>();
            var context = new VendorTransactionService.VendorContext { Chest = vendorChest };
            var result = transactionService.TryCheckout(buyer, context, request);
            TransactionResultClientRpc(result, new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpcParams.Receive.SenderClientId } } });
        }

        [ClientRpc]
        private void TransactionResultClientRpc(TransactionResult result, ClientRpcParams rpc = default) { }
    }
}
