using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// PickupSpawnerNet
    /// --------------------------------------------------------------------
    /// Server-authoritative respawn spawner for ResourceDrop items.
    ///
    /// Best practice for NGO:
    /// - Place spawner objects inside the target scene.
    /// - Do NOT hardcode scene names or MoveGameObjectToScene.
    /// - Server spawns pickup, clients only observe.
    ///
    /// Respawn:
    /// - When a pickup is consumed, it calls NotifyConsumedOrDespawned().
    /// - Spawner waits respawnSeconds then spawns again.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PickupSpawnerNet : NetworkBehaviour
    {
        [Header("Prefab")]
        [Tooltip("NetworkObject prefab to spawn (must be registered with NGO).")]
        [SerializeField] private NetworkObject pickupPrefab;

        [Header("Spawn Location")]
        [Tooltip("If true, spawns at this GameObject's transform (recommended).")]
        [SerializeField] private bool spawnAtThisTransform = true;

        [Tooltip("If spawnAtThisTransform is false, spawn at this position.")]
        [SerializeField] private Vector3 spawnPosition;

        [Tooltip("Spawn one pickup when the server spawns this spawner.")]
        [SerializeField] private bool spawnOnServerStart = true;

        [Header("Respawn")]
        [Tooltip("Seconds after consumption to respawn. Set to 0 to disable respawn.")]
        [Min(0f)]
        [SerializeField] private float respawnSeconds = 10f;

        // Track current instance to prevent duplicates.
        private NetworkObject _spawnedInstance;

        // Track coroutine so we can cancel/replace it safely.
        private Coroutine _respawnRoutine;

        public override void OnNetworkSpawn()
        {
            // This runs on both server & clients. Only server is allowed to spawn.
            if (!IsServer)
                return;

            if (spawnOnServerStart)
                TrySpawnNow();
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
                Debug.LogError("[PickupSpawnerNet] Missing pickupPrefab.", this);
                return;
            }

            // If we still have a valid spawned instance, do nothing.
            if (_spawnedInstance != null && _spawnedInstance.IsSpawned)
                return;

            Vector3 pos = spawnAtThisTransform ? transform.position : spawnPosition;
            Quaternion rot = spawnAtThisTransform ? transform.rotation : Quaternion.identity;

            // Instantiate on server.
            _spawnedInstance = Instantiate(pickupPrefab, pos, rot);

            // If the prefab has ResourceDrop, inject this spawner reference
            // so the pickup can notify us when it is consumed.
            if (_spawnedInstance.TryGetComponent<ResourceDrop>(out var pickup))
            {
                pickup.ServerSetSpawner(this);
            }

            // Spawn for all clients.
            _spawnedInstance.Spawn();
        }

        /// <summary>
        /// SERVER: Called by ResourceDrop when it is consumed/despawned.
        /// Starts the respawn timer if enabled.
        /// </summary>
        public void NotifyConsumedOrDespawned()
        {
            if (!IsServer)
                return;

            // No respawn configured.
            if (respawnSeconds <= 0f)
                return;

            // Cancel any existing timer to avoid multiple spawns.
            if (_respawnRoutine != null)
                StopCoroutine(_respawnRoutine);

            _respawnRoutine = StartCoroutine(ServerRespawnRoutine(respawnSeconds));
        }

        private IEnumerator ServerRespawnRoutine(float delay)
        {
            yield return new WaitForSeconds(delay);

            _respawnRoutine = null;
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