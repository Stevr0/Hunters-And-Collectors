using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// ResourceNodeNet
    /// --------------------------------------------------------------------
    /// Scene-placed resource node (tree/rock/bush) with server-authoritative cooldown.
    ///
    /// Core rules:
    /// - Node lives in the scene (no constant spawn/despawn).
    /// - Only server can "consume" and start cooldown.
    /// - Cooldown is based on NGO ServerTime to avoid client desync.
    /// - Cooldown state replicates to clients via a NetworkVariable.
    ///
    /// Drop safety (Option 3 style):
    /// - Uses ItemDef instead of string id to avoid typos/casing issues.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ResourceNodeNet : NetworkBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique id in the scene. Must be unique across all ResourceNodes.")]
        [SerializeField] private string nodeId = "NODE_001";

        [Header("Drop")]
        [Tooltip("ItemDef to grant when harvested (e.g. it_wood).")]
        [SerializeField] private HuntersAndCollectors.Items.ItemDef dropItem;

        [Tooltip("How many items are granted when harvested.")]
        [Min(1)]
        [SerializeField] private int dropQuantity = 1;

        [Header("Respawn")]
        [Min(0f)]
        [SerializeField] private float respawnSeconds = 30f;

        [Header("Depleted Presentation (Optional)")]
        [Tooltip("Disable this collider while depleted (prevents interaction). If null, uses own collider.")]
        [SerializeField] private Collider interactCollider;

        [Tooltip("Optional visuals to hide while depleted. If empty, node stays visible.")]
        [SerializeField] private GameObject[] visualsToHideWhenDepleted;

        // Server-authoritative "next available" timestamp.
        private readonly NetworkVariable<double> nextHarvestServerTime =
            new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public string NodeId => nodeId;

        /// <summary>ItemDef assigned for drops (may be null if not configured).</summary>
        public HuntersAndCollectors.Items.ItemDef DropItem => dropItem;

        /// <summary>Stable id string used by inventory system.</summary>
        public string DropItemId => dropItem != null ? dropItem.ItemId : string.Empty;

        public int DropQuantity => Mathf.Max(1, dropQuantity);

        /// <summary>
        /// Returns true if the node is currently harvestable (based on server time).
        /// Works on server and clients (clients read replicated timestamp).
        /// </summary>
        public bool IsHarvestableNow()
        {
            return ServerTimeNow() >= nextHarvestServerTime.Value;
        }

        /// <summary>
        /// Seconds until node is harvestable again (0 if already available).
        /// Useful for UI.
        /// </summary>
        public float SecondsUntilHarvestable()
        {
            double remaining = nextHarvestServerTime.Value - ServerTimeNow();
            return (float)Mathf.Max(0f, (float)remaining);
        }

        public override void OnNetworkSpawn()
        {
            if (interactCollider == null)
                interactCollider = GetComponent<Collider>();

            // Subscribe so clients update visuals when the timestamp changes.
            nextHarvestServerTime.OnValueChanged += OnNextHarvestTimeChanged;

            // Refresh immediately for late-join clients / host.
            RefreshPresentation();
        }

        public override void OnNetworkDespawn()
        {
            nextHarvestServerTime.OnValueChanged -= OnNextHarvestTimeChanged;
        }

        private void OnNextHarvestTimeChanged(double previousValue, double newValue)
        {
            RefreshPresentation();
        }

        /// <summary>
        /// SERVER: Starts the cooldown timer (consume this node).
        /// Call this ONLY after a successful harvest (inventory add etc).
        /// </summary>
        public void ServerConsumeStartCooldown()
        {
            if (!IsServer)
                return;

            double cooldown = Mathf.Max(0f, respawnSeconds);
            nextHarvestServerTime.Value = ServerTimeNow() + cooldown;

            // Host also needs to update visuals immediately.
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            bool available = IsHarvestableNow();

            // Disable interaction collider while depleted
            if (interactCollider != null)
                interactCollider.enabled = available;

            // Hide/show visuals while depleted (optional)
            if (visualsToHideWhenDepleted != null)
            {
                for (int i = 0; i < visualsToHideWhenDepleted.Length; i++)
                {
                    var go = visualsToHideWhenDepleted[i];
                    if (go != null)
                        go.SetActive(available);
                }
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
            if (dropQuantity < 1) dropQuantity = 1;
            if (respawnSeconds < 0f) respawnSeconds = 0f;

            if (string.IsNullOrWhiteSpace(nodeId))
                nodeId = "NODE_001";
        }
#endif
    }
}
