using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Persistence;
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

        // ---------------------------------------------------------
        // Persistent base pricing table (ItemId -> BasePrice)
        // In MVP this can live in-memory, but your shard save must serialize it later.
        // ---------------------------------------------------------
        private readonly Dictionary<string, int> basePrices = new();

        // ---------------------------------------------------------
        // Pending seller payout coins (for offline sellers).
        // This MUST be persisted with shard/vendor data later.
        // ---------------------------------------------------------
        [SerializeField] private int pendingPayoutCoins = 0;

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
                    ? new InventorySnapshot.SlotDto { IsEmpty = true, ContentType = InventorySlotContentType.Empty }
                    : new InventorySnapshot.SlotDto
                    {
                        IsEmpty = false,
                        ContentType = InventorySlotContentType.Stack,
                        ItemId = new FixedString64Bytes(s.Stack.ItemId),
                        Quantity = s.Stack.Quantity,
                        Durability = 0,
                        MaxDurability = 0,
                        BonusStrength = 0,
                        BonusDexterity = 0,
                        BonusIntelligence = 0,
                        CraftedBy = default,
                        InstanceId = 0,
                        RolledDamage = 0f,
                        RolledDefence = 0f,
                        RolledSwingSpeed = 0f,
                        RolledMovementSpeed = 0f
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

            int nonEmpty = 0;
            if (snapshot.Slots != null)
            {
                for (int i = 0; i < snapshot.Slots.Length; i++)
                    if (!snapshot.Slots[i].IsEmpty) nonEmpty++;
            }
            Debug.Log($"[VendorChestNet][CLIENT] Snapshot on chest='{name}' netId={NetworkObjectId} vendorId={vendorId} NonEmpty={nonEmpty}");
            Debug.Log($"[VendorChestNet][CLIENT] Chest snapshot received. W={snapshot.W} H={snapshot.H} Slots={(snapshot.Slots == null ? 0 : snapshot.Slots.Length)} NonEmpty={nonEmpty}");
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

        /// <summary>
        /// Returns the base price for an item from this vendor's persistent pricing table.
        /// If missing, returns fallback (e.g. 1).
        /// </summary>
        public int GetBasePriceOrDefault(string itemId, int fallback)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return fallback;

            if (basePrices.TryGetValue(itemId, out var p))
                return Mathf.Max(0, p);

            return fallback;
        }

        /// <summary>
        /// Adds coins to the vendor's pending payout pool.
        /// Use this when the seller is offline.
        /// </summary>
        public void AddPendingPayoutCoins(int amount)
        {
            if (!IsServer) return;

            if (amount <= 0) return;

            // Keep it simple; clamp if you want to be extra safe.
            pendingPayoutCoins += amount;

            // TODO later:
            // - Mark vendor data dirty for shard persistence save.
            // - Optionally replicate pending payout to owner UI only.
        }


        public int PendingPayoutCoins => pendingPayoutCoins;

        public VendorSaveData ServerExportSaveData()
        {
            var data = new VendorSaveData
            {
                vendorId = vendorId,
                ownerPlayerKey = string.Empty,
                treasuryCoins = Mathf.Max(0, pendingPayoutCoins),
                basePrices = new List<KnownItemSaveData>(),
                chest = new InventoryGridSaveData
                {
                    w = grid != null ? grid.Width : width,
                    h = grid != null ? grid.Height : height,
                    slots = new List<InventorySlotSaveData>()
                }
            };

            foreach (var kvp in basePrices)
            {
                data.basePrices.Add(new KnownItemSaveData
                {
                    id = kvp.Key,
                    @base = Mathf.Max(0, kvp.Value)
                });
            }

            if (grid != null)
            {
                for (int i = 0; i < grid.Slots.Length; i++)
                {
                    var slot = grid.Slots[i];
                    if (slot.IsEmpty)
                    {
                        data.chest.slots.Add(null);
                        continue;
                    }

                    data.chest.slots.Add(new InventorySlotSaveData
                    {
                        kind = "Stack",
                        id = slot.Stack.ItemId,
                        q = slot.Stack.Quantity
                    });
                }
            }

            return data;
        }

        public void ServerApplySaveData(VendorSaveData data, ItemDatabase db)
        {
            if (!IsServer || data == null)
                return;

            int targetWidth = data.chest != null ? Mathf.Max(1, data.chest.w) : width;
            int targetHeight = data.chest != null ? Mathf.Max(1, data.chest.h) : height;

            grid = new InventoryGrid(targetWidth, targetHeight, itemDatabase != null ? itemDatabase : db);
            pendingPayoutCoins = Mathf.Max(0, data.treasuryCoins);
            basePrices.Clear();

            if (data.basePrices != null)
            {
                for (int i = 0; i < data.basePrices.Count; i++)
                {
                    KnownItemSaveData row = data.basePrices[i];
                    if (row == null || string.IsNullOrWhiteSpace(row.id))
                        continue;

                    basePrices[row.id.Trim()] = Mathf.Max(0, row.@base);
                }
            }

            if (data.chest != null && data.chest.slots != null)
            {
                int copyCount = Mathf.Min(grid.Slots.Length, data.chest.slots.Count);
                for (int i = 0; i < copyCount; i++)
                {
                    InventorySlotSaveData saveSlot = data.chest.slots[i];
                    if (saveSlot == null || string.IsNullOrWhiteSpace(saveSlot.id) || saveSlot.q <= 0)
                        continue;

                    ItemDatabase useDb = itemDatabase != null ? itemDatabase : db;
                    if (useDb == null || !useDb.TryGet(saveSlot.id, out ItemDef def) || def == null)
                        continue;

                    int maxStack = Mathf.Max(1, def.MaxDurability > 0 ? 1 : def.MaxStack);
                    int qty = Mathf.Clamp(saveSlot.q, 1, maxStack);

                    grid.Slots[i] = new InventorySlot
                    {
                        IsEmpty = false,
                        ContentType = InventorySlotContentType.Stack,
                        Stack = new ItemStack { ItemId = def.ItemId, Quantity = qty },
                        Durability = 0,
                        InstanceData = default
                    };
                }
            }

            ForceBroadcastSnapshot();
        }
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



