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

        // Server-only runtime state. This list is reused to avoid per-tick allocations.
        private readonly System.Collections.Generic.List<NetworkObject> _spawnedActors = new(16);

        private ActorSpawner _actorSpawner;
        private bool _runtimeInitialized;
        private bool _zoneActive;
        private bool _initialPopulationReachedTarget;
        private float _nextServerTickTime;
        private float _nextRespawnAllowedTime;
        private int _lastKnownAliveCount;
        private bool _loggedMissingSpawner;
        private bool _loggedInvalidTable;

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
            // This component exists in scene data on all peers, but only the server is allowed
            // to drive spawn decisions or mutate world state.
            if (!IsServerReady())
                return;

            if (!_runtimeInitialized && !TryInitializeRuntimeState())
                return;

            if (Time.time < _nextServerTickTime)
                return;

            _nextServerTickTime = Time.time + DefaultServerTickInterval;
            ServerTick();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            zoneId = zoneId == null ? string.Empty : zoneId.Trim();
            activationRadius = Mathf.Max(MinimumRadius, activationRadius);
            targetAliveCount = Mathf.Max(1, targetAliveCount);
            spawnScatterRadius = Mathf.Max(0f, spawnScatterRadius);
            minSpawnSeparation = Mathf.Max(0f, minSpawnSeparation);
            respawnDelaySeconds = Mathf.Max(0f, respawnDelaySeconds);
            maxSpawnAttemptsPerCycle = Mathf.Max(1, maxSpawnAttemptsPerCycle);
            EnsureGroundMaskInitialized();
        }
