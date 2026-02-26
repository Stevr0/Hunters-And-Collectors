using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// WorldPickup
    /// --------------------------------------------------------------------
    /// Attach this to any world item that can be picked up (Stone, Stick, etc).
    ///
    /// Data:
    /// - ItemDef (stable id lives inside the asset)
    /// - Quantity
    ///
    /// Authority:
    /// - Clients NEVER despawn this object and NEVER mutate inventory.
    /// - Server consumes + despawns after successful inventory add.
    ///
    /// Respawn integration:
    /// - If spawned by a PickupSpawnerNet, the spawner injects itself into this pickup.
    /// - When consumed, the pickup notifies the spawner so it can start a respawn timer.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class WorldPickup : NetworkBehaviour
    {
        [Header("Item Data")]
        [Tooltip("Drag an ItemDef asset here (e.g. it_stone, it_stick).")]
        [SerializeField] private ItemDef itemDefinition;

        [Tooltip("How many items are granted when picked up.")]
        [Min(1)]
        [SerializeField] private int quantity = 1;

        [Header("Optional Respawn Hook")]
        [Tooltip("Assigned by a spawner at runtime. Leave null for non-respawning pickups.")]
        [SerializeField] private PickupSpawnerNet spawner;

        /// <summary>Definition asset assigned in inspector.</summary>
        public ItemDef ItemDefinition => itemDefinition;

        /// <summary>Quantity granted.</summary>
        public int Quantity => quantity;

        /// <summary>Stable id string used by inventory system.</summary>
        public string ItemId => itemDefinition != null ? itemDefinition.ItemId : string.Empty;

        /// <summary>
        /// SERVER: Called by a spawner after it instantiates this pickup.
        /// We keep this as a simple reference; it is NOT security-critical because
        /// only the server should ever instantiate/spawn world pickups.
        /// </summary>
        public void ServerSetSpawner(PickupSpawnerNet newSpawner)
        {
            // NOTE:
            // We DO NOT check IsServer here because during Instantiate-before-Spawn,
            // NGO flags on this component can be in a "not fully spawned" state.
            // This method should only be called by server-side code.
            spawner = newSpawner;
        }

        /// <summary>
        /// SERVER: Call this ONLY after inventory was successfully updated on the server.
        /// This removes the pickup from the world for all clients and starts respawn.
        /// </summary>
        public void ServerConsumeAndDespawn()
        {
            // Enforce server authority. If a client somehow calls this, do nothing.
            if (!IsServer)
                return;

            // Notify spawner first so it can schedule respawn.
            if (spawner != null)
                spawner.NotifyConsumedOrDespawned();

            // Despawn from NGO and destroy the instance on server.
            NetworkObject.Despawn(true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Editor-time safety: warn immediately if someone forgets to assign the definition.
            if (itemDefinition == null)
                Debug.LogWarning($"[WorldPickup] '{name}' has no ItemDef assigned.", this);

            if (quantity < 1) quantity = 1;
        }
#endif
    }
}