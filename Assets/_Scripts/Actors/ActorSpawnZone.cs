using System.Collections.Generic;
using HuntersAndCollectors.Combat;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// First-pass server-authoritative world spawn zone for hostile/NPC/wildlife actors.
    ///
    /// High-level behavior:
    /// - Scene authors place this component in the world and configure its spawn table.
    /// - Only the server/host evaluates activation, chooses spawn entries, and creates actors.
    /// - Clients never request, own, or simulate world spawning.
    /// - While at least one player is within activation radius, the zone can fill toward targetAliveCount.
    /// - When respawn is enabled, the zone waits for a respawn delay after losses before refilling.
    /// - When respawn is disabled, the zone only tries to build its initial population once.
    ///
    /// This intentionally stays lightweight:
    /// - No persistence for individual spawned NPCs in this first pass.
    /// - No wave/encounter director logic.
    /// - No client prediction or client authority.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorSpawnZone : MonoBehaviour
    {
        private const float MinimumRadius = 0.1f;
        private const float DefaultServerTickInterval = 0.5f;
        private const float GroundProbeStartHeight = 50f;
        private const float GroundProbeDistance = 200f;
        private const float GroundProbeSkin = 0.05f;

        [Header("Identity")]
        [SerializeField] private string zoneId = string.Empty;

        [Header("Activation")]
        [Min(MinimumRadius)]
        [SerializeField] private float activationRadius = 20f;

        [Header("Spawn Rules")]
        [Min(1)]
        [SerializeField] private int targetAliveCount = 3;
        [SerializeField] private SpawnEntry[] spawnTable;

        [Header("Optional Roster")]
        [SerializeField] private SceneEnemyRosterDef rosterOverride;
        [SerializeField] private string rosterTagOverride = string.Empty;
        [SerializeField] private bool useSceneRoster = true;
        [SerializeField] private bool includeEliteEntries = true;
        [SerializeField] private bool includeBossEntries = false;
        [SerializeField] private bool useNightEntries;

        [Header("Spawn Area")]
        [Min(0f)]
        [SerializeField] private float spawnScatterRadius = 5f;
        [Min(0f)]
        [SerializeField] private float minSpawnSeparation = 1.5f;
        [SerializeField] private bool useRaycastToGround = true;
        [SerializeField] private LayerMask groundMask;

        [Header("Respawn")]
        [SerializeField] private bool enableRespawn = true;
        [Min(0f)]
        [SerializeField] private float respawnDelaySeconds = 10f;

        [Header("Optional Limits")]
        [Min(1)]
        [SerializeField] private int maxSpawnAttemptsPerCycle = 10;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private bool verboseLogs = false;

        private readonly List<NetworkObject> spawnedActors = new(16);
        private readonly List<SpawnEntry> resolvedSpawnEntries = new(32);

        private ActorSpawner actorSpawner;
        private bool runtimeInitialized;
        private bool zoneActive;
        private bool initialPopulationReachedTarget;
        private float nextServerTickTime;
        private float nextRespawnAllowedTime;
        private int lastKnownAliveCount;
        private bool loggedMissingSpawner;
        private bool loggedInvalidTable;

        public string ZoneId => zoneId;

        private void Awake()
        {
            EnsureGroundMaskInitialized();
        }

        private void Start()
        {
            TryInitializeRuntimeState();
        }

        private void Update()
        {
            if (!IsServerReady())
                return;

            if (!runtimeInitialized && !TryInitializeRuntimeState())
                return;

            if (Time.time < nextServerTickTime)
                return;

            nextServerTickTime = Time.time + DefaultServerTickInterval;
            ServerTick();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            zoneId = zoneId == null ? string.Empty : zoneId.Trim();
            rosterTagOverride = rosterTagOverride == null ? string.Empty : rosterTagOverride.Trim();
            activationRadius = Mathf.Max(MinimumRadius, activationRadius);
            targetAliveCount = Mathf.Max(1, targetAliveCount);
            spawnScatterRadius = Mathf.Max(0f, spawnScatterRadius);
            minSpawnSeparation = Mathf.Max(0f, minSpawnSeparation);
            respawnDelaySeconds = Mathf.Max(0f, respawnDelaySeconds);
            maxSpawnAttemptsPerCycle = Mathf.Max(1, maxSpawnAttemptsPerCycle);
            EnsureGroundMaskInitialized();
        }
#endif

        private void ServerTick()
        {
            int aliveCount = PruneTrackedActorsAndCountAlive();
            bool playerNearby = IsAnyLivingPlayerWithinActivationRadius();

            if (playerNearby != zoneActive)
            {
                zoneActive = playerNearby;

                if (verboseLogs)
                {
                    string state = zoneActive ? "activated" : "deactivated";
                    Debug.Log($"[ActorSpawnZone] Zone '{GetZoneLabel()}' {state}.", this);
                }
            }

            if (!zoneActive)
            {
                lastKnownAliveCount = aliveCount;
                return;
            }

            HandleRespawnTimerAfterLoss(aliveCount);

            if (!ShouldAttemptSpawn(aliveCount))
            {
                lastKnownAliveCount = aliveCount;
                return;
            }

            if (!TryBuildResolvedSpawnEntries())
            {
                if (!loggedInvalidTable)
                {
                    loggedInvalidTable = true;
                    Debug.LogWarning($"[ActorSpawnZone] Zone '{GetZoneLabel()}' has no valid spawn entries. Spawn skipped.", this);
                }

                lastKnownAliveCount = aliveCount;
                return;
            }

            loggedInvalidTable = false;

            SpawnUntilTarget(aliveCount, out bool exhaustedAttempts);
            int finalAliveCount = PruneTrackedActorsAndCountAlive();

            if (finalAliveCount >= targetAliveCount)
                initialPopulationReachedTarget = true;

            if (exhaustedAttempts && finalAliveCount < targetAliveCount)
            {
                Debug.LogWarning(
                    $"[ActorSpawnZone] Zone '{GetZoneLabel()}' could not reach target alive count after {maxSpawnAttemptsPerCycle} spawn attempts.",
                    this);
            }

            lastKnownAliveCount = finalAliveCount;
        }

        private bool TryInitializeRuntimeState()
        {
            if (runtimeInitialized)
                return true;

            if (!IsServerReady())
                return false;

            if (actorSpawner == null)
                actorSpawner = FindFirstObjectByType<ActorSpawner>();

            if (actorSpawner == null)
            {
                if (!loggedMissingSpawner)
                {
                    loggedMissingSpawner = true;
                    Debug.LogWarning($"[ActorSpawnZone] Zone '{GetZoneLabel()}' is waiting for an ActorSpawner in the scene.", this);
                }

                return false;
            }

            loggedMissingSpawner = false;
            spawnedActors.Clear();
            resolvedSpawnEntries.Clear();
            zoneActive = false;
            initialPopulationReachedTarget = false;
            nextServerTickTime = 0f;
            nextRespawnAllowedTime = 0f;
            lastKnownAliveCount = 0;
            runtimeInitialized = true;
            return true;
        }

        private bool IsServerReady()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        private int PruneTrackedActorsAndCountAlive()
        {
            for (int i = spawnedActors.Count - 1; i >= 0; i--)
            {
                NetworkObject tracked = spawnedActors[i];
                if (!IsTrackedActorAlive(tracked))
                    spawnedActors.RemoveAt(i);
            }

            return spawnedActors.Count;
        }

        private static bool IsTrackedActorAlive(NetworkObject tracked)
        {
            if (tracked == null)
                return false;

            if (!tracked.IsSpawned)
                return false;

            GameObject go = tracked.gameObject;
            if (go == null || !go.activeInHierarchy)
                return false;

            HealthNet health = tracked.GetComponent<HealthNet>();
            if (health != null && health.CurrentHealth <= 0)
                return false;

            return true;
        }

        private bool IsAnyLivingPlayerWithinActivationRadius()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null)
                return false;

            float radiusSq = activationRadius * activationRadius;

            foreach (KeyValuePair<ulong, NetworkClient> kvp in manager.ConnectedClients)
            {
                NetworkClient client = kvp.Value;
                if (client == null)
                    continue;

                NetworkObject playerObject = client.PlayerObject;
                if (playerObject == null || !playerObject.IsSpawned)
                    continue;

                HealthNet playerHealth = playerObject.GetComponent<HealthNet>();
                if (playerHealth != null && playerHealth.CurrentHealth <= 0)
                    continue;

                Vector3 delta = playerObject.transform.position - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radiusSq)
                    return true;
            }

            return false;
        }

        private void HandleRespawnTimerAfterLoss(int aliveCount)
        {
            if (!enableRespawn)
                return;

            if (!initialPopulationReachedTarget)
                return;

            if (aliveCount >= lastKnownAliveCount)
                return;

            nextRespawnAllowedTime = Time.time + respawnDelaySeconds;

            if (verboseLogs)
            {
                Debug.Log(
                    $"[ActorSpawnZone] Zone '{GetZoneLabel()}' waiting {respawnDelaySeconds:0.##}s before refill.",
                    this);
            }
        }

        private bool ShouldAttemptSpawn(int aliveCount)
        {
            if (aliveCount >= targetAliveCount)
                return false;

            if (!enableRespawn)
                return !initialPopulationReachedTarget;

            if (!initialPopulationReachedTarget)
                return true;

            if (Time.time < nextRespawnAllowedTime)
                return false;

            return true;
        }

        private bool TryBuildResolvedSpawnEntries()
        {
            resolvedSpawnEntries.Clear();

            SceneEnemyRosterDef roster = ResolveRoster();
            if (roster != null)
            {
                roster.AppendMatches(
                    resolvedSpawnEntries,
                    ResolveRosterTag(),
                    RosterSpawnUsage.Zone,
                    includeEliteEntries,
                    includeBossEntries,
                    useNightEntries);
            }

            if (resolvedSpawnEntries.Count == 0 && spawnTable != null && spawnTable.Length > 0)
            {
                for (int i = 0; i < spawnTable.Length; i++)
                {
                    SpawnEntry entry = spawnTable[i];
                    if (IsSpawnEntryEligible(entry))
                        resolvedSpawnEntries.Add(entry);
                }
            }

            return resolvedSpawnEntries.Count > 0;
        }

        private int SpawnUntilTarget(int startingAliveCount, out bool exhaustedAttempts)
        {
            int aliveCount = startingAliveCount;
            int attempts = 0;
            int spawned = 0;
            exhaustedAttempts = false;

            while (aliveCount < targetAliveCount && attempts < maxSpawnAttemptsPerCycle)
            {
                attempts++;

                if (!TrySelectSpawnEntry(out SpawnEntry selectedEntry))
                    break;

                int groupSize = selectedEntry.ResolveGroupSize();
                for (int groupIndex = 0; groupIndex < groupSize && aliveCount < targetAliveCount; groupIndex++)
                {
                    if (!TryFindSpawnPosition(out Vector3 spawnPosition))
                        continue;

                    ActorSpawnRequest request = new ActorSpawnRequest
                    {
                        ActorDef = selectedEntry.actorDef,
                        Position = spawnPosition,
                        Rotation = transform.rotation,
                        UseExplicitTransform = true,
                        HasOwner = false,
                        OwnerClientId = default,
                        Prefab = null,
                        SpawnPointId = string.Empty
                    };

                    NetworkObject spawnedObject = actorSpawner.ServerSpawnActor(request);
                    if (spawnedObject == null)
                        continue;

                    spawnedActors.Add(spawnedObject);
                    aliveCount++;
                    spawned++;

                    string actorLabel = selectedEntry.actorDef != null && !string.IsNullOrWhiteSpace(selectedEntry.actorDef.DisplayName)
                        ? selectedEntry.actorDef.DisplayName
                        : selectedEntry.actorDef != null ? selectedEntry.actorDef.name : spawnedObject.name;

                    Debug.Log(
                        $"[ActorSpawnZone] Zone '{GetZoneLabel()}' spawned '{actorLabel}' at ({spawnPosition.x:0.##}, {spawnPosition.y:0.##}, {spawnPosition.z:0.##}).",
                        this);
                }
            }

            exhaustedAttempts = attempts >= maxSpawnAttemptsPerCycle && aliveCount < targetAliveCount;
            return spawned;
        }

        private bool TrySelectSpawnEntry(out SpawnEntry selected)
        {
            selected = default;

            if (resolvedSpawnEntries.Count == 0)
                return false;

            float totalWeight = 0f;
            for (int i = 0; i < resolvedSpawnEntries.Count; i++)
            {
                if (!IsSpawnEntryEligible(resolvedSpawnEntries[i]))
                    continue;

                totalWeight += resolvedSpawnEntries[i].weight;
            }

            if (totalWeight <= 0f)
                return false;

            float choice = Random.value * totalWeight;
            float cursor = 0f;

            for (int i = 0; i < resolvedSpawnEntries.Count; i++)
            {
                SpawnEntry entry = resolvedSpawnEntries[i];
                if (!IsSpawnEntryEligible(entry))
                    continue;

                cursor += entry.weight;
                if (choice <= cursor)
                {
                    selected = entry;
                    return true;
                }
            }

            for (int i = resolvedSpawnEntries.Count - 1; i >= 0; i--)
            {
                if (!resolvedSpawnEntries[i].IsEligible)
                    continue;

                selected = resolvedSpawnEntries[i];
                return true;
            }

            return false;
        }

        private bool IsSpawnEntryEligible(SpawnEntry entry)
        {
            return entry.IsEligible && entry.actorDef.AllowZoneSpawning && entry.AllowsTimeOfDay(useNightEntries);
        }

        private bool TryFindSpawnPosition(out Vector3 spawnPosition)
        {
            spawnPosition = transform.position;

            for (int attempt = 0; attempt < maxSpawnAttemptsPerCycle; attempt++)
            {
                Vector3 candidate = transform.position;

                if (spawnScatterRadius > 0f)
                {
                    Vector2 randomOffset = Random.insideUnitCircle * spawnScatterRadius;
                    candidate.x += randomOffset.x;
                    candidate.z += randomOffset.y;
                }

                if (useRaycastToGround && TrySampleGround(candidate, out RaycastHit groundHit))
                    candidate.y = groundHit.point.y + GroundProbeSkin;

                if (!IsFarEnoughFromTrackedActors(candidate))
                    continue;

                spawnPosition = candidate;
                return true;
            }

            return false;
        }

        private bool TrySampleGround(Vector3 candidate, out RaycastHit hit)
        {
            Vector3 origin = candidate + Vector3.up * GroundProbeStartHeight;
            return Physics.Raycast(origin, Vector3.down, out hit, GroundProbeDistance, groundMask, QueryTriggerInteraction.Ignore);
        }

        private bool IsFarEnoughFromTrackedActors(Vector3 candidate)
        {
            if (minSpawnSeparation <= 0f)
                return true;

            float minDistanceSq = minSpawnSeparation * minSpawnSeparation;
            for (int i = 0; i < spawnedActors.Count; i++)
            {
                NetworkObject tracked = spawnedActors[i];
                if (!IsTrackedActorAlive(tracked))
                    continue;

                Vector3 delta = tracked.transform.position - candidate;
                delta.y = 0f;
                if (delta.sqrMagnitude < minDistanceSq)
                    return false;
            }

            return true;
        }

        private SceneEnemyRosterDef ResolveRoster()
        {
            if (rosterOverride != null)
                return rosterOverride;

            if (!useSceneRoster || actorSpawner == null)
                return null;

            return actorSpawner.DefaultSceneRoster;
        }

        private string ResolveRosterTag()
        {
            return string.IsNullOrWhiteSpace(rosterTagOverride)
                ? zoneId
                : rosterTagOverride.Trim();
        }

        private void EnsureGroundMaskInitialized()
        {
            if (groundMask.value != 0)
                return;

            int groundLayer = LayerMask.NameToLayer("Ground");
            groundMask = groundLayer >= 0 ? (1 << groundLayer) : Physics.DefaultRaycastLayers;
        }

        private string GetZoneLabel()
        {
            return string.IsNullOrWhiteSpace(zoneId) ? name : zoneId;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawGizmos)
                return;

            DrawZoneGizmos(selected: false);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            DrawZoneGizmos(selected: true);
        }

        private void DrawZoneGizmos(bool selected)
        {
            Color previous = Gizmos.color;
            Color activationColor = selected
                ? new Color(1f, 0.45f, 0.2f, 0.9f)
                : new Color(1f, 0.6f, 0.15f, 0.6f);
            Color scatterColor = selected
                ? new Color(0.2f, 1f, 0.55f, 0.95f)
                : new Color(0.2f, 0.95f, 0.55f, 0.55f);

            Gizmos.color = activationColor;
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(MinimumRadius, activationRadius));

            Gizmos.color = scatterColor;
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, spawnScatterRadius));
            Gizmos.DrawSphere(transform.position, 0.18f);

            Gizmos.color = previous;

            Handles.color = Color.white;
            Handles.Label(
                transform.position + Vector3.up * 0.85f,
                $"SpawnZone ({GetZoneLabel()})\nTarget {Mathf.Max(1, targetAliveCount)}");
        }
#endif
    }
}
