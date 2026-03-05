using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Optional per-collider hitbox data for server-authoritative combat.
    ///
    /// Place this on child colliders and assign/auto-resolve RootDamageable.
    /// The server uses this to map hit colliders to the true damageable root
    /// and apply optional damage multipliers (e.g., headshot bonus).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitboxNet : MonoBehaviour
    {
        [SerializeField] private DamageableNet rootDamageable;

        [Min(0f)]
        [SerializeField] private float damageMultiplier = 1f;

        public DamageableNet RootDamageable => rootDamageable;
        public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);

        private void Awake()
        {
            ResolveRootIfMissing();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ResolveRootIfMissing();
            if (damageMultiplier < 0f)
                damageMultiplier = 0f;
        }
#endif

        private void ResolveRootIfMissing()
        {
            if (rootDamageable == null)
                rootDamageable = GetComponentInParent<DamageableNet>();
        }
    }
}
