using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Scene-level registry for HeartStoneNet objects.
    ///
    /// First-pass responsibilities:
    /// - Keep a simple singleton access point.
    /// - Re-scan active scenes and rebuild lookup map.
    /// - Support ID-based lookup and "main" HeartStone selection.
    /// - Warn on duplicate HeartStone IDs.
    ///
    /// This registry is intentionally lightweight and non-authoritative.
    /// Authoritative data remains on HeartStoneNet (NetworkBehaviour).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeartStoneRegistry : MonoBehaviour
    {
        private const string DefaultMainId = "HEARTSTONE_MAIN";

        private static HeartStoneRegistry instance;

        private readonly Dictionary<string, HeartStoneNet> byId =
            new(System.StringComparer.OrdinalIgnoreCase);

        private readonly List<HeartStoneNet> allHeartStones = new();

        public static HeartStoneRegistry Instance => instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[HeartStoneRegistry] Duplicate registry detected. Destroying newest instance.", this);
                Destroy(this);
                return;
            }

            instance = this;
            RebuildRegistry();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            RebuildRegistry();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            if (instance == this)
                instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RebuildRegistry();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            RebuildRegistry();
        }

        /// <summary>
        /// Tries to resolve a HeartStone by configured HeartStoneId.
        /// </summary>
        public bool TryGet(string heartStoneId, out HeartStoneNet heartStone)
        {
            heartStone = null;

            if (string.IsNullOrWhiteSpace(heartStoneId))
                return false;

            return byId.TryGetValue(heartStoneId, out heartStone) && heartStone != null;
        }

        /// <summary>
        /// Resolves the main HeartStone with these rules:
        /// - if only one HeartStone exists, use it
        /// - if many exist, prefer HEARTSTONE_MAIN
        /// - otherwise fallback to first discovered
        /// </summary>
        public bool TryGetMain(out HeartStoneNet heartStone)
        {
            heartStone = null;

            if (allHeartStones.Count == 0)
                return false;

            if (allHeartStones.Count == 1)
            {
                heartStone = allHeartStones[0];
                return heartStone != null;
            }

            if (TryGet(DefaultMainId, out heartStone))
                return true;

            heartStone = allHeartStones[0];
            return heartStone != null;
        }

        /// <summary>
        /// Convenience wrapper for build systems:
        /// - looks up the current main HeartStone
        /// - evaluates CanBuildAtPosition for that world position
        /// </summary>
        public static bool TryCanBuildAt(Vector3 worldPos, out bool canBuild)
        {
            canBuild = false;

            if (Instance == null)
                return false;

            if (!Instance.TryGetMain(out HeartStoneNet mainHeartStone) || mainHeartStone == null)
                return false;

            canBuild = mainHeartStone.CanBuildAtPosition(worldPos);
            return true;
        }

        private void RebuildRegistry()
        {
            byId.Clear();
            allHeartStones.Clear();

            HeartStoneNet[] found = FindObjectsByType<HeartStoneNet>(FindObjectsSortMode.None);

            for (int i = 0; i < found.Length; i++)
            {
                HeartStoneNet candidate = found[i];
                if (candidate == null)
                    continue;

                allHeartStones.Add(candidate);

                string id = candidate.HeartStoneId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning($"[HeartStoneRegistry] Ignoring HeartStone with empty id at '{candidate.name}'.", candidate);
                    continue;
                }

                if (byId.ContainsKey(id))
                {
                    Debug.LogWarning($"[HeartStoneRegistry] Duplicate HeartStone id '{id}' found. Keeping first entry.", candidate);
                    continue;
                }

                byId[id] = candidate;
            }
        }
    }
}
