using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// VendorChestNet
    /// --------------------------------------------------------------------
    /// Server-owned vendor chest inventory.
    /// Replicated to all clients via InventorySnapshot (ClientRpc).
    ///
    /// UI NOTE:
    /// - Clients can't read the server InventoryGrid directly.
    /// - So we cache the last received snapshot locally and raise an event.
    /// </summary>
    public sealed class VendorChestNet : NetworkBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string vendorId = "VENDOR_001";

        [Header("Inventory (Server Authoritative)")]
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private int width = 4;
        [SerializeField] private int height = 4;

        private InventoryGrid grid;

        // -------------------------
        // CLIENT-SIDE SNAPSHOT CACHE
        // -------------------------

        /// <summary>
        /// The most recent snapshot received on THIS client.
        /// This is what the UI should render.
        /// </summary>
        public InventorySnapshot LastSnapshot { get; private set; }

        /// <summary>
        /// Fired when a new snapshot arrives on a client.
        /// Vendor UI can subscribe and re-render.
        /// </summary>
        public event Action<InventorySnapshot> OnSnapshotChanged;

        public string VendorId => vendorId;
        public InventoryGrid Grid => grid;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                grid = new InventoryGrid(width, height, itemDatabase);
            }
        }

        /// <summary>
        /// Server broadcasts authoritative chest snapshot to all connected clients.
        /// </summary>
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
            // Cache it locally so the UI can access it.
            LastSnapshot = snapshot;

            // Notify any listeners (Vendor UI) to re-render.
            OnSnapshotChanged?.Invoke(snapshot);

            Debug.Log($"[VendorChestNet][CLIENT] Chest snapshot received. W={snapshot.W} H={snapshot.H} Slots={(snapshot.Slots == null ? 0 : snapshot.Slots.Length)}");
        }

        /// <summary>
        /// Returns a user-friendly display name for an itemId.
        /// Falls back to raw itemId if not found.
        /// </summary>
        public string GetDisplayName(string itemId)
        {
            if (itemDatabase == null || string.IsNullOrWhiteSpace(itemId))
                return itemId ?? string.Empty;

            // Use your real API
            if (itemDatabase.TryGet(itemId, out var def))
                return def.DisplayName;

            // Fallback if unknown id
            return itemId;
        }

        // Persistent base prices for this vendor
        private readonly Dictionary<string, int> basePrices = new();

        public int GetBasePrice(string itemId)
        {
            if (basePrices.TryGetValue(itemId, out var price))
                return price;

            // Fallback price (optional)
            return 1;
        }

        public void SetBasePrice(string itemId, int price)
        {
            if (!IsServer) return;

            basePrices[itemId] = Mathf.Max(0, price);
        }
    }
}
