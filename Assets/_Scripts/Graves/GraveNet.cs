using System;
using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Persistence;
using HuntersAndCollectors.Players;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Graves
{
    /// <summary>
    /// Server-authoritative world grave container created on player death.
    /// Grave contents live in shard save, not player save.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class GraveNet : NetworkBehaviour
    {
        private const int GraveWidth = 6;
        private const int GraveHeight = 8;

        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private string persistentId;
        [SerializeField] private string ownerPlayerKey;

        private InventoryGrid graveGrid;

        public string PersistentId => persistentId;
        public string OwnerPlayerKey => ownerPlayerKey;
        public InventoryGrid Grid => graveGrid;

        private void Awake()
        {
            EnsureItemDatabase();
        }

        public void ServerInitializeNew(string playerKey)
        {
            if (!HasServerAuthorityContext())
                return;

            EnsureItemDatabase();
            persistentId = string.IsNullOrWhiteSpace(persistentId) ? GeneratePersistentId() : persistentId.Trim();
            ownerPlayerKey = playerKey ?? string.Empty;
            graveGrid = new InventoryGrid(GraveWidth, GraveHeight, itemDatabase);
        }

        public void ServerApplySaveData(GraveSaveData saveData)
        {
            if (!HasServerAuthorityContext())
                return;

            EnsureItemDatabase();

            persistentId = string.IsNullOrWhiteSpace(saveData?.persistentId) ? GeneratePersistentId() : saveData.persistentId.Trim();
            ownerPlayerKey = saveData?.ownerPlayerKey ?? string.Empty;
            graveGrid = new InventoryGrid(GraveWidth, GraveHeight, itemDatabase);

            if (saveData?.inventory?.slots == null)
                return;

            int copyCount = Mathf.Min(graveGrid.Slots.Length, saveData.inventory.slots.Count);
            for (int i = 0; i < copyCount; i++)
            {
                InventorySlotSaveData slot = saveData.inventory.slots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.id))
                    continue;

                if (itemDatabase == null || !itemDatabase.TryGet(slot.id, out ItemDef def) || def == null)
                    continue;

                bool asInstance = string.Equals(slot.kind, "Instance", StringComparison.OrdinalIgnoreCase) || def.UsesItemInstance;
                if (asInstance)
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
                        MaxDurability = Mathf.Max(0, maxDurability),
                        CurrentDurability = maxDurability > 0 ? Mathf.Clamp(currentDurability, 1, Mathf.Max(1, maxDurability)) : 0
                    };

                    ItemInstanceData instanceData = new ItemInstanceData
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

                    graveGrid.Slots[i] = new InventorySlot
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

                int qty = Mathf.Clamp(Mathf.Max(1, slot.q), 1, Mathf.Max(1, def.MaxStack));
                graveGrid.Slots[i] = new InventorySlot
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

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            EnsureItemDatabase();
            if (string.IsNullOrWhiteSpace(persistentId))
                persistentId = GeneratePersistentId();
            if (graveGrid == null)
                graveGrid = new InventoryGrid(GraveWidth, GraveHeight, itemDatabase);

            GraveRegistry.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            GraveRegistry.Unregister(this);
        }

        public override void OnDestroy()
        {
            GraveRegistry.Unregister(this);
            base.OnDestroy();
        }

        public GraveSaveData ServerExportSaveData()
        {
            EnsureItemDatabase();
            var data = new GraveSaveData
            {
                persistentId = persistentId,
                ownerPlayerKey = ownerPlayerKey,
                sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : string.Empty,
                position = new Vector3SaveData { x = transform.position.x, y = transform.position.y, z = transform.position.z },
                rotation = new QuaternionSaveData { x = transform.rotation.x, y = transform.rotation.y, z = transform.rotation.z, w = transform.rotation.w },
                inventory = new InventoryGridSaveData
                {
                    w = GraveWidth,
                    h = GraveHeight,
                    slots = new List<InventorySlotSaveData>(GraveWidth * GraveHeight)
                }
            };

            if (graveGrid == null)
                return data;

            for (int i = 0; i < graveGrid.Slots.Length; i++)
            {
                InventorySlot slot = graveGrid.Slots[i];
                if (slot.IsEmpty)
                {
                    data.inventory.slots.Add(null);
                    continue;
                }

                if (slot.ContentType == InventorySlotContentType.Instance)
                {
                    data.inventory.slots.Add(new InventorySlotSaveData
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

                data.inventory.slots.Add(new InventorySlotSaveData
                {
                    kind = "Stack",
                    id = slot.Stack.ItemId,
                    q = slot.Stack.Quantity
                });
            }

            return data;
        }

        public bool ServerTryAddSlotPayload(InventorySlot slot, out int movedQuantity)
        {
            movedQuantity = 0;

            if (!IsServer || graveGrid == null || slot.IsEmpty)
                return false;

            if (slot.ContentType == InventorySlotContentType.Instance)
            {
                if (!graveGrid.TryAddInstance(slot.Instance, slot.InstanceData))
                    return false;

                movedQuantity = 1;
                return true;
            }

            int originalQuantity = Mathf.Max(0, slot.Stack.Quantity);
            if (originalQuantity <= 0)
                return false;

            int remainder = graveGrid.Add(
                slot.Stack.ItemId,
                originalQuantity,
                slot.Durability,
                slot.InstanceData.BonusStrength,
                slot.InstanceData.BonusDexterity,
                slot.InstanceData.BonusIntelligence,
                slot.InstanceData.CraftedBy);

            movedQuantity = originalQuantity - remainder;
            return movedQuantity > 0;
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestRecoverAllServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer || graveGrid == null)
                return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (!TryResolveSender(senderClientId, out PlayerNetworkRoot playerRoot))
                return;

            if (!string.Equals(playerRoot.PlayerKey, ownerPlayerKey, StringComparison.Ordinal))
                return;

            ServerRecoverAllToPlayer(playerRoot);
        }

        public void ServerRecoverAllToPlayer(PlayerNetworkRoot playerRoot)
        {
            if (!IsServer || playerRoot == null || playerRoot.Inventory == null || graveGrid == null)
                return;

            Debug.Log($"[Grave] Recovery start id={persistentId} owner={ownerPlayerKey}", this);

            PlayerInventoryNet inventory = playerRoot.Inventory;
            inventory.BeginServerBatch();
            try
            {
                for (int i = 0; i < graveGrid.Slots.Length; i++)
                {
                    InventorySlot slot = graveGrid.Slots[i];
                    if (slot.IsEmpty)
                        continue;

                    if (slot.ContentType == InventorySlotContentType.Instance)
                    {
                        if (!inventory.ServerTryAddItemInstance(slot.Instance, slot.InstanceData))
                            continue;

                        graveGrid.Slots[i] = MakeEmptySlot();
                        Debug.Log($"[Grave] Recovery moved itemId={slot.Instance.ItemId} qty=1", this);
                        continue;
                    }

                    int originalQuantity = Mathf.Max(0, slot.Stack.Quantity);
                    int remainder = inventory.ServerAddItem(
                        slot.Stack.ItemId,
                        originalQuantity,
                        slot.Durability,
                        slot.InstanceData.BonusStrength,
                        slot.InstanceData.BonusDexterity,
                        slot.InstanceData.BonusIntelligence,
                        slot.InstanceData.CraftedBy);

                    int moved = originalQuantity - remainder;
                    if (moved <= 0)
                        continue;

                    Debug.Log($"[Grave] Recovery moved itemId={slot.Stack.ItemId} qty={moved}", this);

                    if (remainder <= 0)
                    {
                        graveGrid.Slots[i] = MakeEmptySlot();
                    }
                    else
                    {
                        slot.Stack.Quantity = remainder;
                        graveGrid.Slots[i] = slot;
                    }
                }
            }
            finally
            {
                inventory.EndServerBatchAndSendSnapshotToOwner();
            }

            if (IsEmpty())
            {
                Debug.Log("[Grave] Recovery complete grave emptied/despawned", this);
                SaveManager.NotifyShardStateChanged();
                if (NetworkObject != null && NetworkObject.IsSpawned)
                    NetworkObject.Despawn(destroy: true);
                return;
            }

            Debug.Log($"[Grave] Recovery partial itemsRemaining={CountNonEmptySlots()}", this);
            SaveManager.NotifyShardStateChanged();
        }

        public int CountNonEmptySlots()
        {
            if (graveGrid == null)
                return 0;

            int count = 0;
            for (int i = 0; i < graveGrid.Slots.Length; i++)
            {
                if (!graveGrid.Slots[i].IsEmpty)
                    count++;
            }

            return count;
        }

        public bool IsEmpty() => CountNonEmptySlots() == 0;

        private bool TryResolveSender(ulong senderClientId, out PlayerNetworkRoot playerRoot)
        {
            playerRoot = null;

            if (NetworkManager == null)
                return false;

            if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient senderClient))
                return false;

            if (senderClient.PlayerObject == null)
                return false;

            playerRoot = senderClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            return playerRoot != null;
        }

        private void EnsureItemDatabase()
        {
            if (itemDatabase != null)
                return;

            ItemDatabase[] databases = Resources.FindObjectsOfTypeAll<ItemDatabase>();
            if (databases != null && databases.Length > 0)
                itemDatabase = databases[0];
        }

        private bool HasServerAuthorityContext()
        {
            return IsServer || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
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

        private static string GeneratePersistentId()
        {
            return $"GRAVE_{Guid.NewGuid():N}";
        }
    }

    public static class GraveRegistry
    {
        private static readonly HashSet<GraveNet> Active = new();

        public static void Register(GraveNet grave)
        {
            if (grave != null)
                Active.Add(grave);
        }

        public static void Unregister(GraveNet grave)
        {
            if (grave != null)
                Active.Remove(grave);
        }

        public static List<GraveNet> Snapshot()
        {
            var result = new List<GraveNet>(Active.Count);
            foreach (GraveNet grave in Active)
            {
                if (grave != null)
                    result.Add(grave);
            }

            return result;
        }
    }
}

