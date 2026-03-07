using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Networked, server-authoritative HeartStone world object.
    ///
    /// First-pass scope:
    /// - Tracks HeartStone health as authoritative replicated state.
    /// - Marks shard as dead when health reaches zero.
    /// - Exposes simple XZ-plane build-radius helper checks.
    /// - Provides explicit server-only methods for damage and test restore.
    ///
    /// Intentionally not included yet:
    /// - Portal travel logic
    /// - Save/load persistence wiring
    /// - Raid orchestration and repair systems
    /// - UI-specific presentation
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HeartStoneNet : NetworkBehaviour
    {
        // --------------------------------------------------------------------
        // Authoring fields (scene-configurable, clamped in OnValidate)
        // --------------------------------------------------------------------
        [Header("Identity")]
        [SerializeField] private string heartStoneId = "HEARTSTONE_MAIN";

        [Header("Vitals")]
        [Min(1)]
        [SerializeField] private int maxHealth = 1000;

        [Header("Build Radius Rules")]
        [Min(0f)]
        [SerializeField] private float noBuildRadius = 8f;

        [Min(0f)]
        [SerializeField] private float buildRadius = 35f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;

        // --------------------------------------------------------------------
        // Replicated authoritative state
        // - Everyone can read.
        // - Only server can write.
        // --------------------------------------------------------------------
        private readonly NetworkVariable<int> currentHealth =
            new(1000, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> isShardDead =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // --------------------------------------------------------------------
        // Public read-only API for other systems
        // --------------------------------------------------------------------
        public string HeartStoneId => string.IsNullOrWhiteSpace(heartStoneId) ? "HEARTSTONE_MAIN" : heartStoneId;
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int CurrentHealth => Mathf.Clamp(currentHealth.Value, 0, MaxHealth);
        public bool IsShardDead => isShardDead.Value;
        public float NoBuildRadius => Mathf.Max(0f, noBuildRadius);
        public float BuildRadius => Mathf.Max(NoBuildRadius, buildRadius);

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            // First pass initializes a clean runtime state when this networked world object spawns.
            ServerInitializeState();
        }

        /// <summary>
        /// SERVER ONLY: Attempts to apply damage to the HeartStone.
        /// Returns true only when damage was actually applied.
        /// </summary>
        public bool ServerTryDamage(int amount)
        {
            if (!IsServer)
                return false;

            if (amount <= 0)
                return false;

            if (IsShardDead)
                return false;

            int oldHealth = CurrentHealth;
            int nextHealth = Mathf.Clamp(oldHealth - amount, 0, MaxHealth);

            if (nextHealth == oldHealth)
                return false;

            currentHealth.Value = nextHealth;

            Debug.Log($"[HeartStone][SERVER] Damage applied: -{amount}. Health {oldHealth} -> {nextHealth}", this);

            if (nextHealth <= 0)
                HandleShardDeath();

            return true;
        }

        /// <summary>
        /// SERVER ONLY: Utility helper for test workflows.
        /// Restores full health and clears shard-dead state.
        /// </summary>
        public void ServerRestoreFullHealth()
        {
            if (!IsServer)
                return;

            currentHealth.Value = MaxHealth;
            isShardDead.Value = false;

            Debug.Log($"[HeartStone][SERVER] Full health restored to {MaxHealth}.", this);
        }

        /// <summary>
        /// True when world position is within the no-build radius (XZ-plane check).
        /// Y is intentionally ignored for first-pass placement gating.
        /// </summary>
        public bool IsWithinNoBuildRadius(Vector3 worldPos)
        {
            return IsWithinRadiusXZ(worldPos, NoBuildRadius);
        }

        /// <summary>
        /// True when world position is within the outer build-allowed radius (XZ-plane check).
        /// Y is intentionally ignored for first-pass placement gating.
        /// </summary>
        public bool IsWithinBuildRadius(Vector3 worldPos)
        {
            return IsWithinRadiusXZ(worldPos, BuildRadius);
        }

        /// <summary>
        /// Build is allowed only when:
        /// - shard is not dead
        /// - position is outside no-build radius
        /// - position is inside build radius
        ///
        /// This is a pure query helper. It does not mutate state.
        /// </summary>
        public bool CanBuildAtPosition(Vector3 worldPos)
        {
            if (IsShardDead)
                return false;

            if (IsWithinNoBuildRadius(worldPos))
                return false;

            return IsWithinBuildRadius(worldPos);
        }

        private void ServerInitializeState()
        {
            // Keep serialized values safe before assigning replicated values.
            int safeMaxHealth = MaxHealth;

            // First-pass baseline behavior: spawn with healthy, living shard state.
            currentHealth.Value = safeMaxHealth;
            isShardDead.Value = false;

            Debug.Log($"[HeartStone][SERVER] Initialized id={HeartStoneId}, health={safeMaxHealth}, noBuild={NoBuildRadius}, build={BuildRadius}", this);
        }

        private void HandleShardDeath()
        {
            if (!IsServer)
                return;

            currentHealth.Value = 0;

            if (!isShardDead.Value)
            {
                isShardDead.Value = true;
                Debug.Log("[HeartStone][SERVER] Shard died. HeartStone destroyed.", this);

                // Future hook: shard-death orchestration can be triggered here (portal lockout, shard reset, etc.).
            }
        }

        private bool IsWithinRadiusXZ(Vector3 worldPos, float radius)
        {
            float safeRadius = Mathf.Max(0f, radius);

            Vector3 center = transform.position;
            float dx = worldPos.x - center.x;
            float dz = worldPos.z - center.z;
            float sqrDistanceXZ = (dx * dx) + (dz * dz);
            float sqrRadius = safeRadius * safeRadius;

            return sqrDistanceXZ <= sqrRadius;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (maxHealth < 1)
                maxHealth = 1;

            if (noBuildRadius < 0f)
                noBuildRadius = 0f;

            if (buildRadius < noBuildRadius)
                buildRadius = noBuildRadius;

            if (string.IsNullOrWhiteSpace(heartStoneId))
                heartStoneId = "HEARTSTONE_MAIN";
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            Vector3 center = transform.position;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(center, NoBuildRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(center, BuildRadius);
        }
#endif
    }
}

