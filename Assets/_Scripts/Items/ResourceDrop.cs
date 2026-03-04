using System.Collections;
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

        [Header("Optional Respawn Hook")]
        [SerializeField] private PickupSpawnerNet spawner;

        public ItemDef ItemDefinition => itemDefinition;
        public int Quantity => quantity;
        public string ItemId => itemDefinition != null ? itemDefinition.ItemId : string.Empty;

        public bool IsConsumed { get; private set; }

        // Debug helpers (super useful in logs)
        public bool HasSpawner => spawner != null;
        public string SpawnerName => spawner != null ? spawner.name : "<none>";

        private Coroutine _autoDespawnRoutine;
        private Coroutine _consumeDespawnRoutine;

        /// <summary>
        /// SERVER: Configure runtime state. Safe to call any time on server.
        /// </summary>
        public void ServerInitialize(int newQuantity, PickupSpawnerNet newSpawner = null)
        {
            if (!IsServer)
                return;

            quantity = Mathf.Max(1, newQuantity);
            spawner = newSpawner;
            IsConsumed = false;

            // Ensure interactable when (re)spawned.
            SetAllCollidersEnabled(true);
            SetAllRenderersEnabled(true);

            // Cancel any prior routines (future-proof if pooling/reuse happens)
            if (_autoDespawnRoutine != null) StopCoroutine(_autoDespawnRoutine);
            if (_consumeDespawnRoutine != null) StopCoroutine(_consumeDespawnRoutine);
            _autoDespawnRoutine = null;
            _consumeDespawnRoutine = null;

            Debug.Log($"[ResourceDrop][SERVER] INIT name='{name}' netId={(NetworkObject != null ? NetworkObject.NetworkObjectId : 0)} item='{ItemId}' qty={quantity} spawner={SpawnerName}", this);
        }

        /// <summary>
        /// SERVER back-compat helper
        /// </summary>
        public void ServerSetSpawner(PickupSpawnerNet newSpawner)
        {
            if (!IsServer)
                return;

            spawner = newSpawner;
            Debug.Log($"[ResourceDrop][SERVER] Spawner assigned name='{name}' item='{ItemId}' spawner={SpawnerName}", this);
        }

        /// <summary>
        /// SERVER: Called ONLY after server inventory is updated successfully.
        /// </summary>
        public void ServerConsumeAndDespawn(float delaySeconds = 0.75f)
        {
            if (!IsServer || IsConsumed)
                return;

            IsConsumed = true;

            Debug.Log($"[ResourceDrop][SERVER] CONSUMED name='{name}' netId={NetworkObjectId} item='{ItemId}' qty={quantity} spawner={SpawnerName}", this);

            // If we were scheduled to auto-despawn, cancel it.
            if (_autoDespawnRoutine != null)
            {
                StopCoroutine(_autoDespawnRoutine);
                _autoDespawnRoutine = null;
            }

            // Notify spawner immediately if this drop came from one.
            if (spawner != null)
            {
                spawner.NotifyConsumedOrDespawned();
            }
            else
            {
                // Normal for harvested drops.
                // (Leave as Log, not Warning, so it doesn't look like a bug.)
                Debug.Log($"[ResourceDrop][SERVER] Consumed drop has no spawner (expected for harvested drops). name='{name}' item='{ItemId}'", this);
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
            if (!IsServer)
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

            if (!IsServer || IsConsumed)
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