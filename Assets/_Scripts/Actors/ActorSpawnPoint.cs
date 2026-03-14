using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Scene marker used by ActorSpawner to choose where actors should appear.
    ///
    /// This component intentionally contains no authority logic.
    /// It is only authored spawn metadata (id + type flags + optional defaults).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorSpawnPoint : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string spawnPointId = string.Empty;

        [Header("Type Flags")]
        [SerializeField] private bool isPlayerSpawn = true;
        [SerializeField] private bool isNpcSpawn;
        [SerializeField] private bool isDummySpawn;

        [Header("Optional Defaults")]
        [SerializeField] private ActorDef defaultActorDef;

        [Header("Optional Roster")]
        [SerializeField] private SceneEnemyRosterDef rosterOverride;
        [SerializeField] private string rosterTagOverride = string.Empty;
        [SerializeField] private bool useSceneRoster = true;
        [SerializeField] private bool includeEliteEntries = true;
        [SerializeField] private bool includeBossEntries = true;
        [SerializeField] private bool useNightEntries;

        [Header("Respawn")]
        [Tooltip("If enabled, ActorSpawner can automatically respawn actors that were spawned from this point.")]
        [SerializeField] private bool enableRespawn;
        [Min(0f)]
        [SerializeField] private float respawnDelaySeconds = 5f;
        [Min(1)]
        [SerializeField] private int maxAliveFromPoint = 1;

        [Header("Editor")]
        [SerializeField] private bool drawGizmo = true;

        private readonly List<SpawnEntry> rosterMatches = new(16);

        public string SpawnPointId => spawnPointId;
        public bool IsPlayerSpawn => isPlayerSpawn;
        public bool IsNpcSpawn => isNpcSpawn;
        public bool IsDummySpawn => isDummySpawn;
        public ActorDef DefaultActorDef => defaultActorDef;

        public bool EnableRespawn => enableRespawn;
        public float RespawnDelaySeconds => Mathf.Max(0f, respawnDelaySeconds);
        public int MaxAliveFromPoint => Mathf.Max(1, maxAliveFromPoint);

        public bool DrawGizmo => drawGizmo;
        public SceneEnemyRosterDef RosterOverride => rosterOverride;
        public bool UseSceneRoster => useSceneRoster;

        public ActorDef ResolveActorDef(SceneEnemyRosterDef sceneRoster, bool logWarnings = false)
        {
            SceneEnemyRosterDef roster = ResolveRoster(sceneRoster);
            if (roster == null)
                return defaultActorDef;

            rosterMatches.Clear();
            roster.AppendMatches(
                rosterMatches,
                ResolveRosterTag(),
                RosterSpawnUsage.SpawnPoint,
                includeEliteEntries,
                includeBossEntries,
                useNightEntries);

            if (rosterMatches.Count == 0)
            {
                if (defaultActorDef == null && logWarnings)
                    Debug.LogWarning($"[ActorSpawnPoint] No roster match and no default ActorDef configured for point '{spawnPointId}'.", this);

                return defaultActorDef;
            }

            float totalWeight = 0f;
            for (int i = 0; i < rosterMatches.Count; i++)
                totalWeight += Mathf.Max(0f, rosterMatches[i].weight);

            if (totalWeight <= 0f)
                return defaultActorDef;

            float choice = Random.value * totalWeight;
            float cursor = 0f;
            for (int i = 0; i < rosterMatches.Count; i++)
            {
                SpawnEntry entry = rosterMatches[i];
                cursor += Mathf.Max(0f, entry.weight);
                if (choice <= cursor)
                    return entry.actorDef != null ? entry.actorDef : defaultActorDef;
            }

            SpawnEntry last = rosterMatches[rosterMatches.Count - 1];
            return last.actorDef != null ? last.actorDef : defaultActorDef;
        }

        private SceneEnemyRosterDef ResolveRoster(SceneEnemyRosterDef sceneRoster)
        {
            if (rosterOverride != null)
                return rosterOverride;

            if (!useSceneRoster)
                return null;

            return sceneRoster;
        }

        private string ResolveRosterTag()
        {
            return string.IsNullOrWhiteSpace(rosterTagOverride)
                ? spawnPointId
                : rosterTagOverride.Trim();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            spawnPointId = spawnPointId == null ? string.Empty : spawnPointId.Trim();
            rosterTagOverride = rosterTagOverride == null ? string.Empty : rosterTagOverride.Trim();
            respawnDelaySeconds = Mathf.Max(0f, respawnDelaySeconds);
            maxAliveFromPoint = Mathf.Max(1, maxAliveFromPoint);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmo)
                return;

            Color previous = Gizmos.color;
            Gizmos.color = ResolveGizmoColor();
            Gizmos.DrawSphere(transform.position, 0.25f);
            Gizmos.DrawWireSphere(transform.position, 0.55f);
            Gizmos.color = previous;

            string label = string.IsNullOrWhiteSpace(spawnPointId)
                ? "ActorSpawnPoint"
                : $"ActorSpawnPoint ({spawnPointId})";

            if (enableRespawn)
                label += $" [Respawn {respawnDelaySeconds:0.#}s, max {maxAliveFromPoint}]";

            Handles.color = Color.white;
            Handles.Label(transform.position + Vector3.up * 0.75f, label);
        }

        private Color ResolveGizmoColor()
        {
            if (isPlayerSpawn)
                return new Color(0.2f, 0.8f, 1f, 0.9f);

            if (isNpcSpawn)
                return new Color(1f, 0.45f, 0.25f, 0.9f);

            if (isDummySpawn)
                return new Color(0.6f, 1f, 0.35f, 0.9f);

            return new Color(1f, 1f, 1f, 0.8f);
        }
#endif
    }
}
