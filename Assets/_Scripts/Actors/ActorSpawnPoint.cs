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

        [Header("Respawn")]
        [Tooltip("If enabled, ActorSpawner can automatically respawn actors that were spawned from this point.")]
        [SerializeField] private bool enableRespawn;
        [Min(0f)]
        [SerializeField] private float respawnDelaySeconds = 5f;
        [Min(1)]
        [SerializeField] private int maxAliveFromPoint = 1;

        [Header("Editor")]
        [SerializeField] private bool drawGizmo = true;

        public string SpawnPointId => spawnPointId;
        public bool IsPlayerSpawn => isPlayerSpawn;
        public bool IsNpcSpawn => isNpcSpawn;
        public bool IsDummySpawn => isDummySpawn;
        public ActorDef DefaultActorDef => defaultActorDef;

        public bool EnableRespawn => enableRespawn;
        public float RespawnDelaySeconds => Mathf.Max(0f, respawnDelaySeconds);
        public int MaxAliveFromPoint => Mathf.Max(1, maxAliveFromPoint);

        public bool DrawGizmo => drawGizmo;

#if UNITY_EDITOR
        private void OnValidate()
        {
            spawnPointId = spawnPointId == null ? string.Empty : spawnPointId.Trim();
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
