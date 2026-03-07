using System;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Storage
{
    /// <summary>
    /// ChestContainerNet
    /// --------------------------------------------------------------------
    /// First-pass server-authoritative chest container.
    ///
    /// Responsibilities:
    /// - Own authoritative chest inventory grid.
    /// - Replicate chest snapshots to clients.
    /// - Process store/take requests from interacting players.
    ///
    /// Scope intentionally kept small:
    /// - No ownership/permissions/locking.
    /// - No persistence yet.
    /// - No complex viewer-tracking.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChestContainerNet : NetworkBehaviour
    {
        [Header("Definition")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Chest Size")]
        [Min(1)]
        [SerializeField] private int width = 4;

        [Min(1)]
        [SerializeField] private int height = 4;

        private InventoryGrid chestGrid;

        /// <summary>
        /// Server-only authoritative grid reference.
        /// </summary>
        public InventoryGrid Grid => chestGrid;

        /// <summary>
        /// Most recent chest snapshot on this client.
        /// </summary>
        public InventorySnapshot LastSnapshot { get; private set; }

        /// <summary>
        /// Fired whenever a new chest snapshot arrives on this client.
        /// </summary>
        public event Action<InventorySnapshot> OnChestSnapshotChanged;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            chestGrid = new InventoryGrid(Mathf.Max(1, width), Mathf.Max(1, height), itemDatabase);
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
            Debug.Log($"[ChestContainerNet][SERVER] Chest opened by client={requesterClientId}", this);

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

            Debug.Log($"[ChestContainerNet][SERVER] Store success itemId={itemId} qty={moved}", this);

            playerInventory.ForceSendSnapshotToOwner();
            BroadcastChestSnapshot();
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
                chestGrid.Slots[chestSlotIndex] = new InventorySlot { IsEmpty = true };
            }
            else
            {
                chestSlot.Stack.Quantity = remainingInChestSlot;
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

            Debug.Log($"[ChestContainerNet][SERVER] Take success itemId={itemId} qty={moved}", this);

            playerInventory.ForceSendSnapshotToOwner();
            BroadcastChestSnapshot();
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
                if (database != null && database.TryGet(slot.Stack.ItemId, out ItemDef def) && def != null)
                    maxDurability = Mathf.Max(0, def.MaxDurability);

                int durability = maxDurability > 0
                    ? Mathf.Clamp(slot.Durability <= 0 ? maxDurability : slot.Durability, 1, maxDurability)
                    : 0;

                slots[i] = new InventorySnapshot.SlotDto
                {
                    IsEmpty = false,
                    ItemId = new FixedString64Bytes(slot.Stack.ItemId),
                    Quantity = slot.Stack.Quantity,
                    Durability = durability,
                    MaxDurability = maxDurability,
                    BonusStrength = slot.InstanceData.BonusStrength,
                    BonusDexterity = slot.InstanceData.BonusDexterity,
                    BonusIntelligence = slot.InstanceData.BonusIntelligence,
                    CraftedBy = slot.InstanceData.CraftedBy
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
}
