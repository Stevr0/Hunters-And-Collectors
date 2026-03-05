using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Inventory
{
    public sealed class PlayerInventoryNet : NetworkBehaviour
    {
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private int width = 6;
        [SerializeField] private int height = 4;

        private InventoryGrid grid;
        private KnownItemsNet knownItems;

        private InventorySnapshot lastSnapshot; // client-side shadow copy

        public InventoryGrid Grid => grid;

        // --------------------------------------------------------------------
        // Snapshot batching (SERVER ONLY)
        // --------------------------------------------------------------------
        private int _serverBatchDepth = 0;
        private bool _serverBatchDirty = false;

        public InventorySnapshot LastSnapshot { get; private set; }

        public event Action<InventorySnapshot> OnSnapshotReceived;
        public event Action<InventorySnapshot> OnSnapshotChanged;

        public void BeginServerBatch()
        {
            if (!IsServer) return;
            _serverBatchDepth++;
        }

        public void EndServerBatchAndSendSnapshotToOwner()
        {
            if (!IsServer) return;

            _serverBatchDepth = Mathf.Max(0, _serverBatchDepth - 1);

            if (_serverBatchDepth == 0 && _serverBatchDirty)
            {
                _serverBatchDirty = false;
                ForceSendSnapshotToOwner();
            }
        }

        public void EndServerBatchWithoutSending()
        {
            if (!IsServer) return;

            _serverBatchDepth = Mathf.Max(0, _serverBatchDepth - 1);

            if (_serverBatchDepth == 0)
                _serverBatchDirty = false;
        }

        private void MarkDirtyAndMaybeSendSnapshot()
        {
            if (!IsServer || grid == null) return;

            if (_serverBatchDepth > 0)
            {
                _serverBatchDirty = true;
                return;
            }

            ForceSendSnapshotToOwner();
        }

        public bool ServerHasItem(string itemId, int quantity)
        {
            if (!IsServer || grid == null) return false;
            return grid.CanRemove(itemId, quantity);
        }

        public bool ServerRemoveItem(string itemId, int quantity)
        {
            if (!IsServer || grid == null) return false;
            if (!grid.Remove(itemId, quantity)) return false;

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        public int ServerAddItem(string itemId, int quantity, int durability = -1)
        {
            if (!IsServer) return quantity;
            return AddItemServer(itemId, quantity, durability);
        }

        public bool ServerTryAddItemToSlot(string itemId, int slotIndex, int durability = -1)
        {
            if (!IsServer || grid == null) return false;
            if (slotIndex < 0) return false;

            if (!grid.TryAddOneToSlot(itemId, slotIndex, durability))
                return false;

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        /// <summary>
        /// SERVER: read a slot's current item data.
        /// </summary>
        public bool ServerTryGetSlotItem(int slotIndex, out string itemId, out int qty, out int durability)
        {
            itemId = string.Empty;
            qty = 0;
            durability = 0;

            if (!IsServer || grid == null)
                return false;

            if (slotIndex < 0 || slotIndex >= grid.Slots.Length)
                return false;

            var slot = grid.Slots[slotIndex];
            if (slot.IsEmpty)
                return false;

            itemId = slot.Stack.ItemId;
            qty = slot.Stack.Quantity;
            durability = slot.Durability;

            // Backward-safe normalization if older data left durability unset.
            if (durability <= 0 && TryGetItemDef(itemId, out var def) && def.MaxDurability > 0)
                durability = def.MaxDurability;

            return true;
        }

        /// <summary>
        /// SERVER: removes one item from a specific slot.
        /// </summary>
        public bool ServerRemoveOneAtSlot(int slotIndex, out string removedItemId, out int removedDurability)
        {
            removedItemId = string.Empty;
            removedDurability = 0;

            if (!IsServer || grid == null)
                return false;

            if (slotIndex < 0 || slotIndex >= grid.Slots.Length)
                return false;

            var slot = grid.Slots[slotIndex];
            if (slot.IsEmpty || slot.Stack.Quantity <= 0)
                return false;

            removedItemId = slot.Stack.ItemId;
            removedDurability = slot.Durability;

            if (TryGetItemDef(removedItemId, out var def) && def.MaxDurability > 0 && removedDurability <= 0)
                removedDurability = def.MaxDurability;

            slot.Stack.Quantity -= 1;
            if (slot.Stack.Quantity <= 0)
            {
                grid.Slots[slotIndex] = new InventorySlot { IsEmpty = true };
            }
            else
            {
                grid.Slots[slotIndex] = slot;
            }

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        /// <summary>
        /// SERVER: overwrite a slot with validated state.
        /// </summary>
        public bool ServerTrySetSlot(int slotIndex, string itemId, int qty, int durability)
        {
            if (!IsServer || grid == null)
                return false;

            if (slotIndex < 0 || slotIndex >= grid.Slots.Length)
                return false;

            if (string.IsNullOrWhiteSpace(itemId) || qty <= 0)
            {
                grid.Slots[slotIndex] = new InventorySlot { IsEmpty = true };
                MarkDirtyAndMaybeSendSnapshot();
                return true;
            }

            if (!TryGetItemDef(itemId, out var def))
                return false;

            bool durable = def.MaxDurability > 0;
            int maxStack = durable ? 1 : Mathf.Max(1, def.MaxStack);

            if (qty > maxStack)
                return false;

            if (durable && qty != 1)
                return false;

            int finalDurability = 0;
            if (durable)
            {
                if (durability <= 0)
                    finalDurability = def.MaxDurability;
                else
                    finalDurability = Mathf.Clamp(durability, 1, def.MaxDurability);
            }

            grid.Slots[slotIndex] = new InventorySlot
            {
                IsEmpty = false,
                Stack = new ItemStack { ItemId = itemId, Quantity = qty },
                Durability = finalDurability
            };

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        /// <summary>
        /// SERVER: damage a specific slot item's durability.
        /// If it breaks, the item is removed from the slot.
        /// </summary>
        public bool ServerDamageDurabilityAtSlot(int slotIndex, int amount, out bool broke)
        {
            broke = false;

            if (!IsServer || grid == null || amount <= 0)
                return false;

            if (slotIndex < 0 || slotIndex >= grid.Slots.Length)
                return false;

            var slot = grid.Slots[slotIndex];
            if (slot.IsEmpty || slot.Stack.Quantity <= 0)
                return false;

            if (!TryGetItemDef(slot.Stack.ItemId, out var def) || def.MaxDurability <= 0)
                return false;

            if (slot.Stack.Quantity != 1)
                return false;

            int current = slot.Durability > 0 ? slot.Durability : def.MaxDurability;
            int next = Mathf.Max(0, current - amount);

            if (next <= 0)
            {
                grid.Slots[slotIndex] = new InventorySlot { IsEmpty = true };
                broke = true;
            }
            else
            {
                slot.Durability = next;
                grid.Slots[slotIndex] = slot;
            }

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        /// <summary>
        /// SERVER: find first non-empty slot containing itemId.
        /// </summary>
        public bool ServerTryFindFirstSlotWithItem(string itemId, out int slotIndex)
        {
            slotIndex = -1;
            if (!IsServer || grid == null || string.IsNullOrWhiteSpace(itemId))
                return false;

            string canonical = itemId.Trim();
            for (int i = 0; i < grid.Slots.Length; i++)
            {
                var slot = grid.Slots[i];
                if (slot.IsEmpty)
                    continue;

                if (string.Equals(slot.Stack.ItemId, canonical, StringComparison.Ordinal))
                {
                    slotIndex = i;
                    return true;
                }
            }

            return false;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                knownItems = GetComponent<KnownItemsNet>();
                grid = new InventoryGrid(width, height, itemDatabase);

                ForceSendSnapshotToOwner();
            }
        }

        public void ForceSendSnapshotToOwner()
        {
            if (!IsServer || grid == null) return;

            Debug.Log($"[InventoryNet][SERVER] Sending snapshot to OwnerClientId={OwnerClientId} slots={grid.Slots.Length}");

            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            ReceiveInventorySnapshotClientRpc(ToSnapshot(grid, itemDatabase), rpcParams);
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestMoveSlotServerRpc(int fromIndex, int toIndex)
        {
            if (!IsServer || grid == null) return;
            if (fromIndex < 0 || toIndex < 0) return;

            if (grid.TryMoveSlot(fromIndex, toIndex))
                ForceSendSnapshotToOwner();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestSplitStackServerRpc(int index, int splitAmount)
        {
            if (!IsServer || grid == null || splitAmount <= 0) return;

            if (grid.TrySplitStack(index, splitAmount, out _))
                ForceSendSnapshotToOwner();
        }

        [ClientRpc]
        private void ReceiveInventorySnapshotClientRpc(InventorySnapshot snapshot, ClientRpcParams rpcParams = default)
        {
            LastSnapshot = snapshot;
            OnSnapshotChanged?.Invoke(snapshot);

            Debug.Log($"[InventoryNet][CLIENT] Snapshot received. W={snapshot.W} H={snapshot.H} Slots={snapshot.Slots?.Length ?? 0} IsOwner={IsOwner} OwnerClientId={OwnerClientId}");

            OnSnapshotReceived?.Invoke(snapshot);
        }

        public int AddItemServer(string itemId, int quantity, int durability = -1)
        {
            if (!IsServer || grid == null || quantity <= 0) return quantity;

            var remainder = grid.Add(itemId, quantity, durability);

            if (remainder < quantity)
                knownItems?.EnsureKnown(itemId);

            MarkDirtyAndMaybeSendSnapshot();
            return remainder;
        }

        private static InventorySnapshot ToSnapshot(InventoryGrid source, ItemDatabase itemDatabase)
        {
            var slots = new InventorySnapshot.SlotDto[source.Slots.Length];

            for (var i = 0; i < slots.Length; i++)
            {
                var s = source.Slots[i];

                if (s.IsEmpty)
                {
                    slots[i] = new InventorySnapshot.SlotDto
                    {
                        IsEmpty = true,
                        ItemId = default,
                        Quantity = 0,
                        Durability = 0,
                        MaxDurability = 0
                    };
                    continue;
                }

                int maxDurability = 0;
                if (itemDatabase != null && itemDatabase.TryGet(s.Stack.ItemId, out var def) && def != null)
                    maxDurability = Mathf.Max(0, def.MaxDurability);

                int durability = maxDurability > 0
                    ? Mathf.Clamp(s.Durability <= 0 ? maxDurability : s.Durability, 1, maxDurability)
                    : 0;

                slots[i] = new InventorySnapshot.SlotDto
                {
                    IsEmpty = false,
                    ItemId = new FixedString64Bytes(s.Stack.ItemId),
                    Quantity = s.Stack.Quantity,
                    Durability = durability,
                    MaxDurability = maxDurability
                };
            }

            return new InventorySnapshot
            {
                W = source.Width,
                H = source.Height,
                Slots = slots
            };
        }

        private bool TryGetItemDef(string itemId, out ItemDef def)
        {
            def = null;
            if (itemDatabase == null || string.IsNullOrWhiteSpace(itemId))
                return false;

            return itemDatabase.TryGet(itemId, out def) && def != null;
        }
    }
}
