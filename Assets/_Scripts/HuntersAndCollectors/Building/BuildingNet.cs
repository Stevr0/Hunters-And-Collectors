using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// BuildingNet
    /// --------------------------------------------------------------------
    /// Minimal server-authoritative item placement component.
    ///
    /// Unified item model rule:
    /// - Placeable structures are regular ItemDef entries.
    /// - Placement consumes one inventory item on the server.
    /// - A runtime world structure is then spawned as a NetworkObject.
    ///
    /// This intentionally remains first-pass and does NOT include:
    /// - snapping/support systems
    /// - build permissions
    /// - repair/demolition/refunds
    /// - client prediction
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BuildingNet : NetworkBehaviour
    {
        [Header("Definitions")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Validation")]
        [Min(0f)]
        [SerializeField] private float maxPlaceDistance = 6f;

        [SerializeField] private LayerMask placementBlockerMask = ~0;

        [Min(0.01f)]
        [SerializeField] private float overlapCheckRadius = 0.25f;

        [Header("Dependencies")]
        [SerializeField] private PlayerInventoryNet playerInventory;

        // Reused non-alloc buffer for simple overlap checks.
        private static readonly Collider[] OverlapResults = new Collider[16];

        private struct ConsumedItem
        {
            public string ItemId;
            public int Durability;
            public ItemInstanceData InstanceData;
            public bool Valid;
        }

        private void Awake()
        {
            if (playerInventory == null)
                playerInventory = GetComponent<PlayerInventoryNet>();
        }

        /// <summary>
        /// Owner-client helper for item placement requests.
        /// Clients request only; server validates and executes authoritative placement.
        /// </summary>
        public void RequestPlaceItem(string itemId, Vector3 worldPos, float rotY)
        {
            if (!IsOwner)
                return;

            if (string.IsNullOrWhiteSpace(itemId))
                return;

            RequestPlaceItemServerRpc(itemId, worldPos, rotY);
        }

        /// <summary>
        /// Compatibility wrapper for older callers still using build-piece naming.
        /// </summary>
        public void RequestPlaceBuildPiece(string buildPieceId, Vector3 worldPos, float rotY)
        {
            RequestPlaceItem(buildPieceId, worldPos, rotY);
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestPlaceItemServerRpc(string itemId, Vector3 worldPos, float rotY)
        {
            Debug.Log($"[BuildingNet][SERVER] Place request received: itemId={itemId} pos={worldPos} rotY={rotY}", this);

            if (!TryResolvePlaceableItem(itemId, out ItemDef itemDef))
                return;

            if (!ValidateInventoryHasItem(itemId))
                return;

            if (!ValidateDistance(worldPos))
                return;

            if (!ValidateOverlap(worldPos))
                return;

            if (!ServerTryConsumeOneItem(itemId, out ConsumedItem consumed))
                return;

            if (!SpawnPlacedStructure(itemDef, itemId, worldPos, rotY))
            {
                ServerRollbackConsumedItem(consumed);
                return;
            }

            Debug.Log($"[BuildingNet][SERVER] Placement succeeded: itemId={itemId} pos={worldPos}", this);
        }

        private bool TryResolvePlaceableItem(string itemId, out ItemDef itemDef)
        {
            itemDef = null;

            if (itemDatabase == null)
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: item database missing.", this);
                return false;
            }

            if (!itemDatabase.TryGet(itemId, out itemDef) || itemDef == null)
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: invalid itemId.", this);
                return false;
            }

            if (!itemDef.IsPlaceable)
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: item is not placeable.", this);
                return false;
            }

            if (itemDef.PlaceablePrefab == null)
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: placeable prefab missing.", this);
                return false;
            }

            return true;
        }

        private bool ValidateInventoryHasItem(string itemId)
        {
            if (playerInventory == null)
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: player inventory missing.", this);
                return false;
            }

            if (playerInventory.ServerHasItem(itemId, 1))
                return true;

            Debug.LogWarning("[BuildingNet][SERVER] Placement denied: item not found in inventory.", this);
            return false;
        }

        private bool ValidateDistance(Vector3 worldPos)
        {
            float safeMaxDistance = Mathf.Max(0f, maxPlaceDistance);
            float distance = Vector3.Distance(transform.position, worldPos);

            if (distance <= safeMaxDistance)
                return true;

            Debug.LogWarning($"[BuildingNet][SERVER] Placement denied: out of range (distance={distance:F2}, max={safeMaxDistance:F2}).", this);
            return false;
        }

        private bool ValidateOverlap(Vector3 worldPos)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                worldPos,
                Mathf.Max(0.01f, overlapCheckRadius),
                OverlapResults,
                placementBlockerMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = OverlapResults[i];
                if (hit == null)
                    continue;

                // Ignore colliders that belong to this player object hierarchy.
                if (hit.transform.IsChildOf(transform))
                    continue;

                Debug.LogWarning($"[BuildingNet][SERVER] Placement denied: blocked by overlap ({hit.name}).", this);
                return false;
            }

            return true;
        }

        private bool ServerTryConsumeOneItem(string itemId, out ConsumedItem consumed)
        {
            consumed = default;

            if (playerInventory == null)
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: inventory dependency missing.", this);
                return false;
            }

            if (!playerInventory.ServerTryFindFirstSlotWithItem(itemId, out int slotIndex))
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: item slot not found.", this);
                return false;
            }

            if (!playerInventory.ServerRemoveOneAtSlot(slotIndex, out string removedItemId, out int removedDurability, out ItemInstanceData removedInstanceData))
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: failed to consume inventory item.", this);
                return false;
            }

            if (!string.Equals(removedItemId, itemId, System.StringComparison.Ordinal))
            {
                // Defensive rollback if slot content changed unexpectedly.
                playerInventory.ServerAddItem(
                    removedItemId,
                    1,
                    removedDurability,
                    removedInstanceData.BonusStrength,
                    removedInstanceData.BonusDexterity,
                    removedInstanceData.BonusIntelligence,
                    removedInstanceData.CraftedBy);

                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: consumed item mismatch during validation.", this);
                return false;
            }

            consumed = new ConsumedItem
            {
                ItemId = removedItemId,
                Durability = removedDurability,
                InstanceData = removedInstanceData,
                Valid = true
            };

            return true;
        }

        private void ServerRollbackConsumedItem(ConsumedItem consumed)
        {
            if (!consumed.Valid || playerInventory == null)
                return;

            playerInventory.ServerAddItem(
                consumed.ItemId,
                1,
                consumed.Durability,
                consumed.InstanceData.BonusStrength,
                consumed.InstanceData.BonusDexterity,
                consumed.InstanceData.BonusIntelligence,
                consumed.InstanceData.CraftedBy);
        }

        private bool SpawnPlacedStructure(ItemDef itemDef, string sourceItemId, Vector3 worldPos, float rotY)
        {
            Vector3 spawnPos = worldPos + itemDef.PlacementOffset;
            float finalRotY = itemDef.AllowYawRotation ? rotY : 0f;
            Quaternion spawnRot = Quaternion.Euler(0f, finalRotY, 0f);

            NetworkObject spawnedNetworkObject = Instantiate(itemDef.PlaceablePrefab, spawnPos, spawnRot);
            if (spawnedNetworkObject == null)
            {
                Debug.LogWarning("[BuildingNet][SERVER] Placement denied: failed to instantiate placeable prefab.", this);
                return false;
            }

            PlacedBuildPiece placed = spawnedNetworkObject.GetComponent<PlacedBuildPiece>();
            if (placed != null)
                placed.ServerSetSourceItemId(sourceItemId);

            // World structures are server-owned authoritative objects.
            spawnedNetworkObject.Spawn(destroyWithScene: true);
            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (maxPlaceDistance < 0f)
                maxPlaceDistance = 0f;

            if (overlapCheckRadius < 0.01f)
                overlapCheckRadius = 0.01f;

            if (playerInventory == null)
                playerInventory = GetComponent<PlayerInventoryNet>();
        }
#endif
    }
}

