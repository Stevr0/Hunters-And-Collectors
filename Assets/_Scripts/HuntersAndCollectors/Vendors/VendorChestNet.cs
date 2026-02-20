using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// Server-owned vendor chest inventory replicated to all clients as snapshots.
    /// NOTE: Checkout is handled by VendorInteractable (RPC entry point).
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

        // Client-side shadow snapshot (optional but useful for UI)
        private InventorySnapshot lastSnapshot;

        public string VendorId => vendorId;
        public InventoryGrid Grid => grid;
        public InventorySnapshot LastSnapshot => lastSnapshot;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                grid = new InventoryGrid(width, height, itemDatabase);
                ForceBroadcastSnapshot(); // initial state
            }
        }

        /// <summary>
        /// Broadcasts authoritative chest snapshot to all connected clients.
        /// </summary>
        public void ForceBroadcastSnapshot()
        {
            if (!IsServer || grid == null) return;

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

            ReceiveChestSnapshotClientRpc(new InventorySnapshot { W = grid.Width, H = grid.Height, Slots = slots });
        }

        [ClientRpc]
        private void ReceiveChestSnapshotClientRpc(InventorySnapshot snapshot)
        {
            // Store snapshot for UI
            lastSnapshot = snapshot;

            // TODO: if you have a UI controller, notify it here
        }
    }
}
