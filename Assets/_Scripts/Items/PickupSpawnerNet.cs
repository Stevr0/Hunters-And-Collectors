using System.Collections;
using HuntersAndCollectors.Bootstrap;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
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

        // Server-only currently active instance.
        private NetworkObject _spawnedInstance;

        // Server-only respawn timer.
        private Coroutine _respawnRoutine;

        private static bool HasServerAuthority()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsServer;
        }

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

            if (_respawnRoutine != null)
            {
                StopCoroutine(_respawnRoutine);
                _respawnRoutine = null;
            }

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
            if (!HasServerAuthority())
                return;

            if (pickupPrefab == null)
            {
                Debug.LogError($"[PickupSpawnerNet][SERVER] Missing pickupPrefab on spawner '{name}'.", this);
                return;
            }

            if (_spawnedInstance != null && _spawnedInstance.IsSpawned)
            {
                Debug.Log($"[PickupSpawnerNet][SERVER] '{name}' already has instance netId={_spawnedInstance.NetworkObjectId}", this);
                return;
            }

            Vector3 pos = spawnAtThisTransform ? transform.position : spawnPosition;
            Quaternion rot = spawnAtThisTransform ? transform.rotation : Quaternion.identity;

            var instance = Instantiate(pickupPrefab, pos, rot);
            Debug.Log($"[PickupSpawnerNet][SERVER] Instantiated pickup '{instance.name}' in scene '{instance.gameObject.scene.name}'.", instance);
            if (!Bootstrapper.MoveRuntimeGameplayObjectToScene(instance.gameObject, gameObject.scene.name, "PickupSpawnerNet"))
            {
                Destroy(instance.gameObject);
                return;
            }

            // ResourceDrop may be on root OR children.
            var drop = instance.GetComponentInChildren<ResourceDrop>(true);
            if (drop == null)
            {
                Debug.LogError(
                    $"[PickupSpawnerNet][SERVER] Spawned prefab '{pickupPrefab.name}' has NO ResourceDrop on root/children. " +
                    $"Spawner='{name}'. Respawn cannot work.",
                    instance);

                Destroy(instance.gameObject);
                return;
            }

            // Initialize interactable runtime state, then attach this spawner.
            drop.ServerInitialize(drop.Quantity, null);
            drop.ServerAttachSpawner(this);

            // Spawn for all clients.
            instance.Spawn(true);

            // Keep reference only after successful spawn.
            _spawnedInstance = instance;

            Debug.Log(
                $"[PickupSpawnerNet][SERVER] Respawn/Spawn complete spawner='{name}' spawnedNetId={_spawnedInstance.NetworkObjectId} item='{drop.ItemId}' qty={drop.Quantity}",
                this);
        }

        /// <summary>
        /// SERVER: Called by ResourceDrop when it is consumed.
        /// </summary>
        public void NotifyConsumedOrDespawned()
        {
            if (!HasServerAuthority())
                return;

            // Clear immediately so delayed despawn does not block respawn.
            _spawnedInstance = null;

            if (respawnSeconds <= 0f)
            {
                Debug.Log($"[PickupSpawnerNet][SERVER] NOTIFIED spawner='{name}' respawn disabled (respawnSeconds=0).", this);
                return;
            }

            Debug.Log($"[PickupSpawnerNet][SERVER] NOTIFIED spawner='{name}' scheduling respawn in {respawnSeconds:0.0}s", this);

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

