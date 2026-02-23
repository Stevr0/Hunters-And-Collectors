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
    /// Unity setup:
    /// - Add a NetworkObject component (required).
    /// - This script (NetworkBehaviour) on same GameObject.
    /// - Collider should be on your Interactable layer so PlayerInteract ray can hit it.
    /// - Optional: assign visuals/collider that should disable while depleted.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ResourceNodeNet : NetworkBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique id in the scene. Must be unique across all ResourceNodes.")]
        [SerializeField] private string nodeId = "NODE_001";

        [Header("Drop")]
        [Tooltip("Stable item id (e.g. it_wood). In a later pass we can change this to ItemDef like pickups.")]
        [SerializeField] private string dropItemId = "it_wood";

        [Min(1)]
        [SerializeField] private int dropQuantity = 1;

        [Header("Respawn")]
        [Min(0f)]
        [SerializeField] private float respawnSeconds = 30f;

        [Header("Depleted Presentation (Optional)")]
        [Tooltip("Optional: disable this collider while depleted (prevents interaction). If null, uses own collider.")]
        [SerializeField] private Collider interactCollider;

        [Tooltip("Optional: show/hide visuals while depleted. If empty, node stays visible.")]
        [SerializeField] private GameObject[] visualsToHideWhenDepleted;

        // Server-authoritative "next available" timestamp.
        // Use double because ServerTime is double precision.
        private readonly NetworkVariable<double> nextHarvestServerTime =
            new(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public string NodeId => nodeId;
        public string DropItemId => dropItemId;
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
        /// How many seconds until harvestable again (0 if already available).
        /// Useful for UI.
        /// </summary>
        public float SecondsUntilHarvestable()
        {
            double remaining = nextHarvestServerTime.Value - ServerTimeNow();
            return (float)Mathf.Max(0f, (float)remaining);
        }

        public override void OnNetworkSpawn()
        {
            // Cache collider if not set.
            if (interactCollider == null)
                interactCollider = GetComponent<Collider>();

            // When the replicated value changes, update visuals.
            nextHarvestServerTime.OnValueChanged += (_, __) => RefreshPresentation();

            // Refresh immediately for clients joining late.
            RefreshPresentation();
        }

        public override void OnNetworkDespawn()
        {
            nextHarvestServerTime.OnValueChanged -= (_, __) => RefreshPresentation();
        }

        /// <summary>
        /// SERVER: Consumes the node (starts cooldown).
        /// Call this ONLY after a successful harvest (inventory add etc).
        /// </summary>
        public void ServerConsumeStartCooldown()
        {
            if (!IsServer)
                return;

            double cooldown = Mathf.Max(0f, respawnSeconds);
            nextHarvestServerTime.Value = ServerTimeNow() + cooldown;

            // Presentation will update through OnValueChanged on all clients.
            RefreshPresentation();
        }

        private void RefreshPresentation()
        {
            bool available = IsHarvestableNow();

            // Disable interaction collider while depleted (optional)
            if (interactCollider != null)
                interactCollider.enabled = available;

            // Hide visuals while depleted (optional)
            if (visualsToHideWhenDepleted != null)
            {
                for (int i = 0; i < visualsToHideWhenDepleted.Length; i++)
                {
                    if (visualsToHideWhenDepleted[i] != null)
                        visualsToHideWhenDepleted[i].SetActive(available);
                }
            }
        }

        private static double ServerTimeNow()
        {
            // If NetworkManager isn't running (edit-mode tests), fall back to local time.
            if (NetworkManager.Singleton == null)
                return Time.timeAsDouble;

            return NetworkManager.Singleton.ServerTime.Time;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (dropQuantity < 1) dropQuantity = 1;
            if (respawnSeconds < 0f) respawnSeconds = 0f;
        }
#endif
    }
}
