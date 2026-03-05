using Unity.Netcode;
using UnityEngine;
using HuntersAndCollectors.Harvesting;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Input; // if you use InputState.GameplayLocked

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// AutoPickupClient (Valheim-style)
    /// --------------------------------------------------------------------
    /// Client-side "scanner" that requests pickup of nearby ResourceDrop items.
    ///
    /// Authority model:
    /// - This component runs ONLY on the owning client.
    /// - It NEVER modifies inventory directly.
    /// - It calls HarvestingNet.RequestPickup(drop) which sends a ServerRpc.
    ///
    /// Performance + spam control:
    /// - OverlapSphere scan is throttled by scanIntervalSeconds.
    /// - Each scan requests up to maxPickupsPerScan items.
    ///
    /// UX:
    /// - Disabled automatically while gameplay is locked (UI open).
    /// - Optional toggle method provided if you want a keybind like Valheim (V).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AutoPickupClient : NetworkBehaviour
    {
        [Header("References")]
        [Tooltip("HarvestingNet on the player (used to request pickup).")]
        [SerializeField] private HarvestingNet harvestingNet;

        [Header("Auto Pickup Settings")]
        [Tooltip("Master switch. You can toggle this at runtime (Valheim-style).")]
        [SerializeField] private bool autoPickupEnabled = true;

        [Tooltip("Pickup scan radius (meters). Valheim feels ~2-3m.")]
        [Min(0.25f)]
        [SerializeField] private float pickupRadius = 2.5f;

        [Tooltip("How often we scan for pickups (seconds). 0.10 - 0.25 feels good.")]
        [Min(0.02f)]
        [SerializeField] private float scanIntervalSeconds = 0.15f;

        [Tooltip("Max pickup requests per scan (prevents server spam when many drops exist).")]
        [Min(1)]
        [SerializeField] private int maxPickupsPerScan = 3;

        [Tooltip("Only consider colliders in these layers (recommended: set your pickups to a 'Pickup' layer).")]
        [SerializeField] private LayerMask pickupLayerMask = ~0;

        [Tooltip("Optional: ignore pickups if they are too high/low compared to player (helps avoid weird vertical grabs).")]
        [SerializeField] private bool limitVerticalDifference = true;

        [Tooltip("If limitVerticalDifference is enabled, max allowed Y difference.")]
        [Min(0.1f)]
        [SerializeField] private float maxVerticalDifference = 1.25f;

        // NonAlloc buffer to avoid GC allocs every scan.
        private readonly Collider[] _overlapBuffer = new Collider[32];

        private float _scanTimer;

        public bool AutoPickupEnabled => autoPickupEnabled;

        public override void OnNetworkSpawn()
        {
            // Only the owning client should run this logic.
            if (!IsOwner)
                enabled = false;

            if (harvestingNet == null)
                harvestingNet = GetComponent<HarvestingNet>();
        }

        private void Update()
        {
            // Safety checks
            if (!IsOwner || harvestingNet == null)
                return;

            // If your project locks gameplay input while UI windows are open,
            // don't auto-pickup while locked (feels like Valheim).
            if (InputState.GameplayLocked)
                return;

            if (!autoPickupEnabled)
                return;

            _scanTimer += Time.deltaTime;
            if (_scanTimer < scanIntervalSeconds)
                return;

            _scanTimer = 0f;

            TryAutoPickupOnce();
        }

        /// <summary>
        /// Call this from your input system (e.g. key V) if you want a Valheim-like toggle.
        /// </summary>
        public void ToggleAutoPickup()
        {
            autoPickupEnabled = !autoPickupEnabled;
            Debug.Log($"[AutoPickupClient] AutoPickupEnabled={autoPickupEnabled}", this);
        }

        private void TryAutoPickupOnce()
        {
            Vector3 origin = transform.position;

            int hitCount = Physics.OverlapSphereNonAlloc(
                origin,
                pickupRadius,
                _overlapBuffer,
                pickupLayerMask,
                QueryTriggerInteraction.Collide
            );

            if (hitCount <= 0)
                return;

            // We'll pick the closest few ResourceDrops in range.
            int requested = 0;

            // Simple "closest-first" loop:
            // Each time, find nearest remaining drop, request pickup, mark as used.
            // Because buffer is small (32), O(n^2) is fine and super simple.
            for (int picks = 0; picks < maxPickupsPerScan; picks++)
            {
                ResourceDrop bestDrop = null;
                float bestDistSqr = float.MaxValue;

                for (int i = 0; i < hitCount; i++)
                {
                    var col = _overlapBuffer[i];
                    if (col == null)
                        continue;

                    // Get ResourceDrop on collider parent/root.
                    var drop = col.GetComponentInParent<ResourceDrop>();
                    if (drop == null)
                        continue;

                    // If the drop is already consumed (server-side), ignore.
                    // Note: IsConsumed is server authoritative; on clients it may lag slightly,
                    // but it's still useful as a quick filter.
                    if (drop.IsConsumed)
                        continue;

                    // Optional: vertical filter (helps avoid picking through floors/ledges)
                    if (limitVerticalDifference)
                    {
                        float dy = Mathf.Abs(drop.transform.position.y - origin.y);
                        if (dy > maxVerticalDifference)
                            continue;
                    }

                    float distSqr = (drop.transform.position - origin).sqrMagnitude;
                    if (distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestDrop = drop;
                    }
                }

                if (bestDrop == null)
                    break;

                // IMPORTANT:
                // This should call your existing HarvestingNet flow that sends a ServerRpc.
                harvestingNet.RequestPickup(bestDrop);

                requested++;

                // Remove this drop from consideration this scan:
                // Easiest is to "consume" its collider entry by nulling any buffer entries that reference it.
                // (Not perfect, but good enough for a tiny buffer.)
                for (int i = 0; i < hitCount; i++)
                {
                    var col = _overlapBuffer[i];
                    if (col == null)
                        continue;

                    var drop = col.GetComponentInParent<ResourceDrop>();
                    if (drop == bestDrop)
                        _overlapBuffer[i] = null;
                }
            }

            if (requested > 0)
            {
                // Optional debug:
                // Debug.Log($"[AutoPickupClient] Requested {requested} pickup(s).", this);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
#endif
    }
}