using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// ResourceDrop
    /// --------------------------------------------------------------------
    /// Server-authoritative world item that can be collected by a player.
    /// Keeps the previous WorldPickup data model but exposes deterministic
    /// metadata for the new harvesting flow.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class ResourceDrop : NetworkBehaviour
    {
        [Header("Item Data")]
        [Tooltip("Drag an ItemDef asset here (e.g. IT_Wood, IT_Stone).")]
        [SerializeField] private ItemDef itemDefinition;

        [Tooltip("Base quantity granted before skill scaling.")]
        [Min(1)]
        [SerializeField] private int quantity = 1;

        [Header("Optional Respawn Hook")]
        [Tooltip("Assigned by a spawner at runtime. Leave null for non-respawning drops.")]
        [SerializeField] private PickupSpawnerNet spawner;

        /// <summary>Definition asset assigned in inspector.</summary>
        public ItemDef ItemDefinition => itemDefinition;

        /// <summary>Base quantity granted.</summary>
        public int Quantity => quantity;

        /// <summary>Stable id string used by inventory system.</summary>
        public string ItemId => itemDefinition != null ? itemDefinition.ItemId : string.Empty;

        /// <summary>True after the server consumes the drop.</summary>
        public bool IsConsumed { get; private set; }

        /// <summary>
        /// SERVER: Called by a spawner after it instantiates this drop.
        /// </summary>
        public void ServerSetSpawner(PickupSpawnerNet newSpawner)
        {
            spawner = newSpawner;
        }

        /// <summary>
        /// SERVER: Call this ONLY after inventory was successfully updated on the server.
        /// </summary>
        public void ServerConsumeAndDespawn()
        {
            if (!IsServer || IsConsumed)
                return;

            IsConsumed = true;

            if (spawner != null)
                spawner.NotifyConsumedOrDespawned();

            NetworkObject.Despawn(true);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (itemDefinition == null)
                Debug.LogWarning($"[ResourceDrop] '{name}' has no ItemDef assigned.", this);

            if (quantity < 1) quantity = 1;
        }
#endif
    }

    /// <summary>
    /// Back-compat shim so existing prefabs referencing WorldPickup do not break.
    /// </summary>
    [System.Obsolete("WorldPickup was renamed to ResourceDrop. Please swap the component when convenient.")]
    public sealed class WorldPickup : ResourceDrop { }
}
