using System;
using System.Collections.Generic;
using HuntersAndCollectors.Building;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Persistence;
using HuntersAndCollectors.Players;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Storage
{
    /// <summary>
    /// StorageNet
    /// --------------------------------------------------------------------
    /// Server-authoritative placed chest container.
    ///
    /// Responsibilities:
    /// - Own authoritative chest inventory grid.
    /// - Replicate chest snapshots to clients.
    /// - Process store/take requests from interacting players.
    /// - Export/apply shard persistence using the shared inventory grid save schema.
    /// - Register runtime placed storages by stable placed-object persistence id.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class StorageNet : NetworkBehaviour
    {
        [Header("Definition")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Chest Size")]
        [Min(1)]
        [SerializeField] private int width = 4;

        [Min(1)]
        [SerializeField] private int height = 4;

        private InventoryGrid chestGrid;
        private PlacedBuildPiece placedBuildPiece;

        /// <summary>
        /// Server-only authoritative grid reference.
        /// </summary>
        public InventoryGrid Grid => chestGrid;

        public string PersistentId => placedBuildPiece != null ? placedBuildPiece.PersistentId : string.Empty;
        public string BuildPieceId => placedBuildPiece != null ? placedBuildPiece.SourceItemId : string.Empty;

        /// <summary>
        /// Most recent chest snapshot on this client.
        /// </summary>
        public InventorySnapshot LastSnapshot { get; private set; }

        /// <summary>
        /// Fired whenever a new chest snapshot arrives on this client.
        /// </summary>
        public event Action<InventorySnapshot> OnChestSnapshotChanged;

        private void Awake()
        {
            if (placedBuildPiece == null)
                placedBuildPiece = GetComponent<PlacedBuildPiece>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            if (placedBuildPiece == null)
                placedBuildPiece = GetComponent<PlacedBuildPiece>();

            EnsureServerGrid(Mathf.Max(1, width), Mathf.Max(1, height));
            PlacedStorageRegistry.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            PlacedStorageRegistry.Unregister(this);
        }

        public override void OnDestroy()
        {
            PlacedStorageRegistry.Unregister(this);
            base.OnDestroy();
        }

        /// <summary>
        /// Client asks server to open this chest and receive latest snapshot.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestOpenChestServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer || chestGrid == null)
                return;

            ulong requesterClientId = rpcParams.Receive.SenderClientId;
            Debug.Log($"[ChestContainerNet][SERVER] Chest opened persistentId={PersistentId} by client={requesterClientId}", this);

            SendSnapshotToClient(requesterClientId);
        }

        /// <summary>
        /// Client asks server to move items from player inventory into chest.
        /// First-pass behavior transfers up to requested quantity from one player slot.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestStoreFromPlayerServerRpc(int playerSlotIndex, int quantity, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || chestGrid == null)
                return;

            if (quantity <= 0)
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Store denied: quantity must be > 0.", this);
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (!TryResolveSenderInventory(senderClientId, out PlayerInventoryNet playerInventory))
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Store denied: player inventory not found.", this);
                return;
            }

            if (!playerInventory.ServerTryGetSlotItem(playerSlotIndex, out string itemId, out int slotQuantity, out int durability, out ItemInstanceData instanceData))
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Store denied: invalid or empty player slot.", this);
                return;
            }

            int transferQuantity = Mathf.Clamp(quantity, 1, slotQuantity);

            if (!chestGrid.CanAdd(itemId, transferQuantity, out _, durability, instanceData.BonusStrength, instanceData.BonusDexterity, instanceData.BonusIntelligence, instanceData.CraftedBy))
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Store denied: chest full.", this);
                return;
            }

            int remainingAfterPlayer = slotQuantity - transferQuantity;
            bool playerMutated = remainingAfterPlayer <= 0
                ? playerInventory.ServerTrySetSlot(playerSlotIndex, string.Empty, 0, 0)
                : playerInventory.ServerTrySetSlot(
                    playerSlotIndex,
                    itemId,
                    remainingAfterPlayer,
                    durability,
                    instanceData.BonusStrength,
                    instanceData.BonusDexterity,
                    instanceData.BonusIntelligence,
                    instanceData.CraftedBy);

            if (!playerMutated)
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Store denied: failed to update player inventory.", this);
                return;
            }

            int chestRemainder = chestGrid.Add(
                itemId,
                transferQuantity,
                durability,
                instanceData.BonusStrength,
                instanceData.BonusDexterity,
                instanceData.BonusIntelligence,
                instanceData.CraftedBy);

            int moved = transferQuantity - chestRemainder;
            if (moved <= 0)
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Store denied: chest add failed.", this);
                return;
            }

            Debug.Log($"[ChestContainerNet][SERVER] Store success persistentId={PersistentId} itemId={itemId} qty={moved}", this);

            playerInventory.ForceSendSnapshotToOwner();
            BroadcastChestSnapshot();
            SaveManager.NotifyShardStateChanged();
        }

        /// <summary>
        /// Client asks server to move items from chest into player inventory.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestTakeToPlayerServerRpc(int chestSlotIndex, int quantity, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || chestGrid == null)
                return;

            if (quantity <= 0)
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Take denied: quantity must be > 0.", this);
                return;
            }

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (!TryResolveSenderInventory(senderClientId, out PlayerInventoryNet playerInventory))
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Take denied: player inventory not found.", this);
                return;
            }

            if (!ServerTryGetChestSlot(chestSlotIndex, out InventorySlot chestSlot))
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Take denied: invalid or empty chest slot.", this);
                return;
            }

            int transferQuantity = Mathf.Clamp(quantity, 1, chestSlot.Stack.Quantity);
            string itemId = chestSlot.Stack.ItemId;

            if (!playerInventory.Grid.CanAdd(
                    itemId,
                    transferQuantity,
                    out _,
                    chestSlot.Durability,
                    chestSlot.InstanceData.BonusStrength,
                    chestSlot.InstanceData.BonusDexterity,
                    chestSlot.InstanceData.BonusIntelligence,
                    chestSlot.InstanceData.CraftedBy))
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Take denied: player inventory full.", this);
                return;
            }

            int remainingInChestSlot = chestSlot.Stack.Quantity - transferQuantity;
            if (remainingInChestSlot <= 0)
            {
                chestGrid.Slots[chestSlotIndex] = new InventorySlot { IsEmpty = true, ContentType = InventorySlotContentType.Empty };
            }
            else
            {
                chestSlot.Stack.Quantity = remainingInChestSlot;
                if (chestSlot.ContentType == InventorySlotContentType.Stack && chestSlot.Stack.Quantity > 1)
                    chestSlot.InstanceData = default;
                chestGrid.Slots[chestSlotIndex] = chestSlot;
            }

            int playerRemainder = playerInventory.ServerAddItem(
                itemId,
                transferQuantity,
                chestSlot.Durability,
                chestSlot.InstanceData.BonusStrength,
                chestSlot.InstanceData.BonusDexterity,
                chestSlot.InstanceData.BonusIntelligence,
                chestSlot.InstanceData.CraftedBy);

            int moved = transferQuantity - playerRemainder;
            if (moved <= 0)
            {
                Debug.LogWarning("[ChestContainerNet][SERVER] Take denied: failed to add item to player.", this);
                return;
            }

            Debug.Log($"[ChestContainerNet][SERVER] Take success persistentId={PersistentId} itemId={itemId} qty={moved}", this);

            playerInventory.ForceSendSnapshotToOwner();
            BroadcastChestSnapshot();
            SaveManager.NotifyShardStateChanged();
        }

        /// <summary>
        /// SERVER: export this chest's authoritative inventory for shard persistence.
        /// The shard save owns placed storage progression, never player save.
        /// </summary>
        public PlacedStorageChestSaveData ServerExportSaveData()
        {
            int resolvedWidth = chestGrid != null ? chestGrid.Width : Mathf.Max(1, width);
            int resolvedHeight = chestGrid != null ? chestGrid.Height : Mathf.Max(1, height);
            var data = new PlacedStorageChestSaveData
            {
                persistentId = PersistentId,
                buildPieceId = BuildPieceId,
                position = new Vector3SaveData { x = transform.position.x, y = transform.position.y, z = transform.position.z },
                rotation = new QuaternionSaveData { x = transform.rotation.x, y = transform.rotation.y, z = transform.rotation.z, w = transform.rotation.w },
                chest = new InventoryGridSaveData
                {
                    w = resolvedWidth,
                    h = resolvedHeight,
                    slots = new List<InventorySlotSaveData>(resolvedWidth * resolvedHeight)
                }
            };

            if (chestGrid == null)
                return data;

            for (int i = 0; i < chestGrid.Slots.Length; i++)
            {
                InventorySlot slot = chestGrid.Slots[i];
                if (slot.IsEmpty)
                {
                    data.chest.slots.Add(null);
                    continue;
                }

                if (slot.ContentType == InventorySlotContentType.Instance)
                {
                    data.chest.slots.Add(new InventorySlotSaveData
                    {
                        kind = "Instance",
                        id = slot.Instance.ItemId,
                        q = 1,
                        instanceId = slot.Instance.InstanceId,
                        rolledDamage = slot.Instance.RolledDamage,
                        rolledDefence = slot.Instance.RolledDefence,
                        rolledSwingSpeed = slot.Instance.RolledSwingSpeed,
                        rolledMovementSpeed = slot.Instance.RolledMovementSpeed,
                        maxDurability = slot.Instance.MaxDurability,
                        currentDurability = slot.Instance.CurrentDurability,
                        bonusStrength = slot.InstanceData.BonusStrength,
                        bonusDexterity = slot.InstanceData.BonusDexterity,
                        bonusIntelligence = slot.InstanceData.BonusIntelligence,
                        craftedBy = slot.InstanceData.CraftedBy.ToString()
                    });
                    continue;
                }

                data.chest.slots.Add(new InventorySlotSaveData
                {
                    kind = "Stack",
                    id = slot.Stack.ItemId,
                    q = slot.Stack.Quantity,
                    bonusStrength = slot.InstanceData.BonusStrength,
                    bonusDexterity = slot.InstanceData.BonusDexterity,
                    bonusIntelligence = slot.InstanceData.BonusIntelligence,
                    craftedBy = slot.InstanceData.CraftedBy.ToString(),
                    maxDurability = slot.Durability,
                    currentDurability = slot.Durability
                });
            }

            return data;
        }

        /// <summary>
        /// SERVER: apply saved inventory data after the chest object exists on reload.
        /// Missing/invalid rows are ignored so older shard saves load safely.
        /// </summary>
        public void ServerApplySaveData(PlacedStorageChestSaveData data)
        {
            if (!IsServer)
                return;

            int targetWidth = data?.chest != null ? Mathf.Max(1, data.chest.w) : Mathf.Max(1, width);
            int targetHeight = data?.chest != null ? Mathf.Max(1, data.chest.h) : Mathf.Max(1, height);
            EnsureServerGrid(targetWidth, targetHeight);

            if (data == null || data.chest == null || data.chest.slots == null)
            {
                BroadcastChestSnapshot();
                return;
            }

            chestGrid = new InventoryGrid(targetWidth, targetHeight, itemDatabase);
            int copyCount = Mathf.Min(chestGrid.Slots.Length, data.chest.slots.Count);
            for (int i = 0; i < copyCount; i++)
            {
                InventorySlotSaveData saveSlot = data.chest.slots[i];
                if (saveSlot == null || string.IsNullOrWhiteSpace(saveSlot.id))
                    continue;

                if (itemDatabase == null || !itemDatabase.TryGet(saveSlot.id, out ItemDef def) || def == null)
                    continue;

                bool asInstance = string.Equals(saveSlot.kind, "Instance", StringComparison.OrdinalIgnoreCase) || def.UsesItemInstance;
                if (asInstance)
                {
                    int maxDurability = saveSlot.maxDurability > 0 ? saveSlot.maxDurability : def.ResolveDurabilityMax();
                    int currentDurability = saveSlot.currentDurability > 0 ? saveSlot.currentDurability : maxDurability;

                    ItemInstance instance = new ItemInstance
                    {
                        InstanceId = saveSlot.instanceId,
                        ItemId = def.ItemId,
                        RolledDamage = saveSlot.rolledDamage > 0f ? saveSlot.rolledDamage : def.ResolveDamageMin(),
                        RolledDefence = saveSlot.rolledDefence > 0f ? saveSlot.rolledDefence : def.ResolveDefenceMin(),
                        RolledSwingSpeed = saveSlot.rolledSwingSpeed > 0f ? saveSlot.rolledSwingSpeed : def.ResolveSwingSpeedMin(),
                        RolledMovementSpeed = saveSlot.rolledMovementSpeed > 0f ? saveSlot.rolledMovementSpeed : def.ResolveMovementSpeedMin(),
                        MaxDurability = Mathf.Max(0, maxDurability),
                        CurrentDurability = maxDurability > 0 ? Mathf.Clamp(currentDurability, 1, Mathf.Max(1, maxDurability)) : 0
                    };

                    ItemInstanceData instanceData = new ItemInstanceData
                    {
                        BonusStrength = saveSlot.bonusStrength,
                        BonusDexterity = saveSlot.bonusDexterity,
                        BonusIntelligence = saveSlot.bonusIntelligence,
                        CraftedBy = new FixedString64Bytes(saveSlot.craftedBy ?? string.Empty),
                        InstanceId = instance.InstanceId,
                        RolledDamage = instance.RolledDamage,
                        RolledDefence = instance.RolledDefence,
                        RolledSwingSpeed = instance.RolledSwingSpeed,
                        RolledMovementSpeed = instance.RolledMovementSpeed,
                        MaxDurability = instance.MaxDurability,
                        CurrentDurability = instance.CurrentDurability
                    };

                    chestGrid.Slots[i] = new InventorySlot
                    {
                        IsEmpty = false,
                        ContentType = InventorySlotContentType.Instance,
                        Stack = new ItemStack { ItemId = def.ItemId, Quantity = 1 },
                        Instance = instance,
                        Durability = instance.CurrentDurability,
                        InstanceData = instanceData
                    };
                    continue;
                }

                int maxStack = Mathf.Max(1, def.MaxStack);
                int qty = Mathf.Clamp(Mathf.Max(1, saveSlot.q), 1, maxStack);
                chestGrid.Slots[i] = new InventorySlot
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Stack,
                    Stack = new ItemStack { ItemId = def.ItemId, Quantity = qty },
                    Instance = default,
                    Durability = 0,
                    InstanceData = default
                };
            }

            BroadcastChestSnapshot();
        }

        public int CountNonEmptySlots()
        {
            if (chestGrid == null)
                return 0;

            int count = 0;
            for (int i = 0; i < chestGrid.Slots.Length; i++)
            {
                if (!chestGrid.Slots[i].IsEmpty)
                    count++;
            }

            return count;
        }

        private bool ServerTryGetChestSlot(int slotIndex, out InventorySlot slot)
        {
            slot = default;

            if (!IsServer || chestGrid == null)
                return false;

            if (slotIndex < 0 || slotIndex >= chestGrid.Slots.Length)
                return false;

            slot = chestGrid.Slots[slotIndex];
            return !slot.IsEmpty;
        }

        private bool TryResolveSenderInventory(ulong senderClientId, out PlayerInventoryNet playerInventory)
        {
            playerInventory = null;

            if (NetworkManager == null)
                return false;

            if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient))
                return false;

            if (senderClient.PlayerObject == null)
                return false;

            PlayerNetworkRoot root = senderClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            if (root == null || root.Inventory == null)
                return false;

            playerInventory = root.Inventory;
            return true;
        }

        private void SendSnapshotToClient(ulong targetClientId)
        {
            if (!IsServer || chestGrid == null)
                return;

            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } }
            };

            ReceiveChestSnapshotClientRpc(ToSnapshot(chestGrid, itemDatabase), rpcParams);
        }

        private void BroadcastChestSnapshot()
        {
            if (!IsServer || chestGrid == null)
                return;

            ReceiveChestSnapshotClientRpc(ToSnapshot(chestGrid, itemDatabase));
        }

        private void EnsureServerGrid(int targetWidth, int targetHeight)
        {
            int safeWidth = Mathf.Max(1, targetWidth);
            int safeHeight = Mathf.Max(1, targetHeight);

            if (chestGrid != null && chestGrid.Width == safeWidth && chestGrid.Height == safeHeight)
                return;

            chestGrid = new InventoryGrid(safeWidth, safeHeight, itemDatabase);
        }

        [ClientRpc]
        private void ReceiveChestSnapshotClientRpc(InventorySnapshot snapshot, ClientRpcParams rpcParams = default)
        {
            LastSnapshot = snapshot;
            OnChestSnapshotChanged?.Invoke(snapshot);
        }

        private static InventorySnapshot ToSnapshot(InventoryGrid source, ItemDatabase database)
        {
            InventorySnapshot.SlotDto[] slots = new InventorySnapshot.SlotDto[source.Slots.Length];

            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlot slot = source.Slots[i];

                if (slot.IsEmpty)
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

                if (slot.ContentType == InventorySlotContentType.Instance)
                {
                    slots[i] = new InventorySnapshot.SlotDto
                    {
                        IsEmpty = false,
                        ContentType = InventorySlotContentType.Instance,
                        ItemId = new FixedString64Bytes(slot.Instance.ItemId),
                        Quantity = 1,
                        Durability = Mathf.Max(0, slot.Instance.CurrentDurability),
                        MaxDurability = Mathf.Max(0, slot.Instance.MaxDurability),
                        BonusStrength = slot.InstanceData.BonusStrength,
                        BonusDexterity = slot.InstanceData.BonusDexterity,
                        BonusIntelligence = slot.InstanceData.BonusIntelligence,
                        CraftedBy = slot.InstanceData.CraftedBy,
                        InstanceId = slot.Instance.InstanceId,
                        RolledDamage = slot.Instance.RolledDamage,
                        RolledDefence = slot.Instance.RolledDefence,
                        RolledSwingSpeed = slot.Instance.RolledSwingSpeed,
                        RolledMovementSpeed = slot.Instance.RolledMovementSpeed
                    };
                    continue;
                }

                int maxDurability = 0;
                if (database != null && database.TryGet(slot.Stack.ItemId, out ItemDef def) && def != null)
                    maxDurability = Mathf.Max(0, def.ResolveDurabilityMax());

                int durability = maxDurability > 0
                    ? Mathf.Clamp(slot.Durability <= 0 ? maxDurability : slot.Durability, 1, maxDurability)
                    : 0;

                slots[i] = new InventorySnapshot.SlotDto
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Stack,
                    ItemId = new FixedString64Bytes(slot.Stack.ItemId),
                    Quantity = slot.Stack.Quantity,
                    Durability = durability,
                    MaxDurability = maxDurability,
                    BonusStrength = slot.InstanceData.BonusStrength,
                    BonusDexterity = slot.InstanceData.BonusDexterity,
                    BonusIntelligence = slot.InstanceData.BonusIntelligence,
                    CraftedBy = slot.InstanceData.CraftedBy,
                    InstanceId = 0,
                    RolledDamage = 0f,
                    RolledDefence = 0f,
                    RolledSwingSpeed = 0f,
                    RolledMovementSpeed = 0f
                };
            }

            return new InventorySnapshot
            {
                W = source.Width,
                H = source.Height,
                Slots = slots
            };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (width < 1)
                width = 1;

            if (height < 1)
                height = 1;
        }
#endif
    }

    /// <summary>
    /// Runtime registry of server-authoritative placed storage chests.
    /// Save/load matches shard chest records through the placed build persistent id.
    /// </summary>
    public static class PlacedStorageRegistry
    {
        private static readonly HashSet<StorageNet> Active = new();

        public static void Register(StorageNet storage)
        {
            if (storage == null)
                return;

            Active.Add(storage);
        }

        public static void Unregister(StorageNet storage)
        {
            if (storage == null)
                return;

            Active.Remove(storage);
        }

        public static List<StorageNet> Snapshot()
        {
            var result = new List<StorageNet>(Active.Count);
            foreach (StorageNet storage in Active)
            {
                if (storage != null)
                    result.Add(storage);
            }

            return result;
        }
    }
}
