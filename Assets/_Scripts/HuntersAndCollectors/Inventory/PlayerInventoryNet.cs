using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Authoritative player inventory network component with owner-scoped snapshots.
    /// </summary>
    public sealed class PlayerInventoryNet : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab and assign item database.
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private KnownItemsNet knownItems;
        [SerializeField] private int width = 6;
        [SerializeField] private int height = 4;
        private InventoryGrid grid;

        /// <summary>Server authoritative inventory grid instance.</summary>
        public InventoryGrid Grid => grid;

        public override void OnNetworkSpawn()
        {
            if (IsServer) grid = new InventoryGrid(width, height, itemDatabase);
        }

        /// <summary>
        /// Sends latest inventory snapshot to owning client only.
        /// </summary>
        public void ForceSendSnapshotToOwner()
        {
            if (!IsServer || grid == null) return;
            var rpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } } };
            ReceiveInventorySnapshotClientRpc(ToSnapshot(grid), rpcParams);
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestMoveSlotServerRpc(int fromIndex, int toIndex)
        {
            if (!IsServer || grid == null) return;
            if (grid.TryMoveSlot(fromIndex, toIndex)) ForceSendSnapshotToOwner();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestSplitStackServerRpc(int index, int splitAmount)
        {
            if (!IsServer || grid == null || splitAmount <= 0) return;
            if (grid.TrySplitStack(index, splitAmount, out _)) ForceSendSnapshotToOwner();
        }

        [ClientRpc]
        private void ReceiveInventorySnapshotClientRpc(InventorySnapshot snapshot, ClientRpcParams rpcParams = default) { }

        /// <summary>
        /// Adds item stack and auto-registers as known when added quantity succeeds.
        /// </summary>
        public int AddItemServer(string itemId, int quantity)
        {
            if (!IsServer || grid == null || quantity <= 0) return quantity;
            var remainder = grid.Add(itemId, quantity);
            if (remainder < quantity) knownItems?.EnsureKnown(itemId);
            ForceSendSnapshotToOwner();
            return remainder;
        }

        private static InventorySnapshot ToSnapshot(InventoryGrid source)
        {
            var slots = new InventorySnapshot.SlotDto[source.Slots.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                var s = source.Slots[i];
                slots[i] = s.IsEmpty
                    ? new InventorySnapshot.SlotDto { IsEmpty = true, ItemId = default, Quantity = 0 }
                    : new InventorySnapshot.SlotDto { IsEmpty = false, ItemId = new FixedString64Bytes(s.Stack.ItemId), Quantity = s.Stack.Quantity };
            }
            return new InventorySnapshot { W = source.Width, H = source.Height, Slots = slots };
        }
    }
}
