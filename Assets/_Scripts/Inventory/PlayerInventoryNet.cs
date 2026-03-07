using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Persistence;
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

        /// <summary>
        /// SERVER ONLY: checks whether a slot index is valid for the authoritative inventory grid.
        /// </summary>
        public bool ServerIsValidSlotIndex(int slotIndex)
        {
            if (!IsServer || grid == null)
                return false;

            return slotIndex >= 0 && slotIndex < grid.Slots.Length;
        }

        /// <summary>
        /// SERVER ONLY: true when the slot is currently empty.
        /// Returns false for invalid slot indices.
        /// </summary>
        public bool ServerIsSlotEmpty(int slotIndex)
        {
            if (!ServerIsValidSlotIndex(slotIndex))
                return false;

            return grid.Slots[slotIndex].IsEmpty;
        }

        /// <summary>
        /// SERVER ONLY: resolves an ItemDef through this inventory's configured item database.
        /// This keeps food-consume validation inventory-driven and authoritative.
        /// </summary>
        public bool ServerTryGetItemDef(string itemId, out ItemDef def)
        {
            if (!IsServer)
            {
                def = null;
                return false;
            }

            return TryGetItemDef(itemId, out def);
        }

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

        public int ServerAddItem(string itemId, int quantity, int durability = -1, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0, FixedString64Bytes craftedBy = default)
        {
            if (!IsServer) return quantity;
            return AddItemServer(itemId, quantity, durability, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy);
        }

        public bool ServerTryAddItemToSlot(string itemId, int slotIndex, int durability = -1, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0, FixedString64Bytes craftedBy = default)
        {
            if (!IsServer || grid == null) return false;
            if (slotIndex < 0) return false;

            if (!grid.TryAddOneToSlot(itemId, slotIndex, durability, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy))
                return false;

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        /// <summary>
        /// Backward-compatible overload.
        /// </summary>
        public bool ServerTryGetSlotItem(int slotIndex, out string itemId, out int qty, out int durability)
        {
            return ServerTryGetSlotItem(slotIndex, out itemId, out qty, out durability, out _);
        }

        /// <summary>
        /// SERVER: read a slot's current item data including instance bonuses.
        /// </summary>
        public bool ServerTryGetSlotItem(int slotIndex, out string itemId, out int qty, out int durability, out ItemInstanceData instanceData)
        {
            itemId = string.Empty;
            qty = 0;
            durability = 0;
            instanceData = default;

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
            instanceData = slot.InstanceData;

            // Backward-safe normalization if older data left durability unset.
            if (durability <= 0 && TryGetItemDef(itemId, out var def) && def.MaxDurability > 0)
                durability = def.MaxDurability;

            return true;
        }

        /// <summary>
        /// Backward-compatible overload.
        /// </summary>
        public bool ServerRemoveOneAtSlot(int slotIndex, out string removedItemId, out int removedDurability)
        {
            return ServerRemoveOneAtSlot(slotIndex, out removedItemId, out removedDurability, out _);
        }

        /// <summary>
        /// SERVER: removes one item from a specific slot and returns instance bonuses.
        /// </summary>
        public bool ServerRemoveOneAtSlot(int slotIndex, out string removedItemId, out int removedDurability, out ItemInstanceData removedInstanceData)
        {
            removedItemId = string.Empty;
            removedDurability = 0;
            removedInstanceData = default;

            if (!IsServer || grid == null)
                return false;

            if (slotIndex < 0 || slotIndex >= grid.Slots.Length)
                return false;

            var slot = grid.Slots[slotIndex];
            if (slot.IsEmpty || slot.Stack.Quantity <= 0)
                return false;

            removedItemId = slot.Stack.ItemId;
            removedDurability = slot.Durability;
            removedInstanceData = slot.InstanceData;

            if (TryGetItemDef(removedItemId, out var def) && def.MaxDurability > 0 && removedDurability <= 0)
                removedDurability = def.MaxDurability;

            slot.Stack.Quantity -= 1;
            if (slot.Stack.Quantity <= 0)
            {
                grid.Slots[slotIndex] = new InventorySlot { IsEmpty = true };
            }
            else
            {
                // If more than one remains in stack, this slot is a regular stack and bonuses should stay default.
                if (slot.Stack.Quantity > 1)
                    slot.InstanceData = default;

                grid.Slots[slotIndex] = slot;
            }

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        /// <summary>
        /// SERVER: overwrite a slot with validated state.
        /// </summary>
        public bool ServerTrySetSlot(int slotIndex, string itemId, int qty, int durability, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0, FixedString64Bytes craftedBy = default)
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

            ItemInstanceData instanceData = default;
            if (durable || qty == 1)
            {
                instanceData.BonusStrength = bonusStrength;
                instanceData.BonusDexterity = bonusDexterity;
                instanceData.BonusIntelligence = bonusIntelligence;
                instanceData.CraftedBy = craftedBy;
            }

            grid.Slots[slotIndex] = new InventorySlot
            {
                IsEmpty = false,
                Stack = new ItemStack { ItemId = itemId, Quantity = qty },
                Durability = finalDurability,
                InstanceData = instanceData
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


        /// <summary>
        /// SERVER ONLY: replace authoritative grid from persistence payload and replicate snapshot.
        /// </summary>
        public void ServerLoadGrid(InventoryGridSaveData saveData)
        {
            if (!IsServer)
                return;

            if (saveData == null)
                return;

            int targetWidth = Mathf.Max(1, saveData.w);
            int targetHeight = Mathf.Max(1, saveData.h);
            int expectedSlots = targetWidth * targetHeight;

            grid = new InventoryGrid(targetWidth, targetHeight, itemDatabase);

            if (saveData.slots != null)
            {
                int copyCount = Mathf.Min(expectedSlots, saveData.slots.Count);
                for (int i = 0; i < copyCount; i++)
                {
                    var slot = saveData.slots[i];
                    if (slot == null || string.IsNullOrWhiteSpace(slot.id) || slot.q <= 0)
                        continue;

                    if (!TryGetItemDef(slot.id, out var def) || def == null)
                        continue;

                    int maxStack = Mathf.Max(1, def.MaxDurability > 0 ? 1 : def.MaxStack);
                    int qty = Mathf.Clamp(slot.q, 1, maxStack);

                    grid.Slots[i] = new InventorySlot
                    {
                        IsEmpty = false,
                        Stack = new ItemStack { ItemId = def.ItemId, Quantity = qty },
                        Durability = 0,
                        InstanceData = default
                    };
                }
            }

            ForceSendSnapshotToOwner();
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

        public int AddItemServer(string itemId, int quantity, int durability = -1, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0, FixedString64Bytes craftedBy = default)
        {
            if (!IsServer || grid == null || quantity <= 0)
                return quantity;

            // Prevent partial adds for server systems that assume atomic inventory writes.
            if (!grid.CanAdd(itemId, quantity, out _, durability, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy))
                return quantity;

            var remainder = grid.Add(itemId, quantity, durability, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy);

            if (remainder < quantity)
            {
                knownItems?.EnsureKnown(itemId);

                // Save trigger hook for server-authoritative progression mutations (harvest/craft/vendor/item grants).
                SaveManager.NotifyPlayerProgressChanged(GetComponent<PlayerNetworkRoot>());
            }

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
                        MaxDurability = 0,
                        BonusStrength = 0,
                        BonusDexterity = 0,
                        BonusIntelligence = 0,
                        CraftedBy = default
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
                    MaxDurability = maxDurability,
                    BonusStrength = s.InstanceData.BonusStrength,
                    BonusDexterity = s.InstanceData.BonusDexterity,
                    BonusIntelligence = s.InstanceData.BonusIntelligence,
                    CraftedBy = s.InstanceData.CraftedBy
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









