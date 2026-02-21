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

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Debug.Log(
                $"[VendorInteractable] OnNetworkSpawn NetId={NetworkObjectId} OwnerClientId={OwnerClientId} IsServer={IsServer} IsClient={IsClient}",
                this);
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestOpenVendorServerRpc(ServerRpcParams serverRpcParams = default)
        {
            if (!IsServer || vendorChest == null)
                return;

            vendorChest.ForceBroadcastSnapshot();
        }

        // This RPC is invoked by any client interacting with a world vendor object.
        // VendorInteractable is NOT client-owned, so default ServerRpc ownership checks
        // would reject client calls unless we explicitly allow non-owners.
        [ServerRpc(RequireOwnership = false)]
        public void RequestCheckoutServerRpc(CheckoutRequest request, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || vendorChest == null)
                return;

            var buyerClientId = rpcParams.Receive.SenderClientId;

            var lines = request.Lines;
            Debug.Log(
                $"[VendorInteractable] RequestCheckoutServerRpc RECEIVED senderClientId={buyerClientId} lines={lines?.Length ?? 0}",
                this);

            if (lines != null)
            {
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    Debug.Log(
                        $"[VendorInteractable] CheckoutLine[{i}] slotIndex={line.SlotIndex} qty={line.Quantity}",
                        this);
                }
            }

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
