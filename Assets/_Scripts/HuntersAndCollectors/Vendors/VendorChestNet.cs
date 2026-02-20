using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// Server-owned vendor chest inventory replicated to all clients.
    /// Also handles checkout requests via ServerRpc.
    /// </summary>
    public sealed class VendorChestNet : NetworkBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string vendorId = "VENDOR_001";

        [Header("Inventory")]
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private int width = 4;
        [SerializeField] private int height = 4;

        private InventoryGrid grid;
        private readonly VendorTransactionService transactionService = new();

        public string VendorId => vendorId;
        public InventoryGrid Grid => grid;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                grid = new InventoryGrid(width, height, itemDatabase);
        }

        // =========================================================
        // CHECKOUT ENTRY POINT
        // =========================================================

        [ServerRpc(RequireOwnership = false)]
        public void RequestCheckoutServerRpc(CheckoutRequest request, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || grid == null)
                return;

            var buyerClientId = rpcParams.Receive.SenderClientId;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(buyerClientId, out var buyerClient))
                return;

            var buyerRoot = buyerClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            var sellerRoot = OwnerClientId == NetworkManager.ServerClientId
                ? null
                : NetworkManager.Singleton.ConnectedClients[OwnerClientId].PlayerObject.GetComponent<PlayerNetworkRoot>();

            var context = new VendorTransactionService.VendorContext
            {
                Chest = this,
                Seller = sellerRoot
            };

            var result = transactionService.TryCheckout(buyerRoot, context, request);

            // Send result only to buyer
            var toBuyer = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { buyerClientId } }
            };

            ReceiveCheckoutResultClientRpc(result, toBuyer);

            // Broadcast chest state update
            ForceBroadcastSnapshot();
        }

        [ClientRpc]
        private void ReceiveCheckoutResultClientRpc(TransactionResult result, ClientRpcParams rpcParams = default)
        {
            // UI hook:
            // result.Result.Success
            // result.Result.Reason
            // result.TotalPrice
        }

        // =========================================================
        // SNAPSHOT REPLICATION
        // =========================================================

        public void ForceBroadcastSnapshot()
        {
            if (!IsServer || grid == null)
                return;

            var slots = new InventorySnapshot.SlotDto[grid.Slots.Length];

            for (var i = 0; i < slots.Length; i++)
            {
                var s = grid.Slots[i];

                slots[i] = s.IsEmpty
                    ? new InventorySnapshot.SlotDto { IsEmpty = true }
                    : new InventorySnapshot.SlotDto
                    {
                        IsEmpty = false,
                        ItemId = new FixedString64Bytes(s.Stack.ItemId),
                        Quantity = s.Stack.Quantity
                    };
            }

            ReceiveChestSnapshotClientRpc(new InventorySnapshot
            {
                W = grid.Width,
                H = grid.Height,
                Slots = slots
            });
        }

        [ClientRpc]
        private void ReceiveChestSnapshotClientRpc(InventorySnapshot snapshot)
        {
            // TODO: update chest UI
        }
    }
}