#endif

        /// <summary>
        /// Main authoritative zone tick.
        /// This is intentionally coarse-grained so we do not spend per-frame work on every zone.
        /// </summary>
        private void ServerTick()
        {
            int aliveCount = PruneTrackedActorsAndCountAlive();
            bool playerNearby = IsAnyLivingPlayerWithinActivationRadius();

            if (playerNearby != _zoneActive)
            {
                _zoneActive = playerNearby;

                if (verboseLogs)
                {
                    string state = _zoneActive ? "activated" : "deactivated";
                    Debug.Log($"[ActorSpawnZone] Zone '{GetZoneLabel()}' {state}.", this);
                }
            }

            if (!_zoneActive)
            {
                _lastKnownAliveCount = aliveCount;
                return;
            }

            HandleRespawnTimerAfterLoss(aliveCount);

            if (!ShouldAttemptSpawn(aliveCount))
            {
                _lastKnownAliveCount = aliveCount;
                return;
            }

            if (!HasAnyEligibleSpawnEntries())
            {
                if (!_loggedInvalidTable)
                {
                    _loggedInvalidTable = true;
                    Debug.LogWarning($"[ActorSpawnZone] Zone '{GetZoneLabel()}' has no valid spawn entries. Spawn skipped.", this);
                }

                _lastKnownAliveCount = aliveCount;
                return;
            }

            _loggedInvalidTable = false;

            int spawnedThisCycle = SpawnUntilTarget(aliveCount, out bool exhaustedAttempts);
            int finalAliveCount = PruneTrackedActorsAndCountAlive();

            if (finalAliveCount >= targetAliveCount)
                _initialPopulationReachedTarget = true;

            if (exhaustedAttempts && finalAliveCount < targetAliveCount)
            {
                Debug.LogWarning(
                    $"[ActorSpawnZone] Zone '{GetZoneLabel()}' could not reach target alive count after {maxSpawnAttemptsPerCycle} spawn attempts.",
                    this);
            }

            _lastKnownAliveCount = finalAliveCount;
        }

        private bool TryInitializeRuntimeState()
        {
            if (_runtimeInitialized)
                return true;

            if (!IsServerReady())
                return false;

            if (_actorSpawner == null)
                _actorSpawner = FindFirstObjectByType<ActorSpawner>();

            if (_actorSpawner == null)
            {
                if (!_loggedMissingSpawner)
                {
                    _loggedMissingSpawner = true;
                    Debug.LogWarning($"[ActorSpawnZone] Zone '{GetZoneLabel()}' is waiting for an ActorSpawner in the scene.", this);
                }

                return false;
            }

            _loggedMissingSpawner = false;
            _spawnedActors.Clear();
            _zoneActive = false;
            _initialPopulationReachedTarget = false;
            _nextServerTickTime = 0f;
            _nextRespawnAllowedTime = 0f;
            _lastKnownAliveCount = 0;
            _runtimeInitialized = true;
            return true;
        }

        private bool IsServerReady()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        /// <summary>
        /// Removes destroyed/despawned references and returns the current alive count for the zone.
        /// This is the first-pass authoritative bookkeeping path for actor death/despawn.
        /// </summary>
        private int PruneTrackedActorsAndCountAlive()
        {
            for (int i = _spawnedActors.Count - 1; i >= 0; i--)
            {
                NetworkObject tracked = _spawnedActors[i];
                if (!IsTrackedActorAlive(tracked))
                    _spawnedActors.RemoveAt(i);
            }

            return _spawnedActors.Count;
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

            foreach (var kvp in manager.ConnectedClients)
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

            // Initial fill should happen immediately. The respawn timer only matters after the
            // zone has already reached its desired alive population at least once.
            if (!_initialPopulationReachedTarget)
                return;

            if (aliveCount >= _lastKnownAliveCount)
                return;

            _nextRespawnAllowedTime = Time.time + respawnDelaySeconds;

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

            // Respawn-disabled zones are allowed to keep trying until they first reach their
            // intended population, but they do not refill after that point.
            if (!enableRespawn)
                return !_initialPopulationReachedTarget;

            // Respawn-enabled zones immediately build their initial population, then respect the
            // respawn timer for later refills after losses.
            if (!_initialPopulationReachedTarget)
                return true;

            if (Time.time < _nextRespawnAllowedTime)
                return false;

            return true;
        }

        private bool HasAnyEligibleSpawnEntries()
        {
            if (spawnTable == null || spawnTable.Length == 0)
                return false;

            for (int i = 0; i < spawnTable.Length; i++)
            {
                if (IsSpawnEntryEligible(spawnTable[i]))
                    return true;
            }

            return false;
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

                NetworkObject spawnedObject = _actorSpawner.ServerSpawnActor(request);
                if (spawnedObject == null)
                    continue;

                _spawnedActors.Add(spawnedObject);
                aliveCount++;
                spawned++;

                string actorLabel = selectedEntry.actorDef != null && !string.IsNullOrWhiteSpace(selectedEntry.actorDef.DisplayName)
                    ? selectedEntry.actorDef.DisplayName
                    : selectedEntry.actorDef != null ? selectedEntry.actorDef.name : spawnedObject.name;

                Debug.Log(
                    $"[ActorSpawnZone] Zone '{GetZoneLabel()}' spawned '{actorLabel}' at ({spawnPosition.x:0.##}, {spawnPosition.y:0.##}, {spawnPosition.z:0.##}).",
                    this);
            }

            exhaustedAttempts = attempts >= maxSpawnAttemptsPerCycle && aliveCount < targetAliveCount;
            return spawned;
        }

        /// <summary>
        /// Weighted random selection using relative weights, not percentage chances.
        /// </summary>
        private bool TrySelectSpawnEntry(out SpawnEntry selected)
        {
            selected = default;

            if (spawnTable == null || spawnTable.Length == 0)
                return false;

            float totalWeight = 0f;
            for (int i = 0; i < spawnTable.Length; i++)
            {
                if (!IsSpawnEntryEligible(spawnTable[i]))
                    continue;

                totalWeight += spawnTable[i].weight;
            }

            if (totalWeight <= 0f)
                return false;

            float choice = Random.value * totalWeight;
            float cursor = 0f;

            for (int i = 0; i < spawnTable.Length; i++)
            {
                SpawnEntry entry = spawnTable[i];
                if (!IsSpawnEntryEligible(entry))
                    continue;

                cursor += entry.weight;
                if (choice <= cursor)
                {
                    selected = entry;
                    return true;
                }
            }

            // Floating point accumulation can land very slightly past the end, so use the last
            // eligible row as a stable fallback instead of failing the cycle.
            for (int i = spawnTable.Length - 1; i >= 0; i--)
            {
                if (!spawnTable[i].IsEligible)
                    continue;

                selected = spawnTable[i];
                return true;
            }

            return false;
        }

        private static bool IsSpawnEntryEligible(SpawnEntry entry)
        {
            return entry.IsEligible && entry.actorDef.AllowZoneSpawning;
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
            for (int i = 0; i < _spawnedActors.Count; i++)
            {
                NetworkObject tracked = _spawnedActors[i];
                if (!IsTrackedActorAlive(tracked))
                    continue;

                Vector3 delta = tracked.transform.position - candidate;
                delta.y = 0f;
                if (delta.sqrMagnitude < minDistanceSq)
                    return false;
            }

            return true;
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


