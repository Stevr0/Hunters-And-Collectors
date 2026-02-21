using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    public sealed class VendorInteractable : NetworkBehaviour
    {
        [SerializeField] private VendorChestNet vendorChest;

        // Public read-only access so UI can bind to the correct chest.
        public VendorChestNet Chest => vendorChest;

        private readonly VendorTransactionService transactionService = new();

        private void OnValidate()
        {
            // Editor-time safety: helps catch missing wiring early.
            if (vendorChest == null)
                Debug.LogWarning($"[VendorInteractable] '{name}' has no VendorChestNet assigned.", this);
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestOpenVendorServerRpc(ServerRpcParams serverRpcParams = default)
        {
            if (!IsServer || vendorChest == null)
                return;

            vendorChest.ForceBroadcastSnapshot();
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestCheckoutServerRpc(CheckoutRequest request, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || vendorChest == null)
                return;

            var buyerClientId = rpcParams.Receive.SenderClientId;

            if (!NetworkManager.ConnectedClients.TryGetValue(buyerClientId, out var buyerClient) || buyerClient.PlayerObject == null)
                return;

            var buyer = buyerClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            if (buyer == null)
                return;

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

            var toBuyer = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { buyerClientId } }
            };

            TransactionResultClientRpc(result, toBuyer);

            vendorChest.ForceBroadcastSnapshot();
        }

        [ClientRpc]
        private void TransactionResultClientRpc(TransactionResult result, ClientRpcParams rpc = default)
        {
            // UI hook lives elsewhere
        }
    }
}
