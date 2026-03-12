using System.Collections;
using HuntersAndCollectors.Inventory;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class ResourceDrop : NetworkBehaviour
    {
        [Header("Item Data")]
        [SerializeField] private ItemDef itemDefinition;

        [Min(1)]
        [SerializeField] private int quantity = 1;

        // Server-only runtime back-reference.
        // Intentionally not serialized/replicated.
        private PickupSpawnerNet _spawner;
        private bool _hasInstancePayload;
        private ItemInstance _instancePayload;
        private ItemInstanceData _instanceDataPayload;

        public ItemDef ItemDefinition => itemDefinition;
        public int Quantity => quantity;
        public string ItemId => itemDefinition != null ? itemDefinition.ItemId : string.Empty;

        public bool IsConsumed { get; private set; }

        public bool HasSpawner => _spawner != null;
        public string SpawnerName => _spawner != null ? _spawner.name : "<none>";
        public bool HasInstancePayload => _hasInstancePayload;
        public ItemInstance InstancePayload => _instancePayload;
        public ItemInstanceData InstanceDataPayload => _instanceDataPayload;

        private Coroutine _autoDespawnRoutine;
        private Coroutine _consumeDespawnRoutine;

        private static bool HasServerAuthority()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsServer;
        }

        /// <summary>
        /// SERVER: Configure runtime state. Safe to call before or after net spawn.
        /// </summary>
        public void ServerInitialize(int newQuantity, PickupSpawnerNet newSpawner = null, ItemDef overrideItemDefinition = null)
        {
            // Use singleton authority check so this can run before netObj.Spawn().
            if (!HasServerAuthority())
                return;

            // Authoritative spawn path can override item definition at runtime.
            // This prevents prefab-authored item mismatches from awarding wrong items.
            if (overrideItemDefinition != null)
                itemDefinition = overrideItemDefinition;

            quantity = Mathf.Max(1, newQuantity);
            IsConsumed = false;

            // Ensure interactable when (re)spawned.
            SetAllCollidersEnabled(true);
            SetAllRenderersEnabled(true);

            // Cancel any prior routines (future-proof if pooling/reuse happens)
            if (_autoDespawnRoutine != null) StopCoroutine(_autoDespawnRoutine);
            if (_consumeDespawnRoutine != null) StopCoroutine(_consumeDespawnRoutine);
            _autoDespawnRoutine = null;
            _consumeDespawnRoutine = null;

            // Harvested drops should remain unbound, spawner drops attach explicitly.
            _spawner = null;
            _hasInstancePayload = false;
            _instancePayload = default;
            _instanceDataPayload = default;
            if (newSpawner != null)
                ServerAttachSpawner(newSpawner);

            Debug.Log($"[ResourceDrop][SERVER] INIT name='{name}' netId={(NetworkObject != null && NetworkObject.IsSpawned ? NetworkObject.NetworkObjectId : 0)} item='{ItemId}' qty={quantity} hasSpawner={HasSpawner} spawner={SpawnerName}", this);
        }

        /// <summary>
        /// SERVER: Configure runtime state for a dropped concrete item instance.
        /// This keeps durability and rolled stats intact when the item is picked up again.
        /// </summary>
        public void ServerInitializeInstance(in ItemInstance instancePayload, in ItemInstanceData instanceDataPayload, PickupSpawnerNet newSpawner = null, ItemDef overrideItemDefinition = null)
        {
            ServerInitialize(1, newSpawner, overrideItemDefinition);

            if (!HasServerAuthority())
                return;

            _hasInstancePayload = true;
            _instancePayload = instancePayload;
            _instanceDataPayload = instanceDataPayload;
        }

        /// <summary>
        /// SERVER: Attach owning spawner at runtime (server-only reference).
        /// </summary>
        public void ServerAttachSpawner(PickupSpawnerNet newSpawner)
        {
            if (!HasServerAuthority())
                return;

            _spawner = newSpawner;

            Debug.Log(
                $"[ResourceDrop][SERVER] ATTACH_SPAWNER drop='{name}' netId={(NetworkObject != null && NetworkObject.IsSpawned ? NetworkObject.NetworkObjectId : 0)} item='{ItemId}' spawner='{SpawnerName}'",
                this);
        }

        /// <summary>
        /// SERVER back-compat helper.
        /// </summary>
        public void ServerSetSpawner(PickupSpawnerNet newSpawner)
        {
            ServerAttachSpawner(newSpawner);
        }

        /// <summary>
        /// SERVER: Called ONLY after server inventory is updated successfully.
        /// </summary>
        public void ServerConsumeAndDespawn(float delaySeconds = 0.75f)
        {
            if (!HasServerAuthority() || IsConsumed)
                return;

            IsConsumed = true;

            Debug.Log($"[ResourceDrop][SERVER] CONSUMED name='{name}' netId={NetworkObjectId} item='{ItemId}' qty={quantity} hasSpawner={HasSpawner} spawner={SpawnerName}", this);

            // If we were scheduled to auto-despawn, cancel it.
            if (_autoDespawnRoutine != null)
            {
                StopCoroutine(_autoDespawnRoutine);
                _autoDespawnRoutine = null;
            }

            // Notify spawner immediately if this drop came from one.
            if (_spawner != null)
            {
                var ownerSpawner = _spawner;
                _spawner = null;
                ownerSpawner.NotifyConsumedOrDespawned();
            }
            else
            {
                // Normal for harvested drops.
                // (Leave as Log, not Warning, so it doesn't look like a bug.)
                Debug.Log($"[ResourceDrop][SERVER] No spawner assigned (normal for harvested drops). name='{name}' item='{ItemId}'", this);
            }

            // Prevent double pickup immediately.
            SetAllCollidersEnabled(false);

            // Despawn after a short delay (lets pickup anim/sfx happen)
            if (_consumeDespawnRoutine != null)
                StopCoroutine(_consumeDespawnRoutine);

            _consumeDespawnRoutine = StartCoroutine(DespawnAfterDelay(delaySeconds, hideVisualsJustBeforeDespawn: true));
        }

        /// <summary>
        /// SERVER: Optional clutter cleanup. Does NOT mark consumed and does NOT disable colliders early.
        /// </summary>
        public void ServerScheduleAutoDespawn(float lifetimeSeconds)
        {
            if (!HasServerAuthority())
                return;

            if (lifetimeSeconds <= 0f)
                return;

            if (IsConsumed)
                return;

            if (_autoDespawnRoutine != null)
                StopCoroutine(_autoDespawnRoutine);

            _autoDespawnRoutine = StartCoroutine(AutoDespawnRoutine(lifetimeSeconds));
        }

        private IEnumerator AutoDespawnRoutine(float lifetime)
        {
            yield return new WaitForSeconds(lifetime);

            if (!HasServerAuthority() || IsConsumed)
                yield break;

            // Auto-despawn should NOT notify spawner as "consumed".
            // This is only for cleaning clutter (typically harvested drops).
            Debug.Log($"[ResourceDrop][SERVER] AUTO-DESPAWN name='{name}' netId={NetworkObjectId} item='{ItemId}' qty={quantity} spawner={SpawnerName}", this);

            // Optional: hide right before despawn
            SetAllRenderersEnabled(false);
            yield return null;

            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        private IEnumerator DespawnAfterDelay(float delay, bool hideVisualsJustBeforeDespawn)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            if (NetworkObject == null || !NetworkObject.IsSpawned)
                yield break;

            if (hideVisualsJustBeforeDespawn)
                SetAllRenderersEnabled(false);

            yield return null;

            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }

        private void SetAllCollidersEnabled(bool enabled)
        {
            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = enabled;
            }
        }

        private void SetAllRenderersEnabled(bool enabled)
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null)
                    rends[i].enabled = enabled;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (_autoDespawnRoutine != null)
            {
                StopCoroutine(_autoDespawnRoutine);
                _autoDespawnRoutine = null;
            }

            if (_consumeDespawnRoutine != null)
            {
                StopCoroutine(_consumeDespawnRoutine);
                _consumeDespawnRoutine = null;
            }

            _spawner = null;
            _hasInstancePayload = false;
            _instancePayload = default;
            _instanceDataPayload = default;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (quantity < 1) quantity = 1;
        }
#endif
    }

    [System.Obsolete("WorldPickup was renamed to ResourceDrop. Please swap the component when convenient.")]
    public sealed class WorldPickup : ResourceDrop { }
}
