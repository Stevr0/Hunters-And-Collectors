using System;
using System.Collections;
using System.Collections.Generic;
using HuntersAndCollectors.Bootstrap;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Unified, server-authoritative actor spawning service.
    ///
    /// Actor model:
    /// - Any combat-capable entity is spawned through the same path (player/NPC/dummy/future actor).
    /// - Prefab + ActorDef + spawn transform are the only required concepts.
    ///
    /// Notes:
    /// - This class performs server-only NGO spawning.
    /// - Actor identity/stats initialization remains in ActorDefBinder and related actor components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorSpawner : MonoBehaviour
    {
        [Serializable]
        private sealed class ActorPrefabBinding
        {
            [Tooltip("ActorDef key used to select a prefab. Reference match is preferred.")]
            [SerializeField] private ActorDef actorDef;

            [Tooltip("Optional ActorId fallback key when the ActorDef reference is not the same instance.")]
            [SerializeField] private string actorId;

            [Tooltip("Network prefab to spawn for this actor entry.")]
            [SerializeField] private NetworkObject prefab;

            public ActorDef ActorDef => actorDef;
            public string ActorId => actorId;
            public NetworkObject Prefab => prefab;
        }

        [Serializable]
        private sealed class StartupSpawnEntry
        {
            [SerializeField] private ActorDef actorDef;
            [SerializeField] private string spawnPointId = string.Empty;
            [Min(1)]
            [SerializeField] private int count = 1;
            [Tooltip("Optional explicit fallback prefab for this entry.")]
            [SerializeField] private NetworkObject fallbackPrefab;

            public ActorDef ActorDef => actorDef;
            public string SpawnPointId => spawnPointId;
            public int Count => Mathf.Max(1, count);
            public NetworkObject FallbackPrefab => fallbackPrefab;
        }

        [Header("Prefabs")]
        [Tooltip("Optional ActorDef -> prefab map for projects with many actor prefabs.")]
        [SerializeField] private List<ActorPrefabBinding> actorPrefabBindings = new();

        [Header("Fallback Prefabs")]
        [SerializeField] private NetworkObject playerActorPrefab;
        [SerializeField] private NetworkObject dummyActorPrefab;
        [SerializeField] private NetworkObject npcActorPrefab;

        [Header("Rosters")]
        [Tooltip("Optional scene-level enemy roster. Spawn zones and spawn points can inherit this automatically.")]
        [SerializeField] private SceneEnemyRosterDef defaultSceneRoster;

        [Header("Spawn Points")]
        [Tooltip("Optional authored cache. If empty, ActorSpawnPoint components are discovered at runtime.")]
        [SerializeField] private List<ActorSpawnPoint> spawnPoints = new();

        [Header("Grounding")]
        [SerializeField] private bool snapToGround = true;
        [Tooltip("Ground sampling mask. Defaults to the 'Ground' layer when left empty.")]
        [SerializeField] private LayerMask groundMask;
        [Tooltip("Blocking mask used to push spawned actors upward out of overlaps. Ground is excluded by default.")]
        [SerializeField] private LayerMask obstacleMask;
        [Min(0.1f)]
        [SerializeField] private float groundRayStartHeight = 50f;
        [Min(1f)]
        [SerializeField] private float groundRayDistance = 200f;
        [Min(0f)]
        [SerializeField] private float groundContactSkin = 0.05f;
        [Min(0.01f)]
        [SerializeField] private float obstacleLiftStep = 0.25f;
        [Min(1)]
        [SerializeField] private int obstacleLiftMaxSteps = 24;

        [Header("Startup Spawns")]
        [Tooltip("If enabled, Bootstrapper (or another server startup flow) can ask this spawner to spawn configured world actors once.")]
        [SerializeField] private bool spawnConfiguredActorsOnServerStart = true;
        [SerializeField] private List<StartupSpawnEntry> startupSpawns = new();

        private readonly HashSet<string> loggedMissingSpawnPointIds = new();
        private readonly HashSet<string> loggedFallbackTypeWarnings = new();
        private readonly Dictionary<string, int> aliveByRespawnPoint = new();

        private bool startupSpawnsExecuted;
        private bool loggedGroundRayMiss;

        public SceneEnemyRosterDef DefaultSceneRoster => defaultSceneRoster;

        private void Awake()
        {
            EnsureLayerMasksInitialized();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            groundRayStartHeight = Mathf.Max(0.1f, groundRayStartHeight);
            groundRayDistance = Mathf.Max(1f, groundRayDistance);
            groundContactSkin = Mathf.Max(0f, groundContactSkin);
            obstacleLiftStep = Mathf.Max(0.01f, obstacleLiftStep);
            obstacleLiftMaxSteps = Mathf.Max(1, obstacleLiftMaxSteps);
            EnsureLayerMasksInitialized();
        }
#endif

        public void ServerSpawnConfiguredActorsOnce()
        {
            if (startupSpawnsExecuted)
                return;

            if (!spawnConfiguredActorsOnServerStart)
            {
                startupSpawnsExecuted = true;
                return;
            }

            if (!TryValidateServerSpawnContext())
                return;

            if (startupSpawns == null || startupSpawns.Count == 0)
            {
                startupSpawnsExecuted = true;
                return;
            }

            for (int i = 0; i < startupSpawns.Count; i++)
            {
                StartupSpawnEntry entry = startupSpawns[i];
                if (entry == null)
                    continue;

                for (int n = 0; n < entry.Count; n++)
                {
                    NetworkObject spawned = ServerSpawnActorByDef(entry.ActorDef, entry.SpawnPointId, entry.FallbackPrefab, null);
                    if (spawned == null)
                    {
                        string actorLabel = entry.ActorDef != null ? entry.ActorDef.name : "<null ActorDef>";
                        Debug.LogWarning($"[ActorSpawner] Startup spawn failed for actorDef='{actorLabel}' spawnPoint='{entry.SpawnPointId}'.", this);
                    }
                }
            }

            startupSpawnsExecuted = true;
        }

        public NetworkObject ServerSpawnPlayerActor(ulong ownerClientId, ActorDef actorDef, string spawnPointId = "")
        {
            ResolveSpawnTransform(
                spawnPointId,
                p => p != null && p.IsPlayerSpawn,
                "player",
                out Vector3 position,
                out Quaternion rotation,
                out string pointLabel,
                out ActorDef pointDefaultDef,
                out ActorSpawnPoint spawnPoint);

            ActorDef resolvedDef = actorDef != null ? actorDef : pointDefaultDef;
            NetworkObject prefab = ResolvePrefabForActor(resolvedDef, playerActorPrefab);
            NetworkObject spawned = ServerSpawnActor(prefab, resolvedDef, position, rotation, ownerClientId);
            RegisterRespawnTracker(spawned, spawnPoint, resolvedDef, prefab, ownerClientId);
            LogSpawnResult(spawned, resolvedDef, pointLabel);
            return spawned;
        }

        public NetworkObject ServerSpawnNpcActor(ActorDef actorDef, string spawnPointId = "")
        {
            ResolveSpawnTransform(
                spawnPointId,
                p => p != null && p.IsNpcSpawn,
                "npc",
                out Vector3 position,
                out Quaternion rotation,
                out string pointLabel,
                out ActorDef pointDefaultDef,
                out ActorSpawnPoint spawnPoint);

            ActorDef resolvedDef = actorDef != null ? actorDef : pointDefaultDef;
            if (resolvedDef == null && spawnPoint != null)
                resolvedDef = spawnPoint.ResolveActorDef(defaultSceneRoster, logWarnings: true);

            NetworkObject prefab = ResolvePrefabForActor(resolvedDef, npcActorPrefab);
            NetworkObject spawned = ServerSpawnActor(prefab, resolvedDef, position, rotation, null);
            RegisterRespawnTracker(spawned, spawnPoint, resolvedDef, prefab, null);
            LogSpawnResult(spawned, resolvedDef, pointLabel);
            return spawned;
        }

        public NetworkObject ServerSpawnDummyActor(ActorDef actorDef, string spawnPointId = "")
        {
            ResolveSpawnTransform(
                spawnPointId,
                p => p != null && p.IsDummySpawn,
                "dummy",
                out Vector3 position,
                out Quaternion rotation,
                out string pointLabel,
                out ActorDef pointDefaultDef,
                out ActorSpawnPoint spawnPoint);

            ActorDef resolvedDef = actorDef != null ? actorDef : pointDefaultDef;
            NetworkObject prefab = ResolvePrefabForActor(resolvedDef, dummyActorPrefab);
            NetworkObject spawned = ServerSpawnActor(prefab, resolvedDef, position, rotation, null);
            RegisterRespawnTracker(spawned, spawnPoint, resolvedDef, prefab, null);
            LogSpawnResult(spawned, resolvedDef, pointLabel);
            return spawned;
        }

        public NetworkObject ServerSpawnActorByDef(ActorDef actorDef, string spawnPointId = "", NetworkObject fallbackPrefab = null, ulong? ownerClientId = null)
        {
            ResolveSpawnTransform(
                spawnPointId,
                _ => true,
                "actor",
                out Vector3 position,
                out Quaternion rotation,
                out string pointLabel,
                out ActorDef pointDefaultDef,
                out ActorSpawnPoint spawnPoint);

            ActorDef resolvedDef = actorDef != null ? actorDef : pointDefaultDef;
            if (resolvedDef == null && spawnPoint != null)
                resolvedDef = spawnPoint.ResolveActorDef(defaultSceneRoster, logWarnings: true);

            NetworkObject prefab = ResolvePrefabForActor(resolvedDef, fallbackPrefab);
            NetworkObject spawned = ServerSpawnActor(prefab, resolvedDef, position, rotation, ownerClientId);
            RegisterRespawnTracker(spawned, spawnPoint, resolvedDef, prefab, ownerClientId);
            LogSpawnResult(spawned, resolvedDef, pointLabel);
            return spawned;
        }

        public NetworkObject ServerSpawnActor(NetworkObject prefab, ActorDef actorDef, Vector3 position, Quaternion rotation, ulong? ownerClientId = null)
        {
            if (!TryValidateServerSpawnContext())
                return null;

            if (prefab == null)
            {
                Debug.LogWarning("[ActorSpawner] Missing actor prefab. Spawn aborted.", this);
                return null;
            }

            Vector3 spawnPosition = ResolveGroundedSpawnPosition(prefab, position, rotation);
            NetworkObject instance = Instantiate(prefab, spawnPosition, rotation);
            if (instance == null)
            {
                Debug.LogWarning("[ActorSpawner] Failed to instantiate actor prefab.", this);
                return null;
            }

            string targetSceneName = gameObject.scene.IsValid() ? gameObject.scene.name : string.Empty;
            if (string.Equals(targetSceneName, "SCN_Bootstrap", StringComparison.Ordinal))
                targetSceneName = Bootstrapper.ResolveDefaultGameplaySceneName();

            Debug.Log($"[ActorSpawner] Instantiated actor prefab '{instance.name}' in scene '{instance.gameObject.scene.name}'. Target scene='{targetSceneName}'.", instance);
            if (!Bootstrapper.MoveRuntimeGameplayObjectToScene(instance.gameObject, targetSceneName, "ActorSpawner"))
            {
                Destroy(instance.gameObject);
                return null;
            }

            ActorDefBinder binder = instance.GetComponent<ActorDefBinder>();
            if (binder != null)
            {
                if (actorDef != null)
                    binder.SetActorDefRuntime(actorDef);
            }
            else
            {
                Debug.LogWarning($"[ActorSpawner] Spawned actor '{instance.name}' is missing ActorDefBinder.", instance);
            }

            ConfigureRuntimeAi(instance, actorDef);

            if (ownerClientId.HasValue)
                instance.SpawnWithOwnership(ownerClientId.Value);
            else
                instance.Spawn();

            return instance;
        }

        public NetworkObject ServerSpawnActor(ActorSpawnRequest request)
        {
            NetworkObject requestPrefab = request.Prefab;

            if (request.UseExplicitTransform)
            {
                if (requestPrefab == null)
                    requestPrefab = ResolvePrefabForActor(request.ActorDef, null);

                NetworkObject explicitSpawned = ServerSpawnActor(
                    requestPrefab,
                    request.ActorDef,
                    request.Position,
                    request.Rotation,
                    request.HasOwner ? request.OwnerClientId : (ulong?)null);

                LogSpawnResult(explicitSpawned, request.ActorDef, "<explicit>");
                return explicitSpawned;
            }

            ResolveSpawnTransform(
                request.SpawnPointId,
                _ => true,
                "actor",
                out Vector3 position,
                out Quaternion rotation,
                out _,
                out ActorDef pointDefaultDef,
                out ActorSpawnPoint spawnPoint);

            ActorDef resolvedDef = request.ActorDef != null ? request.ActorDef : pointDefaultDef;
            if (resolvedDef == null && spawnPoint != null)
                resolvedDef = spawnPoint.ResolveActorDef(defaultSceneRoster, logWarnings: true);

            if (requestPrefab == null)
                requestPrefab = ResolvePrefabForActor(resolvedDef, null);

            NetworkObject spawned = ServerSpawnActor(
                requestPrefab,
                resolvedDef,
                position,
                rotation,
                request.HasOwner ? request.OwnerClientId : (ulong?)null);

            RegisterRespawnTracker(spawned, spawnPoint, resolvedDef, requestPrefab, request.HasOwner ? request.OwnerClientId : (ulong?)null);
            return spawned;
        }

        public bool TryGetPlayerSpawnTransform(string spawnPointId, out Vector3 position, out Quaternion rotation)
        {
            return TryGetSpawnTransformByType(spawnPointId, p => p != null && p.IsPlayerSpawn, "player", out position, out rotation);
        }

        public bool TryGetNpcSpawnTransform(string spawnPointId, out Vector3 position, out Quaternion rotation)
        {
            return TryGetSpawnTransformByType(spawnPointId, p => p != null && p.IsNpcSpawn, "npc", out position, out rotation);
        }

        public bool TryGetDummySpawnTransform(string spawnPointId, out Vector3 position, out Quaternion rotation)
        {
            return TryGetSpawnTransformByType(spawnPointId, p => p != null && p.IsDummySpawn, "dummy", out position, out rotation);
        }

        private bool TryGetSpawnTransformByType(
            string spawnPointId,
            Predicate<ActorSpawnPoint> typePredicate,
            string actorTypeLabel,
            out Vector3 position,
            out Quaternion rotation)
        {
            ResolveSpawnTransform(
                spawnPointId,
                typePredicate,
                actorTypeLabel,
                out position,
                out rotation,
                out _,
                out _,
                out ActorSpawnPoint resolvedSpawnPoint);

            return resolvedSpawnPoint != null;
        }

        private void ResolveSpawnTransform(
            string spawnPointId,
            Predicate<ActorSpawnPoint> typePredicate,
            string actorTypeLabel,
            out Vector3 position,
            out Quaternion rotation,
            out string pointLabel,
            out ActorDef defaultActorDef,
            out ActorSpawnPoint resolvedSpawnPoint)
        {
            ActorSpawnPoint spawnPoint = null;

            if (!string.IsNullOrWhiteSpace(spawnPointId))
            {
                if (!TryFindSpawnPoint(spawnPointId, out spawnPoint))
                {
                    if (loggedMissingSpawnPointIds.Add(spawnPointId))
                        Debug.LogWarning($"[ActorSpawner] Spawn point '{spawnPointId}' not found, using fallback position.", this);
                }
            }

            if (spawnPoint == null)
                spawnPoint = FindBestSpawnPoint(typePredicate);

            if (spawnPoint != null)
            {
                position = spawnPoint.transform.position;
                rotation = spawnPoint.transform.rotation;
                pointLabel = string.IsNullOrWhiteSpace(spawnPoint.SpawnPointId) ? "<unnamed>" : spawnPoint.SpawnPointId;
                defaultActorDef = spawnPoint.DefaultActorDef;
                resolvedSpawnPoint = spawnPoint;
                return;
            }

            position = transform.position;
            rotation = transform.rotation;
            pointLabel = "<fallback>";
            defaultActorDef = null;
            resolvedSpawnPoint = null;

            if (loggedFallbackTypeWarnings.Add(actorTypeLabel ?? "actor"))
                Debug.LogWarning($"[ActorSpawner] No {actorTypeLabel} spawn point found, using spawner transform fallback.", this);
        }

        private void RegisterRespawnTracker(NetworkObject spawned, ActorSpawnPoint spawnPoint, ActorDef actorDef, NetworkObject fallbackPrefab, ulong? ownerClientId)
        {
            if (spawned == null || spawnPoint == null || !spawnPoint.EnableRespawn)
                return;

            if (ownerClientId.HasValue)
                return;

            string key = GetRespawnPointKey(spawnPoint);
            if (!TryIncrementAlive(key, spawnPoint.MaxAliveFromPoint))
                return;

            StartCoroutine(CoMonitorRespawn(spawned, spawnPoint, key, actorDef, fallbackPrefab));
        }

        private IEnumerator CoMonitorRespawn(NetworkObject trackedObject, ActorSpawnPoint spawnPoint, string pointKey, ActorDef actorDef, NetworkObject fallbackPrefab)
        {
            while (trackedObject != null && trackedObject.IsSpawned)
                yield return null;

            DecrementAlive(pointKey);

            if (!IsServerReadyForSpawn() || spawnPoint == null || !spawnPoint.EnableRespawn)
                yield break;

            float delay = Mathf.Max(0f, spawnPoint.RespawnDelaySeconds);
            if (delay > 0f)
                yield return new WaitForSeconds(delay);

            while (IsServerReadyForSpawn() && spawnPoint != null && spawnPoint.EnableRespawn)
            {
                int alive = GetAlive(pointKey);
                if (alive < spawnPoint.MaxAliveFromPoint)
                {
                    ServerSpawnActorByDef(actorDef, spawnPoint.SpawnPointId, fallbackPrefab, null);
                    yield break;
                }

                yield return new WaitForSeconds(1f);
            }
        }

        private static string GetRespawnPointKey(ActorSpawnPoint point)
        {
            if (point == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(point.SpawnPointId))
                return point.SpawnPointId;

            return $"__spawnpoint_{point.GetInstanceID()}";
        }

        private bool TryIncrementAlive(string key, int maxAlive)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            int current = GetAlive(key);
            if (current >= Mathf.Max(1, maxAlive))
                return false;

            aliveByRespawnPoint[key] = current + 1;
            return true;
        }

        private int GetAlive(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return 0;

            return aliveByRespawnPoint.TryGetValue(key, out int value) ? Mathf.Max(0, value) : 0;
        }

        private void DecrementAlive(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            int current = GetAlive(key);
            if (current <= 1)
            {
                aliveByRespawnPoint.Remove(key);
                return;
            }

            aliveByRespawnPoint[key] = current - 1;
        }

        private bool IsServerReadyForSpawn()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        private Vector3 ResolveGroundedSpawnPosition(NetworkObject prefab, Vector3 desiredPosition, Quaternion rotation)
        {
            if (!snapToGround || prefab == null)
                return desiredPosition;

            EnsureLayerMasksInitialized();

            if (!TrySampleGround(desiredPosition, out RaycastHit groundHit))
            {
                if (!loggedGroundRayMiss)
                {
                    loggedGroundRayMiss = true;
                    Debug.LogWarning("[ActorSpawner] Ground snap missed ground mask. Using requested spawn position.", this);
                }

                return desiredPosition;
            }

            float bottomOffset = GetBottomOffsetFromRoot(prefab);
            Vector3 grounded = desiredPosition;
            grounded.y = groundHit.point.y - bottomOffset + groundContactSkin;

            return LiftOutOfObstacles(prefab, grounded, rotation);
        }

        private bool TrySampleGround(Vector3 desiredPosition, out RaycastHit hit)
        {
            Vector3 origin = desiredPosition + Vector3.up * groundRayStartHeight;
            return Physics.Raycast(origin, Vector3.down, out hit, groundRayDistance, groundMask, QueryTriggerInteraction.Ignore);
        }

        private float GetBottomOffsetFromRoot(NetworkObject prefab)
        {
            CharacterController cc = prefab.GetComponentInChildren<CharacterController>(true);
            if (cc != null)
                return cc.center.y - (cc.height * 0.5f);

            CapsuleCollider capsule = prefab.GetComponentInChildren<CapsuleCollider>(true);
            if (capsule != null && capsule.direction == 1)
                return capsule.center.y - (capsule.height * 0.5f);

            Collider col = prefab.GetComponentInChildren<Collider>(true);
            if (col != null)
                return col.bounds.min.y - prefab.transform.position.y;

            return 0f;
        }

        private Vector3 LiftOutOfObstacles(NetworkObject prefab, Vector3 candidate, Quaternion rotation)
        {
            if (!TryBuildCapsuleProbe(prefab, out Vector3 center, out float radius, out float halfHeight))
                return candidate;

            Vector3 result = candidate;
            float centerToCap = Mathf.Max(0f, halfHeight - radius);

            for (int i = 0; i < obstacleLiftMaxSteps; i++)
            {
                Vector3 worldCenter = result + (rotation * center);
                Vector3 p1 = worldCenter + Vector3.up * centerToCap;
                Vector3 p2 = worldCenter - Vector3.up * centerToCap;

                bool blocked = Physics.CheckCapsule(p1, p2, radius, obstacleMask, QueryTriggerInteraction.Ignore);
                if (!blocked)
                    return result;

                result.y += obstacleLiftStep;
            }

            return result;
        }

        private bool TryBuildCapsuleProbe(NetworkObject prefab, out Vector3 center, out float radius, out float halfHeight)
        {
            CharacterController cc = prefab.GetComponentInChildren<CharacterController>(true);
            if (cc != null)
            {
                center = cc.center;
                radius = Mathf.Max(0.05f, cc.radius);
                halfHeight = Mathf.Max(radius, cc.height * 0.5f);
                return true;
            }

            CapsuleCollider capsule = prefab.GetComponentInChildren<CapsuleCollider>(true);
            if (capsule != null && capsule.direction == 1)
            {
                center = capsule.center;
                radius = Mathf.Max(0.05f, capsule.radius);
                halfHeight = Mathf.Max(radius, capsule.height * 0.5f);
                return true;
            }

            Collider col = prefab.GetComponentInChildren<Collider>(true);
            if (col != null)
            {
                Bounds b = col.bounds;
                center = b.center - prefab.transform.position;
                float vertical = Mathf.Max(0.1f, b.size.y * 0.5f);
                float horizontal = Mathf.Max(0.1f, Mathf.Max(b.extents.x, b.extents.z));
                radius = Mathf.Min(horizontal, vertical);
                halfHeight = vertical;
                return true;
            }

            center = Vector3.zero;
            radius = 0f;
            halfHeight = 0f;
            return false;
        }

        private void EnsureLayerMasksInitialized()
        {
            if (groundMask.value == 0)
            {
                int groundLayer = LayerMask.NameToLayer("Ground");
                if (groundLayer >= 0)
                    groundMask = 1 << groundLayer;
            }

            if (obstacleMask.value == 0)
            {
                obstacleMask = Physics.DefaultRaycastLayers;

                int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
                if (ignoreRaycastLayer >= 0)
                    obstacleMask &= ~(1 << ignoreRaycastLayer);

                int groundLayer = LayerMask.NameToLayer("Ground");
                if (groundLayer >= 0)
                    obstacleMask &= ~(1 << groundLayer);
            }
        }

        private void LogSpawnResult(NetworkObject spawned, ActorDef actorDef, string pointLabel)
        {
            if (spawned == null)
                return;

            string actorLabel = actorDef != null && !string.IsNullOrWhiteSpace(actorDef.DisplayName)
                ? actorDef.DisplayName
                : spawned.name;

            Debug.Log($"[ActorSpawner] Spawned actor '{actorLabel}' at point '{pointLabel}'.", spawned);
        }

        private NetworkObject ResolvePrefabForActor(ActorDef actorDef, NetworkObject fallbackPrefab)
        {
            if (actorDef != null && actorDef.Prefab != null && actorDef.Prefab.TryGetComponent(out NetworkObject prefabFromDef))
                return prefabFromDef;

            if (actorDef == null || actorPrefabBindings == null || actorPrefabBindings.Count == 0)
                return fallbackPrefab;

            for (int i = 0; i < actorPrefabBindings.Count; i++)
            {
                ActorPrefabBinding binding = actorPrefabBindings[i];
                if (binding == null || binding.Prefab == null)
                    continue;

                if (binding.ActorDef == actorDef)
                    return binding.Prefab;
            }

            if (!string.IsNullOrWhiteSpace(actorDef.ActorId))
            {
                for (int i = 0; i < actorPrefabBindings.Count; i++)
                {
                    ActorPrefabBinding binding = actorPrefabBindings[i];
                    if (binding == null || binding.Prefab == null || string.IsNullOrWhiteSpace(binding.ActorId))
                        continue;

                    if (string.Equals(binding.ActorId, actorDef.ActorId, StringComparison.Ordinal))
                        return binding.Prefab;
                }
            }

            return fallbackPrefab;
        }

        private static void ConfigureRuntimeAi(NetworkObject instance, ActorDef actorDef)
        {
            if (instance == null || actorDef == null || actorDef.AiDef == null)
                return;

            ActorAIController ai = instance.GetComponent<ActorAIController>();
            if (ai == null)
                ai = instance.gameObject.AddComponent<ActorAIController>();

            ai.SetRuntimeDef(actorDef.AiDef);
        }

        private bool TryValidateServerSpawnContext()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null)
            {
                Debug.LogWarning("[ActorSpawner] NetworkManager/SpawnManager missing. Spawn aborted.", this);
                return false;
            }

            if (!manager.IsServer)
            {
                Debug.LogWarning("[ActorSpawner] Spawn called on non-server instance. Spawn aborted.", this);
                return false;
            }

            return true;
        }

        private void EnsureSpawnPointCache()
        {
            if (spawnPoints != null && spawnPoints.Count > 0)
            {
                for (int i = spawnPoints.Count - 1; i >= 0; i--)
                {
                    if (spawnPoints[i] == null)
                        spawnPoints.RemoveAt(i);
                }

                if (spawnPoints.Count > 0)
                    return;
            }

            ActorSpawnPoint[] discovered = FindObjectsByType<ActorSpawnPoint>(FindObjectsSortMode.None);
            if (spawnPoints == null)
                spawnPoints = new List<ActorSpawnPoint>(discovered.Length);
            else
                spawnPoints.Clear();

            if (discovered == null || discovered.Length == 0)
                return;

            for (int i = 0; i < discovered.Length; i++)
            {
                if (discovered[i] != null)
                    spawnPoints.Add(discovered[i]);
            }
        }

        private bool TryFindSpawnPoint(string spawnPointId, out ActorSpawnPoint spawnPoint)
        {
            EnsureSpawnPointCache();

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                ActorSpawnPoint p = spawnPoints[i];
                if (p == null)
                    continue;

                if (!string.Equals(p.SpawnPointId, spawnPointId, StringComparison.OrdinalIgnoreCase))
                    continue;

                spawnPoint = p;
                return true;
            }

            spawnPoint = null;
            return false;
        }

        private ActorSpawnPoint FindBestSpawnPoint(Predicate<ActorSpawnPoint> predicate)
        {
            EnsureSpawnPointCache();

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                ActorSpawnPoint p = spawnPoints[i];
                if (p == null)
                    continue;

                if (predicate == null || predicate(p))
                    return p;
            }

            return null;
        }
    }
}

