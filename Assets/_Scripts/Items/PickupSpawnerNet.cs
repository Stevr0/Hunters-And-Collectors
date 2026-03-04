using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// PickupSpawnerNet (Server-authoritative)
    /// ------------------------------------------------------------
    /// Spawns a ResourceDrop prefab and respawns it after it is picked up.
    ///
    /// Key fixes:
    /// 1) We CLEAR our spawned-instance reference immediately when notified consumed,
    ///    because the pickup may remain IsSpawned for a short "despawn delay" window.
    ///    (If we don't clear immediately, TrySpawnNow() can be blocked by IsSpawned==true.)
    ///
    /// 2) We ONLY assign _spawnedInstance AFTER a successful Spawn(),
    ///    so the spawner doesn't think it owns a live instance when something failed.
    ///
    /// 3) We inject THIS spawner into the spawned ResourceDrop via ServerInitialize(),
    ///    and we log clearly if ResourceDrop is missing on the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PickupSpawnerNet : NetworkBehaviour
    {
        [Header("Prefab")]
        [Tooltip("NetworkObject prefab to spawn (must be registered with NGO).")]
        [SerializeField] private NetworkObject pickupPrefab;

        [Header("Spawn Location")]
        [Tooltip("If true, spawn at this GameObject's transform. If false, use spawnPosition.")]
        [SerializeField] private bool spawnAtThisTransform = true;

        [Tooltip("Used only if spawnAtThisTransform is false.")]
        [SerializeField] private Vector3 spawnPosition;

        [Tooltip("If true, spawns once when the server spawns this spawner.")]
        [SerializeField] private bool spawnOnServerStart = true;

        [Header("Respawn")]
        [Tooltip("Seconds after pickup to respawn. Set 0 to disable respawn.")]
        [Min(0f)]
        [SerializeField] private float respawnSeconds = 10f;

        // Track the currently spawned instance (server-only).
        private NetworkObject _spawnedInstance;

        // Track respawn coroutine (server-only).
        private Coroutine _respawnRoutine;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            Debug.Log(
                $"[PickupSpawnerNet][SERVER] OnNetworkSpawn spawner='{name}' spawnOnStart={spawnOnServerStart} respawnSeconds={respawnSeconds}",
                this);

            if (spawnOnServerStart)
                TrySpawnNow();
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            // Clean up coroutine on despawn so we don't run timers for dead spawners.
            if (_respawnRoutine != null)
            {
                StopCoroutine(_respawnRoutine);
                _respawnRoutine = null;
            }

            // If a pickup is still alive, we can optionally despawn it too.
            // (Not required, but helps avoid orphaned pickups in editor testing.)
            if (_spawnedInstance != null && _spawnedInstance.IsSpawned)
            {
                Debug.Log($"[PickupSpawnerNet][SERVER] Spawner despawned; despawning active pickup netId={_spawnedInstance.NetworkObjectId}", this);
                _spawnedInstance.Despawn(true);
            }

            _spawnedInstance = null;
        }

        /// <summary>
        /// SERVER: Spawn a pickup if one is not already spawned.
        /// </summary>
        [ContextMenu("Server: Try Spawn Now")]
        public void TrySpawnNow()
        {
            if (!IsServer)
                return;

            if (pickupPrefab == null)
            {
                Debug.LogError($"[PickupSpawnerNet][SERVER] Missing pickupPrefab on spawner '{name}'.", this);
                return;
            }

            // If we still have a live spawned instance, do nothing.
            if (_spawnedInstance != null && _spawnedInstance.IsSpawned)
            {
                Debug.Log($"[PickupSpawnerNet][SERVER] '{name}' already has instance netId={_spawnedInstance.NetworkObjectId}", this);
                return;
            }

            Vector3 pos = spawnAtThisTransform ? transform.position : spawnPosition;
            Quaternion rot = spawnAtThisTransform ? transform.rotation : Quaternion.identity;

            // Instantiate on server.
            var instance = Instantiate(pickupPrefab, pos, rot);

            // ResourceDrop may be on root OR children (common when visuals are nested).
            var drop = instance.GetComponentInChildren<ResourceDrop>(true);

            if (drop == null)
            {
                Debug.LogError(
                    $"[PickupSpawnerNet][SERVER] Spawned prefab '{pickupPrefab.name}' has NO ResourceDrop on root/children. " +
                    $"Spawner='{name}'. Respawn cannot work.",
                    instance);

                // Destroy the instance so it doesn't hang around on server.
                Destroy(instance.gameObject);
                return;
            }

            // Inject THIS spawner so the pickup can notify us on consume.
            // Keep prefab quantity as configured (you can later override if needed).
            drop.ServerInitialize(drop.Quantity, this);

            // Spawn for all clients.
            instance.Spawn(true);

            // IMPORTANT FIX:
            // Only store reference AFTER successful Spawn().
            _spawnedInstance = instance;

            Debug.Log(
                $"[PickupSpawnerNet][SERVER] Spawned pickup from spawner='{name}' " +
                $"spawnedNetId={_spawnedInstance.NetworkObjectId} drop='{drop.name}' item='{drop.ItemId}' qty={drop.Quantity}",
                this);
        }

        /// <summary>
        /// SERVER: Called by ResourceDrop when it is consumed/despawned.
        /// Starts the respawn timer if enabled.
        /// </summary>
        public void NotifyConsumedOrDespawned()
        {
            if (!IsServer)
                return;

            // IMPORTANT FIX:
            // From the spawner's perspective, the pickup is "gone" the moment it is consumed.
            // The object may remain IsSpawned for a short despawn delay (e.g., 0.75s).
            // If we don't clear this now, TrySpawnNow can be blocked.
            _spawnedInstance = null;

            if (respawnSeconds <= 0f)
            {
                Debug.Log($"[PickupSpawnerNet][SERVER] '{name}' notified consumed but respawnSeconds=0 (respawn disabled).", this);
                return;
            }

            Debug.Log($"[PickupSpawnerNet][SERVER] '{name}' notified consumed. Respawn in {respawnSeconds:0.0}s", this);

            // Cancel any existing timer to avoid multiple spawns.
            if (_respawnRoutine != null)
                StopCoroutine(_respawnRoutine);

            _respawnRoutine = StartCoroutine(ServerRespawnRoutine(respawnSeconds));
        }

        private IEnumerator ServerRespawnRoutine(float delay)
        {
            // Wait in scaled time (fine for gameplay). If you want pause-proof, swap to WaitForSecondsRealtime.
            yield return new WaitForSeconds(delay);

            _respawnRoutine = null;

            // Try spawn again (will succeed because we cleared _spawnedInstance at consume-time).
            TrySpawnNow();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (respawnSeconds < 0f) respawnSeconds = 0f;
        }
#endif
    }
}