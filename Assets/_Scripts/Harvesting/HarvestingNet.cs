using System;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// HarvestingNet
    /// --------------------------------------------------------------------
    /// Server-authoritative entry point for all resource acquisition. Clients only
    /// call the public request helpers which in turn invoke ServerRpc methods.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HarvestingNet : NetworkBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private PlayerInventoryNet inventory;
        [SerializeField] private SkillsNet skills;
        [SerializeField] private ResourceNodeRegistry nodeRegistry;

        [Header("Validation")]
        [Tooltip("Max world distance allowed when harvesting a node (meters).")]
        [Min(0.5f)]
        [SerializeField] private float nodeMaxDistance = 3.0f;

        [Tooltip("Max world distance allowed when picking up a resource drop (meters).")]
        [Min(0.5f)]
        [SerializeField] private float dropMaxDistance = 3.0f;

        [Header("Yield Scaling")]
        [Tooltip("Hard cap applied after skill scaling to prevent runaway yields.")]
        [Min(1)]
        [SerializeField] private int nodeYieldMax = 50;

        [Tooltip("Hard cap applied to world drop pickups after skill scaling.")]
        [Min(1)]
        [SerializeField] private int dropYieldMax = 30;

        [Header("XP Rewards")]
        [Tooltip("Base XP granted for every successful harvest or pickup.")]
        [Min(0)]
        [SerializeField] private int xpPerAction = 1;

        [Tooltip("Bonus XP when the action depletes a node (set to 0 to disable)." )]
        [Min(0)]
        [SerializeField] private int xpBonusOnNodeDeplete = 2;

        private const int NodeYieldDivisor = 10;  // floor(level / 10)
        private const int DropYieldDivisor = 20;  // floor(level / 20)

        #region Client Entry Points

        /// <summary>
        /// Client-side helper invoked by PlayerInteract or UI.
        /// </summary>
        public void RequestHarvest(ResourceNodeNet node)
        {
            if (!IsOwner || node == null)
                return;

            if (string.IsNullOrWhiteSpace(node.NodeId))
                return;

            RequestHarvestServerRpc(node.NodeId);
        }

        /// <summary>
        /// Client-side helper for picking up spawned drops.
        /// </summary>
        public void RequestPickup(ResourceDrop drop)
        {
            if (!IsOwner || drop == null)
                return;

            if (!drop.IsSpawned)
                return;

            RequestPickupDropServerRpc(drop.NetworkObjectId);
        }

        #endregion

        #region Server RPCs

        [ServerRpc(RequireOwnership = true)]
        private void RequestHarvestServerRpc(string nodeId)
        {
            if (!IsServer)
                return;

            if (!EnsureDependencies())
            {
                SendHarvestResult(false, HarvestFailureReason.ConfigError, nodeId, string.Empty, 0);
                return;
            }

            if (!nodeRegistry.TryGet(nodeId, out var node) || node == null)
            {
                SendHarvestResult(false, HarvestFailureReason.NodeNotFound, nodeId, string.Empty, 0);
                return;
            }

            if (!node.IsHarvestableNow())
            {
                SendHarvestResult(false, HarvestFailureReason.NodeOnCooldown, nodeId, node.DropItemId, 0);
                return;
            }

            if (!IsWithinRange(node.transform.position, nodeMaxDistance))
            {
                SendHarvestResult(false, HarvestFailureReason.OutOfRange, nodeId, node.DropItemId, 0);
                return;
            }

            var itemId = node.DropItemId;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                SendHarvestResult(false, HarvestFailureReason.ConfigError, nodeId, string.Empty, 0);
                return;
            }

            var skillId = SkillIdForResource(node.ResourceType);
            var level = GetSkillLevel(skillId);
            var desiredYield = CalculateNodeYield(node.BaseYield, level);

            if (!CanInventoryAccept(itemId, desiredYield))
            {
                SendHarvestResult(false, HarvestFailureReason.InventoryFull, nodeId, itemId, 0);
                return;
            }

            var remainder = inventory.ServerAddItem(itemId, desiredYield);
            var granted = desiredYield - remainder;

            if (granted <= 0)
            {
                SendHarvestResult(false, HarvestFailureReason.InventoryFull, nodeId, itemId, 0);
                return;
            }

            node.ServerConsumeStartCooldown();

            var xpAward = xpPerAction + (xpBonusOnNodeDeplete > 0 ? xpBonusOnNodeDeplete : 0);
            GrantXp(skillId, xpAward);

            SendHarvestResult(true, HarvestFailureReason.None, nodeId, itemId, granted);
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestPickupDropServerRpc(ulong dropNetworkObjectId)
        {
            if (!IsServer)
                return;

            if (!EnsureDependencies())
            {
                SendDropResult(false, HarvestFailureReason.ConfigError, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(dropNetworkObjectId, out var dropNetObj))
            {
                SendDropResult(false, HarvestFailureReason.DropMissing, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            if (!dropNetObj.TryGetComponent<ResourceDrop>(out var drop))
            {
                SendDropResult(false, HarvestFailureReason.DropMissing, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            if (drop.IsConsumed)
            {
                SendDropResult(false, HarvestFailureReason.AlreadyConsumed, dropNetworkObjectId, drop.ItemId, 0);
                return;
            }

            if (!IsWithinRange(dropNetObj.transform.position, dropMaxDistance))
            {
                SendDropResult(false, HarvestFailureReason.OutOfRange, dropNetworkObjectId, drop.ItemId, 0);
                return;
            }

            var itemId = drop.ItemId;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                SendDropResult(false, HarvestFailureReason.ConfigError, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            if (!TryGetSkillForItem(itemId, out var skillId))
            {
                SendDropResult(false, HarvestFailureReason.ConfigError, dropNetworkObjectId, itemId, 0);
                return;
            }

            var level = GetSkillLevel(skillId);
            var desiredQuantity = CalculateDropYield(drop.Quantity, level);

            if (!CanInventoryAccept(itemId, desiredQuantity))
            {
                SendDropResult(false, HarvestFailureReason.InventoryFull, dropNetworkObjectId, itemId, 0);
                return;
            }

            var remainder = inventory.ServerAddItem(itemId, desiredQuantity);
            var granted = desiredQuantity - remainder;

            if (granted <= 0)
            {
                SendDropResult(false, HarvestFailureReason.InventoryFull, dropNetworkObjectId, itemId, 0);
                return;
            }

            drop.ServerConsumeAndDespawn();
            GrantXp(skillId, xpPerAction);

            SendDropResult(true, HarvestFailureReason.None, dropNetworkObjectId, itemId, granted);
        }

        #endregion

        #region Client Feedback RPCs

        [ClientRpc]
        private void HarvestResultClientRpc(bool success, HarvestFailureReason reason, string nodeId, string itemId, int amountAwarded, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            Debug.Log($"[HarvestingNet][CLIENT] Harvest result: success={success} reason={reason} node={nodeId} item={itemId} amount={amountAwarded}");
        }

        [ClientRpc]
        private void DropPickupResultClientRpc(bool success, HarvestFailureReason reason, ulong dropNetworkObjectId, string itemId, int amountAwarded, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            Debug.Log($"[HarvestingNet][CLIENT] Drop pickup result: success={success} reason={reason} dropId={dropNetworkObjectId} item={itemId} amount={amountAwarded}");
        }

        private void SendHarvestResult(bool success, HarvestFailureReason reason, string nodeId, string itemId, int amount)
        {
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            HarvestResultClientRpc(success, reason, nodeId ?? string.Empty, itemId ?? string.Empty, amount, rpcParams);
        }

        private void SendDropResult(bool success, HarvestFailureReason reason, ulong dropId, string itemId, int amount)
        {
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            DropPickupResultClientRpc(success, reason, dropId, itemId ?? string.Empty, amount, rpcParams);
        }

        #endregion

        #region Helpers

        private bool EnsureDependencies()
        {
            if (inventory == null)
                inventory = GetComponent<PlayerInventoryNet>();

            if (skills == null)
                skills = GetComponent<SkillsNet>();

            if (nodeRegistry == null)
                nodeRegistry = ResourceNodeRegistry.Instance ?? FindFirstObjectByType<ResourceNodeRegistry>();

            return inventory != null && nodeRegistry != null;
        }

        private bool IsWithinRange(Vector3 targetPosition, float maxDistance)
        {
            var delta = Vector3.Distance(transform.position, targetPosition);
            return delta <= maxDistance + 0.1f; // tiny tolerance for camera/player pivot differences
        }

        private int CalculateNodeYield(int baseYield, int level)
        {
            var scaled = baseYield + Mathf.FloorToInt(level / (float)NodeYieldDivisor);
            return Mathf.Clamp(scaled, 1, nodeYieldMax);
        }

        private int CalculateDropYield(int baseQuantity, int level)
        {
            var scaled = baseQuantity + Mathf.FloorToInt(level / (float)DropYieldDivisor);
            return Mathf.Clamp(scaled, 1, dropYieldMax);
        }

        private bool CanInventoryAccept(string itemId, int quantity)
        {
            if (inventory == null || inventory.Grid == null)
                return false;

            if (!inventory.Grid.CanAdd(itemId, quantity, out var remainder))
                return false;

            return remainder == 0;
        }

        private int GetSkillLevel(string skillId)
        {
            if (skills == null || string.IsNullOrWhiteSpace(skillId))
                return 0;

            return Mathf.Clamp(skills.GetLevel(skillId), 0, 100);
        }

        private void GrantXp(string skillId, int amount)
        {
            if (skills == null || amount <= 0 || string.IsNullOrWhiteSpace(skillId))
                return;

            skills.AddXp(skillId, amount);
        }

        private static string SkillIdForResource(ResourceNodeResourceType resourceType)
        {
            return resourceType switch
            {
                ResourceNodeResourceType.Wood => SkillId.Woodcutting,
                ResourceNodeResourceType.Stone => SkillId.Mining,
                ResourceNodeResourceType.Fiber => SkillId.Foraging,
                _ => SkillId.Foraging
            };
        }

        private static bool TryGetSkillForItem(string itemId, out string skillId)
        {
            skillId = string.Empty;

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            var canonical = itemId.Trim();

            if (canonical.Equals("IT_Wood", StringComparison.OrdinalIgnoreCase))
            {
                skillId = SkillId.Woodcutting;
                return true;
            }

            if (canonical.Equals("IT_Stone", StringComparison.OrdinalIgnoreCase))
            {
                skillId = SkillId.Mining;
                return true;
            }

            if (canonical.Equals("IT_Fiber", StringComparison.OrdinalIgnoreCase))
            {
                skillId = SkillId.Foraging;
                return true;
            }

            return false;
        }

        #endregion
    }

    public enum HarvestFailureReason
    {
        None,
        NodeNotFound,
        NodeOnCooldown,
        OutOfRange,
        InventoryFull,
        ConfigError,
        DropMissing,
        AlreadyConsumed
    }
}
