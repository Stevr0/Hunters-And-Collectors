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
        // Vendor transactions may add multiple items in a single checkout.
        // We want exactly ONE snapshot sent at the end, not one per item.
        private int _serverBatchDepth = 0;
        private bool _serverBatchDirty = false;

        /// <summary>
        /// Last snapshot received on this client (owner). Useful for UI / debug.
        /// </summary>
        public InventorySnapshot LastSnapshot { get; private set; }

        /// <summary>
        /// Fired on the client whenever a new snapshot is received.
        /// UI can subscribe to this.
        /// </summary>
        public event Action<InventorySnapshot> OnSnapshotReceived;

        /// <summary>
        /// Fired whenever a new snapshot arrives on this client.
        /// </summary>
        public event Action<InventorySnapshot> OnSnapshotChanged;

        /// <summary>
        /// Starts a server-side batching scope.
        /// While batching, inventory mutations should NOT send snapshots immediately.
        /// Instead they mark the batch as dirty and we send one snapshot when the
        /// batch ends.
        ///
        /// Safe to nest (depth counter).
        /// </summary>
        public void BeginServerBatch()
        {
            if (!IsServer) return;
            _serverBatchDepth++;
        }

        /// <summary>
        /// Ends a server-side batching scope and sends ONE snapshot to the owner
        /// if anything changed during the batch.
        /// </summary>
        public void EndServerBatchAndSendSnapshotToOwner()
        {
            if (!IsServer) return;

            _serverBatchDepth = Mathf.Max(0, _serverBatchDepth - 1);

            // Only the outer-most End triggers the snapshot.
            if (_serverBatchDepth == 0 && _serverBatchDirty)
            {
                _serverBatchDirty = false;
                ForceSendSnapshotToOwner();
            }
        }

        /// <summary>
        /// End a server-side batch WITHOUT sending a snapshot.
        /// Useful when a transaction fails and you know you didn't commit anything.
        /// </summary>
        public void EndServerBatchWithoutSending()
        {
            if (!IsServer) return;

            _serverBatchDepth = Mathf.Max(0, _serverBatchDepth - 1);

            // Clear dirty when outermost scope ends.
            if (_serverBatchDepth == 0)
                _serverBatchDirty = false;
        }

        /// <summary>
        /// Marks inventory as dirty. If we are batching, it will delay the snapshot.
        /// If not batching, it will send immediately (legacy behavior).
        /// </summary>
        private void MarkDirtyAndMaybeSendSnapshot()
        {
            if (!IsServer || grid == null) return;

            if (_serverBatchDepth > 0)
            {
                // Defer snapshot until EndServerBatch...
                _serverBatchDirty = true;
                return;
            }

            // Legacy behavior: send immediately.
            ForceSendSnapshotToOwner();
        }

        /// <summary>
        /// SERVER: Returns true if any slot contains at least quantity of itemId.
        /// </summary>
        public bool ServerHasItem(string itemId, int quantity)
        {
            if (!IsServer || grid == null) return false;
            return grid.CanRemove(itemId, quantity);
        }

        /// <summary>
        /// SERVER: Removes quantity of itemId if possible (authoritative).
        /// Sends snapshot batching-aware.
        /// </summary>
        public bool ServerRemoveItem(string itemId, int quantity)
        {
            if (!IsServer || grid == null) return false;
            if (!grid.Remove(itemId, quantity)) return false;

            // Uses your batching-aware snapshot send.
            // If you didn't modify MarkDirtyAndMaybeSendSnapshot visibility,
            // you can just call ForceSendSnapshotToOwner() here instead.
            ForceSendSnapshotToOwner();
            return true;
        }

        /// <summary>
        /// SERVER: Adds quantity of itemId and returns remainder (authoritative).
        /// Uses your existing AddItemServer which is already snapshot-aware.
        /// </summary>
        public int ServerAddItem(string itemId, int quantity)
        {
            if (!IsServer) return quantity;
            return AddItemServer(itemId, quantity);
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

            ReceiveInventorySnapshotClientRpc(ToSnapshot(grid), rpcParams);
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
            // IMPORTANT:
            // This runs on the CLIENT. On host, it also runs locally (same process).
            // If you don't do anything here, your UI will never update.

            LastSnapshot = snapshot;
            OnSnapshotChanged?.Invoke(snapshot);

            Debug.Log($"[InventoryNet][CLIENT] Snapshot received. W={snapshot.W} H={snapshot.H} Slots={snapshot.Slots?.Length ?? 0} IsOwner={IsOwner} OwnerClientId={OwnerClientId}");

            // Tell any UI listeners to refresh.
            OnSnapshotReceived?.Invoke(snapshot);
        }

        public int AddItemServer(string itemId, int quantity)
        {

            if (!IsServer || grid == null || quantity <= 0) return quantity;

            var remainder = grid.Add(itemId, quantity);

            if (remainder < quantity)
                knownItems?.EnsureKnown(itemId);

            // NEW (batch-aware):
            MarkDirtyAndMaybeSendSnapshot();

            return remainder;
        }

        private static InventorySnapshot ToSnapshot(InventoryGrid source)
        {
            var slots = new InventorySnapshot.SlotDto[source.Slots.Length];

            for (var i = 0; i < slots.Length; i++)
            {
                var s = source.Slots[i];

                slots[i] = s.IsEmpty
                    ? new InventorySnapshot.SlotDto { IsEmpty = true }
                    : new InventorySnapshot.SlotDto
                    {
                        IsEmpty = false,
                        ItemId = new FixedString64Bytes(s.Stack.ItemId),
                        Quantity = s.Stack.Quantity
                    };
            }

            return new InventorySnapshot
            {
                W = source.Width,
                H = source.Height,
                Slots = slots
            };


        }
    }
}
