using System.Collections.Generic;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Trigger-based environmental rest source.
    ///
    /// First-pass behavior:
    /// - Server is authoritative for occupancy signaling.
    /// - Enter/exit only informs PlayerVitalsNet; actual rested timers are handled by PlayerVitalsNet.
    /// - This keeps the zone scene-authorable and decoupled from shelter build-state logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class RestZone : MonoBehaviour
    {
        [Header("Rested Source")]
        [Tooltip("If false, this zone does not grant rested even when entered.")]
        [SerializeField] private bool grantsRested = true;

        [Tooltip("Continuous time required inside this zone before rested activates.")]
        [Min(0f)]
        [SerializeField] private float warmupSeconds = 3f;

        [Tooltip("Rested duration granted/refreshed by this zone after warmup completes.")]
        [Min(1f)]
        [SerializeField] private float restedDurationSeconds = 600f;

        private readonly HashSet<PlayerVitalsNet> occupants = new();
        private Collider zoneCollider;

        public bool GrantsRested => grantsRested;
        public float WarmupSeconds => Mathf.Max(0f, warmupSeconds);
        public float RestedDurationSeconds => Mathf.Max(1f, restedDurationSeconds);

        private void Awake()
        {
            zoneCollider = GetComponent<Collider>();
            if (zoneCollider != null)
                zoneCollider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServerRuntime())
                return;

            if (!TryResolvePlayerVitals(other, out var vitals))
                return;

            if (!occupants.Add(vitals))
                return;

            vitals.ServerNotifyEnteredRestZone(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServerRuntime())
                return;

            if (!TryResolvePlayerVitals(other, out var vitals))
                return;

            if (!occupants.Remove(vitals))
                return;

            vitals.ServerNotifyExitedRestZone(this);
        }

        private void OnDisable()
        {
            if (occupants.Count == 0)
                return;

            foreach (var vitals in occupants)
            {
                if (vitals != null)
                    vitals.ServerNotifyExitedRestZone(this);
            }

            occupants.Clear();
        }

        private static bool TryResolvePlayerVitals(Collider col, out PlayerVitalsNet vitals)
        {
            vitals = null;
            if (col == null)
                return false;

            vitals = col.GetComponentInParent<PlayerVitalsNet>();
            if (vitals == null)
                return false;

            // Only interact with spawned network player objects.
            return vitals.IsSpawned;
        }

        private static bool IsServerRuntime()
        {
            NetworkManager nm = NetworkManager.Singleton;
            return nm != null && nm.IsServer;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (warmupSeconds < 0f)
                warmupSeconds = 0f;

            if (restedDurationSeconds < 1f)
                restedDurationSeconds = 1f;
        }
#endif
    }
}
