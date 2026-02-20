using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// VendorInteractable
    /// --------------------------------------------------------------------
    /// Network entry point for opening vendor UI and requesting checkout.
    ///
    /// MVP rules:
    /// - Clients do NOT own vendor NPC objects, so RequireOwnership must be false.
    /// - Server resolves buyer from rpc sender id.
    /// - Seller is resolved from VendorChest owner (or left null if server-owned).
    /// </summary>
    public sealed class VendorInteractable : NetworkBehaviour
    {
        [SerializeField] private VendorChestNet vendorChest;

        private readonly VendorTransactionService transactionService = new();

        [ServerRpc(RequireOwnership = false)]
        public void RequestOpenVendorServerRpc(ServerRpcParams serverRpcParams = default)
        {
            if (!IsServer || vendorChest == null)
                return;

            // MVP: broadcast to all so everyone sees consistent stock.
            vendorChest.ForceBroadcastSnapshot();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestCheckoutServerRpc(CheckoutRequest request, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || vendorChest == null)
                return;

            // Identify buyer from the RPC sender.
            var buyerClientId = rpcParams.Receive.SenderClientId;

            if (!NetworkManager.ConnectedClients.TryGetValue(buyerClientId, out var buyerClient) || buyerClient.PlayerObject == null)
                return;

            var buyer = buyerClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            if (buyer == null)
                return;

            // Resolve seller:
            // - If chest is owned by a player, that owner is the seller.
            // - If server-owned chest, Seller will be null (server/vendor "NPC seller").
            PlayerNetworkRoot seller = null;

            var sellerClientId = vendorChest.OwnerClientId;
            if (sellerClientId != NetworkManager.ServerClientId &&
                NetworkManager.ConnectedClients.TryGetValue(sellerClientId, out var sellerClient) &&
                sellerClient.PlayerObject != null)
            {
                seller = sellerClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            }

            var context = new VendorTransactionService.VendorContext
            {
                Chest = vendorChest,
                Seller = seller
            };

            var result = transactionService.TryCheckout(buyer, context, request);

            // Return result ONLY to buyer
            var toBuyer = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { buyerClientId } }
            };

            TransactionResultClientRpc(result, toBuyer);

            // Update stock for everyone
            vendorChest.ForceBroadcastSnapshot();
        }

        [ClientRpc]
        private void TransactionResultClientRpc(TransactionResult result, ClientRpcParams rpc = default)
        {
            // TODO UI hook:
            // result.Result.Success
            // result.Result.Reason
            // result.TotalPrice
        }
    }
}
