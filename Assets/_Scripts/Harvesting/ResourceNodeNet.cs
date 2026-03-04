using System;
using HuntersAndCollectors.Items;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Scene-authored harvest node with server-authoritative cooldown + tool gating.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ResourceNodeNet : NetworkBehaviour
    {
        [Header("Identity")]
        [Tooltip("Stable unique id per-scene (e.g. TREE_001).")]
        [SerializeField] private string nodeId = "NODE_001";

        [Header("Resource")]
        [Tooltip("Resource family used for skill + loot routing.")]
        [SerializeField] private ResourceType resourceType = ResourceType.Wood;

        [Tooltip("Tool required to begin harvesting. Fiber is gated by Sickle per MVP doc.")]
        [SerializeField] private ToolType requiredTool = ToolType.Axe;

        [Tooltip("Specific item that must be equipped. Leave empty to fall back to tool type.")]
        [SerializeField] private ItemDef requiredToolItem;

        [Tooltip("Base seconds before skill reductions.")]
        [Min(0.1f)]
        [SerializeField] private float baseHarvestSeconds = 2.0f;

        [Header("Yield")]
        [Tooltip("Item granted on success (stackable resource).")]
        [SerializeField] private HuntersAndCollectors.Items.ItemDef dropItem;

        [Tooltip("Base amount before skill scaling.")]
        [Min(1)]
        [SerializeField] private int baseYield = 1;

        [Header("Respawn")]
        [Tooltip("Seconds before the node becomes harvestable again.")]
        [Min(0f)]
        [SerializeField] private float respawnSeconds = 30f;

        [Header("Health")]
        [Tooltip("Total hits required before the node depletes.")]
        [Min(1)]
        [SerializeField] private int maxHealth = 3;

        [Header("Rare Drops")]
        [Tooltip("Optional bonus roll entries. Leave empty for none.")]
        [SerializeField] private RareDropEntry[] rareDrops;

        [Header("Presentation")]
        [Tooltip("Collider disabled while on cooldown. Defaults to current collider if null.")]
        [SerializeField] private Collider interactCollider;

        [Tooltip("Optional visuals hidden while on cooldown.")]
        [SerializeField] private GameObject[] visualsToHideWhenDepleted;

        private Coroutine _serverRespawnRoutine;
        private readonly NetworkVariable<double> nextHarvestServerTime =
            new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> currentHealth =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private ulong _lockedByClientId = ulong.MaxValue;
        private double _lockStartServerTime;

        public string NodeId => nodeId;
        public ResourceType ResourceType => resourceType;
        public ToolType RequiredTool => requiredTool;
        public ItemDef RequiredToolItem => requiredToolItem;
        public string RequiredToolItemId => requiredToolItem != null ? requiredToolItem.ItemId : string.Empty;
        public float BaseHarvestSeconds => Mathf.Max(0.1f, baseHarvestSeconds);
        public int BaseYield => Mathf.Max(1, baseYield);
        public string DropItemId => dropItem != null ? dropItem.ItemId : string.Empty;
        public HuntersAndCollectors.Items.ItemDef DropItem => dropItem;
        public float RespawnSeconds => Mathf.Max(0f, respawnSeconds);
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int CurrentHealth => Mathf.Clamp(currentHealth.Value, 0, MaxHealth);
        public bool IsDepleted => CurrentHealth <= 0;
        public bool HasRareDrops => rareDrops != null && rareDrops.Length > 0;
        public System.Collections.Generic.IReadOnlyList<RareDropEntry> RareDrops => rareDrops ?? Array.Empty<RareDropEntry>();
        public bool IsLocked => _lockedByClientId != ulong.MaxValue;
        public ulong LockedByClientId => _lockedByClientId;

        public override void OnNetworkSpawn()
        {
            if (interactCollider == null)
                interactCollider = GetComponent<Collider>();

            if (IsServer)
            {
                maxHealth = Mathf.Max(1, maxHealth);
                if (currentHealth.Value <= 0)
                    currentHealth.Value = maxHealth;
            }

            nextHarvestServerTime.OnValueChanged += OnNextHarvestTimeChanged;
            RefreshPresentation();
        }

        public override void OnNetworkDespawn()
        {
            nextHarvestServerTime.OnValueChanged -= OnNextHarvestTimeChanged;
            ServerForceReleaseHarvestLock();

            if (IsServer && _serverRespawnRoutine != null)
            {
                StopCoroutine(_serverRespawnRoutine);
                _serverRespawnRoutine = null;
            }
        }

        /// <summary>Clients + server can query if the node can currently be harvested.</summary>
        public bool IsHarvestableNow()
        {
            return ServerTimeNow() >= nextHarvestServerTime.Value;
        }

        /// <summary>Seconds remaining until harvestable (0 when available).</summary>
        public float SecondsUntilHarvestable()
        {
            var remaining = (float)(nextHarvestServerTime.Value - ServerTimeNow());
            return Mathf.Max(0f, remaining);
        }

        public bool ServerApplyDamage(int damage)
        {
            if (!IsServer)
                return false;

            if (damage <= 0)
                return false;

            if (!IsHarvestableNow())
                return false;

            var current = CurrentHealth;
            if (current <= 0)
                return false;

            var next = Mathf.Max(0, current - damage);
            currentHealth.Value = next;
            return next <= 0;
        }

        public void ServerRestoreFullHealth()
        {
            if (!IsServer)
                return;

            currentHealth.Value = MaxHealth;
            nextHarvestServerTime.Value = ServerTimeNow();
            RefreshPresentation();
        }

        private void OnNextHarvestTimeChanged(double previousValue, double newValue)
        {
            RefreshPresentation();
        }

        /// <summary>SERVER: attempt to reserve this node for a specific client.</summary>
        public bool ServerTryAcquireHarvestLock(ulong clientId)
        {
            if (!IsServer)
                return false;

            if (clientId == ulong.MaxValue)
                return false;

            if (IsLocked)
                return _lockedByClientId == clientId; // already locked by same client

            if (!IsHarvestableNow())
                return false;

            _lockedByClientId = clientId;
            _lockStartServerTime = ServerTimeNow();
            return true;
        }

        /// <summary>SERVER: releases the harvest lock when the owner cancels/completes.</summary>
        public void ServerReleaseHarvestLock(ulong clientId)
        {
            if (!IsServer)
                return;

            if (_lockedByClientId != clientId)
                return;

            _lockedByClientId = ulong.MaxValue;
        }

        /// <summary>SERVER: forcefully clears the harvest lock (e.g., node cooldown).</summary>
        public void ServerForceReleaseHarvestLock()
        {
            if (!IsServer)
                return;

            _lockedByClientId = ulong.MaxValue;
        }

        public bool IsServerLockOwner(ulong clientId)
        {
            return _lockedByClientId == clientId;
        }

        /// <summary>
        /// SERVER: Call after successful harvest to enter cooldown + refresh visuals.
        /// </summary>
        public void ServerConsumeStartCooldown()
        {
            if (!IsServer)
                return;

            ServerForceReleaseHarvestLock();
            currentHealth.Value = 0;

            var cooldown = RespawnSeconds;
            nextHarvestServerTime.Value = ServerTimeNow() + cooldown;
            RefreshPresentation();

            if (_serverRespawnRoutine != null)
            {
                StopCoroutine(_serverRespawnRoutine);
                _serverRespawnRoutine = null;
            }

            if (cooldown <= 0f)
            {
                nextHarvestServerTime.Value = ServerTimeNow();
                currentHealth.Value = MaxHealth;
                RefreshPresentation();
                return;
            }

            _serverRespawnRoutine = StartCoroutine(ServerRespawnRoutine(cooldown));
        }

        private System.Collections.IEnumerator ServerRespawnRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);

            nextHarvestServerTime.Value = ServerTimeNow();
            currentHealth.Value = MaxHealth;
            RefreshPresentation();
            _serverRespawnRoutine = null;
        }

        private void RefreshPresentation()
        {
            var available = IsHarvestableNow();

            if (interactCollider != null)
                interactCollider.enabled = available;

            if (visualsToHideWhenDepleted == null)
                return;

            for (int i = 0; i < visualsToHideWhenDepleted.Length; i++)
            {
                if (visualsToHideWhenDepleted[i] != null)
                    visualsToHideWhenDepleted[i].SetActive(available);
            }
        }

        private static double ServerTimeNow()
        {
            if (NetworkManager.Singleton == null)
                return Time.timeAsDouble;

            return NetworkManager.Singleton.ServerTime.Time;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (baseYield < 1) baseYield = 1;
            if (respawnSeconds < 0f) respawnSeconds = 0f;
            if (baseHarvestSeconds < 0.1f) baseHarvestSeconds = 0.1f;
            if (maxHealth < 1) maxHealth = 1;

            if (resourceType == ResourceType.Fiber && requiredTool == ToolType.None)
                requiredTool = ToolType.Sickle; // enforce MVP expectation

            if (requiredToolItem != null && requiredTool == ToolType.None)
                requiredTool = GuessToolTypeFromItem(requiredToolItem);

            if (string.IsNullOrWhiteSpace(nodeId))
                nodeId = name;
        }

        private static ToolType GuessToolTypeFromItem(ItemDef item)
        {
            if (item == null || item.ToolTags == null)
                return ToolType.None;

            for (int i = 0; i < item.ToolTags.Length; i++)
            {
                switch (item.ToolTags[i])
                {
                    case ToolTag.Axe:
                        return ToolType.Axe;
                    case ToolTag.Pickaxe:
                        return ToolType.Pickaxe;
                    case ToolTag.Sickle:
                        return ToolType.Sickle;
                }
            }

            return ToolType.None;
        }
#endif
    }
}
