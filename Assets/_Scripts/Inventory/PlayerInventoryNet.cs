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
        private const int AuthoritativeWidth = 8;
        private const int AuthoritativeHeight = 4;

        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private int width = AuthoritativeWidth;
        [SerializeField] private int height = AuthoritativeHeight;

        [Header("Debug")]
        [SerializeField] private bool debugMoveTrace = true;

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

        /// <summary>
        /// SERVER ONLY: add one authoritative item instance into inventory.
        /// This is the preferred path for RNG-crafted non-stackable gear.
        /// </summary>
        public bool ServerTryAddItemInstance(ItemInstance instance, ItemInstanceData instanceData)
        {
            if (!IsServer || grid == null)
                return false;

            if (string.IsNullOrWhiteSpace(instance.ItemId))
                return false;

            if (!grid.TryAddInstance(instance, instanceData))
                return false;

            knownItems?.EnsureKnown(instance.ItemId);
            SaveManager.NotifyPlayerProgressChanged(GetComponent<PlayerNetworkRoot>());
            MarkDirtyAndMaybeSendSnapshot();
            return true;
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
        /// SERVER: read a slot's current item data including instance metadata.
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

            if (slot.ContentType == InventorySlotContentType.Instance)
            {
                itemId = slot.Instance.ItemId;
                qty = 1;
                durability = slot.Instance.CurrentDurability;
                instanceData = slot.InstanceData;
                return true;
            }

            itemId = slot.Stack.ItemId;
            qty = slot.Stack.Quantity;
            durability = slot.Durability;
            instanceData = slot.InstanceData;

            if (durability <= 0 && TryGetItemDef(itemId, out var def) && def.ResolveDurabilityMax() > 0)
                durability = def.ResolveDurabilityMax();

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
        /// SERVER: removes one item from a specific slot and returns instance metadata.
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
            if (slot.IsEmpty)
                return false;

            if (slot.ContentType == InventorySlotContentType.Instance)
            {
                removedItemId = slot.Instance.ItemId;
                removedDurability = slot.Instance.CurrentDurability;
                removedInstanceData = slot.InstanceData;
                grid.Slots[slotIndex] = MakeEmptySlot();
                MarkDirtyAndMaybeSendSnapshot();
                return true;
            }

            if (slot.Stack.Quantity <= 0)
                return false;

            removedItemId = slot.Stack.ItemId;
            removedDurability = slot.Durability;
            removedInstanceData = slot.InstanceData;

            slot.Stack.Quantity -= 1;
            if (slot.Stack.Quantity <= 0)
            {
                grid.Slots[slotIndex] = MakeEmptySlot();
            }
            else
            {
                if (slot.Stack.Quantity > 1)
                    slot.InstanceData = default;

                grid.Slots[slotIndex] = slot;
            }

            MarkDirtyAndMaybeSendSnapshot();
            return true;
        }

        /// <summary>
        /// SERVER: overwrite a slot with validated state.
        /// This path remains compatibility-friendly for existing systems that write by itemId.
        /// </summary>
        public bool ServerTrySetSlot(int slotIndex, string itemId, int qty, int durability, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0, FixedString64Bytes craftedBy = default)
        {
            if (!IsServer || grid == null)
                return false;

            if (slotIndex < 0 || slotIndex >= grid.Slots.Length)
                return false;

            if (string.IsNullOrWhiteSpace(itemId) || qty <= 0)
            {
                grid.Slots[slotIndex] = MakeEmptySlot();
                MarkDirtyAndMaybeSendSnapshot();
                return true;
            }

            if (!TryGetItemDef(itemId, out var def))
                return false;

            bool asInstance = def.UsesItemInstance;
            int maxStack = asInstance ? 1 : Mathf.Max(1, def.MaxStack);

            if (qty > maxStack)
                return false;

            if (asInstance && qty != 1)
                return false;

            if (asInstance)
            {
                int finalMax = durability > 0 ? durability : def.ResolveDurabilityMax();
                int finalCurrent = Mathf.Clamp(durability > 0 ? durability : finalMax, 1, Mathf.Max(1, finalMax));

                ItemInstance instance = new ItemInstance
                {
                    InstanceId = 0,
                    ItemId = itemId,
                    RolledDamage = def.ResolveDamageMin(),
                    RolledDefence = def.ResolveDefenceMin(),
                    RolledSwingSpeed = def.ResolveSwingSpeedMin(),
                    RolledMovementSpeed = def.ResolveMovementSpeedMin(),
                    MaxDurability = finalMax,
                    CurrentDurability = finalCurrent
                };

                ItemInstanceData instanceData = new ItemInstanceData
                {
                    BonusStrength = bonusStrength,
                    BonusDexterity = bonusDexterity,
                    BonusIntelligence = bonusIntelligence,
                    CraftedBy = craftedBy,
                    InstanceId = instance.InstanceId,
                    RolledDamage = instance.RolledDamage,
                    RolledDefence = instance.RolledDefence,
                    RolledSwingSpeed = instance.RolledSwingSpeed,
                    RolledMovementSpeed = instance.RolledMovementSpeed,
                    MaxDurability = instance.MaxDurability,
                    CurrentDurability = instance.CurrentDurability
                };

                grid.Slots[slotIndex] = new InventorySlot
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Instance,
                    Stack = new ItemStack { ItemId = itemId, Quantity = 1 },
                    Instance = instance,
                    Durability = finalCurrent,
                    InstanceData = instanceData
                };
            }
            else
            {
                grid.Slots[slotIndex] = new InventorySlot
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Stack,
                    Stack = new ItemStack { ItemId = itemId, Quantity = qty },
                    Instance = default,
                    Durability = 0,
                    InstanceData = default
                };
            }

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
            if (slot.IsEmpty)
                return false;

            if (slot.ContentType == InventorySlotContentType.Instance)
            {
                int max = Mathf.Max(0, slot.Instance.MaxDurability);
                if (max <= 0)
                    return false;

                int current = Mathf.Clamp(slot.Instance.CurrentDurability <= 0 ? max : slot.Instance.CurrentDurability, 1, max);
                int next = Mathf.Max(0, current - amount);

                if (next <= 0)
                {
                    grid.Slots[slotIndex] = MakeEmptySlot();
                    broke = true;
                }
                else
                {
                    slot.Instance.CurrentDurability = next;
                    slot.Durability = next;
                    slot.InstanceData.CurrentDurability = next;
                    grid.Slots[slotIndex] = slot;
                }

                MarkDirtyAndMaybeSendSnapshot();
                return true;
            }

            // Stack slots are not durable in current model.
            return false;
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

                string slotItemId = slot.ContentType == InventorySlotContentType.Instance
                    ? slot.Instance.ItemId
                    : slot.Stack.ItemId;

                if (string.Equals(slotItemId, canonical, StringComparison.Ordinal))
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
                EnforceAuthoritativeDimensions();

                knownItems = GetComponent<KnownItemsNet>();
                grid = new InventoryGrid(width, height, itemDatabase);

                if (debugMoveTrace)
                    Debug.Log($"[InventoryDragTrace][Server] Spawned authoritative inventory W={width} H={height} Slots={grid.Slots.Length}");

                ForceSendSnapshotToOwner();
            }
        }


        /// <summary>
        /// SERVER ONLY: replace authoritative grid from persistence payload and replicate snapshot.
        /// Supports both old v1 stack-only save slots and new v2 tagged-union save slots.
        /// </summary>
        public void ServerLoadGrid(InventoryGridSaveData saveData)
        {
            if (!IsServer)
                return;

            EnforceAuthoritativeDimensions();

            if (saveData == null)
                return;

            int targetWidth = Mathf.Max(1, width);
            int targetHeight = Mathf.Max(1, height);
            int expectedSlots = targetWidth * targetHeight;

            grid = new InventoryGrid(targetWidth, targetHeight, itemDatabase);

            if (saveData.slots != null)
            {
                int sourceWidth = Mathf.Max(1, saveData.w);
                int sourceHeight = Mathf.Max(1, saveData.h);
                int sourceCount = Mathf.Min(Mathf.Max(0, sourceWidth * sourceHeight), saveData.slots.Count);

                int copyCount = Mathf.Min(expectedSlots, sourceCount);
                for (int i = 0; i < copyCount; i++)
                {
                    var slot = saveData.slots[i];
                    if (slot == null || string.IsNullOrWhiteSpace(slot.id))
                        continue;

                    if (!TryGetItemDef(slot.id, out var def) || def == null)
                        continue;

                    bool slotSaysInstance = string.Equals(slot.kind, "Instance", StringComparison.OrdinalIgnoreCase);
                    bool shouldInstance = slotSaysInstance || def.UsesItemInstance;

                    if (shouldInstance)
                    {
                        int maxDurability = slot.maxDurability > 0 ? slot.maxDurability : def.ResolveDurabilityMax();
                        int currentDurability = slot.currentDurability > 0 ? slot.currentDurability : maxDurability;

                        ItemInstance instance = new ItemInstance
                        {
                            InstanceId = slot.instanceId,
                            ItemId = def.ItemId,
                            RolledDamage = slot.rolledDamage > 0f ? slot.rolledDamage : def.ResolveDamageMin(),
                            RolledDefence = slot.rolledDefence > 0f ? slot.rolledDefence : def.ResolveDefenceMin(),
                            RolledSwingSpeed = slot.rolledSwingSpeed > 0f ? slot.rolledSwingSpeed : def.ResolveSwingSpeedMin(),
                            RolledMovementSpeed = slot.rolledMovementSpeed > 0f ? slot.rolledMovementSpeed : def.ResolveMovementSpeedMin(),
                            MaxDurability = maxDurability,
                            CurrentDurability = Mathf.Clamp(currentDurability, 1, Mathf.Max(1, maxDurability))
                        };

                        ItemInstanceData data = new ItemInstanceData
                        {
                            BonusStrength = slot.bonusStrength,
                            BonusDexterity = slot.bonusDexterity,
                            BonusIntelligence = slot.bonusIntelligence,
                            CraftedBy = new FixedString64Bytes(slot.craftedBy ?? string.Empty),
                            InstanceId = instance.InstanceId,
                            RolledDamage = instance.RolledDamage,
                            RolledDefence = instance.RolledDefence,
                            RolledSwingSpeed = instance.RolledSwingSpeed,
                            RolledMovementSpeed = instance.RolledMovementSpeed,
                            MaxDurability = instance.MaxDurability,
                            CurrentDurability = instance.CurrentDurability
                        };

                        grid.Slots[i] = new InventorySlot
                        {
                            IsEmpty = false,
                            ContentType = InventorySlotContentType.Instance,
                            Stack = new ItemStack { ItemId = def.ItemId, Quantity = 1 },
                            Instance = instance,
                            Durability = instance.CurrentDurability,
                            InstanceData = data
                        };
                    }
                    else
                    {
                        int maxStack = Mathf.Max(1, def.MaxStack);
                        int qty = Mathf.Clamp(Mathf.Max(1, slot.q), 1, maxStack);

                        grid.Slots[i] = new InventorySlot
                        {
                            IsEmpty = false,
                            ContentType = InventorySlotContentType.Stack,
                            Stack = new ItemStack { ItemId = def.ItemId, Quantity = qty },
                            Instance = default,
                            Durability = 0,
                            InstanceData = default
                        };
                    }
                }
            }

            ForceSendSnapshotToOwner();
        }

        private void EnforceAuthoritativeDimensions()
        {
            if (width != AuthoritativeWidth || height != AuthoritativeHeight)
            {
                if (debugMoveTrace)
                    Debug.LogWarning($"[InventoryDragTrace][Server] Correcting inventory dimensions from {width}x{height} to {AuthoritativeWidth}x{AuthoritativeHeight}");

                width = AuthoritativeWidth;
                height = AuthoritativeHeight;
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
            if (!IsServer || grid == null)
                return;

            if (debugMoveTrace)
                Debug.Log($"[InventoryDragTrace][Server] RequestMove from={fromIndex} to={toIndex} slots={grid.Slots.Length}");

            if (fromIndex < 0 || toIndex < 0)
            {
                if (debugMoveTrace)
                    Debug.Log($"[InventoryDragTrace][Server] RejectMove reason=NegativeIndex from={fromIndex} to={toIndex}");
                return;
            }

            if (fromIndex >= grid.Slots.Length || toIndex >= grid.Slots.Length)
            {
                if (debugMoveTrace)
                    Debug.Log($"[InventoryDragTrace][Server] RejectMove reason=OutOfRange from={fromIndex} to={toIndex} max={grid.Slots.Length - 1}");
                return;
            }

            if (!grid.TryMoveSlot(fromIndex, toIndex))
            {
                if (debugMoveTrace)
                    Debug.Log($"[InventoryDragTrace][Server] RejectMove reason=GridTryMoveFailed from={fromIndex} to={toIndex}");
                return;
            }

            if (debugMoveTrace)
                Debug.Log($"[InventoryDragTrace][Server] MoveAccepted from={fromIndex} to={toIndex}");

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

            if (!grid.CanAdd(itemId, quantity, out _, durability, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy))
                return quantity;

            var remainder = grid.Add(itemId, quantity, durability, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy);

            if (remainder < quantity)
            {
                knownItems?.EnsureKnown(itemId);
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
                        ContentType = InventorySlotContentType.Empty,
                        ItemId = default,
                        Quantity = 0,
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
                    continue;
                }

                if (s.ContentType == InventorySlotContentType.Instance)
                {
                    slots[i] = new InventorySnapshot.SlotDto
                    {
                        IsEmpty = false,
                        ContentType = InventorySlotContentType.Instance,
                        ItemId = new FixedString64Bytes(s.Instance.ItemId),
                        Quantity = 1,
                        Durability = Mathf.Max(0, s.Instance.CurrentDurability),
                        MaxDurability = Mathf.Max(0, s.Instance.MaxDurability),
                        BonusStrength = s.InstanceData.BonusStrength,
                        BonusDexterity = s.InstanceData.BonusDexterity,
                        BonusIntelligence = s.InstanceData.BonusIntelligence,
                        CraftedBy = s.InstanceData.CraftedBy,
                        InstanceId = s.Instance.InstanceId,
                        RolledDamage = s.Instance.RolledDamage,
                        RolledDefence = s.Instance.RolledDefence,
                        RolledSwingSpeed = s.Instance.RolledSwingSpeed,
                        RolledMovementSpeed = s.Instance.RolledMovementSpeed
                    };
                    continue;
                }

                int maxDurability = 0;
                if (itemDatabase != null && itemDatabase.TryGet(s.Stack.ItemId, out var def) && def != null)
                    maxDurability = Mathf.Max(0, def.ResolveDurabilityMax());

                int durability = maxDurability > 0
                    ? Mathf.Clamp(s.Durability <= 0 ? maxDurability : s.Durability, 1, maxDurability)
                    : 0;

                slots[i] = new InventorySnapshot.SlotDto
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Stack,
                    ItemId = new FixedString64Bytes(s.Stack.ItemId),
                    Quantity = s.Stack.Quantity,
                    Durability = durability,
                    MaxDurability = maxDurability,
                    BonusStrength = s.InstanceData.BonusStrength,
                    BonusDexterity = s.InstanceData.BonusDexterity,
                    BonusIntelligence = s.InstanceData.BonusIntelligence,
                    CraftedBy = s.InstanceData.CraftedBy,
                    InstanceId = s.InstanceData.InstanceId,
                    RolledDamage = s.InstanceData.RolledDamage,
                    RolledDefence = s.InstanceData.RolledDefence,
                    RolledSwingSpeed = s.InstanceData.RolledSwingSpeed,
                    RolledMovementSpeed = s.InstanceData.RolledMovementSpeed
                };
            }

            return new InventorySnapshot
            {
                W = source.Width,
                H = source.Height,
                Slots = slots
            };
        }

        private static InventorySlot MakeEmptySlot()
        {
            return new InventorySlot
            {
                IsEmpty = true,
                ContentType = InventorySlotContentType.Empty,
                Stack = default,
                Instance = default,
                Durability = 0,
                InstanceData = default
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
