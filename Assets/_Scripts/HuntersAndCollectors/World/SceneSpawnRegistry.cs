using System;
using System.Collections.Generic;
using HuntersAndCollectors.Bootstrap;
using UnityEngine;

namespace HuntersAndCollectors.World
{
    /// <summary>
    /// Runtime registry for non-networked scene spawn markers.
    /// Spawn points self-register as scenes load and unload.
    ///
    /// This registry intentionally uses the unified Bootstrap.SceneSpawnPoint type so there is only one
    /// authored spawn-marker component to place in scenes.
    /// </summary>
    public static class SceneSpawnRegistry
    {
        private static readonly Dictionary<string, SceneSpawnPoint> SpawnPoints = new(StringComparer.OrdinalIgnoreCase);

        public static void Register(SceneSpawnPoint spawnPoint)
        {
            if (spawnPoint == null)
                return;

            string key = BuildKey(spawnPoint.gameObject.scene.name, spawnPoint.SpawnPointId);
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (SpawnPoints.TryGetValue(key, out SceneSpawnPoint existing) && existing != null && existing != spawnPoint)
            {
                Debug.LogWarning(
                    $"[SceneSpawnRegistry] Duplicate spawn point id '{spawnPoint.SpawnPointId}' in scene '{spawnPoint.gameObject.scene.name}'. Keeping the newest registration on '{spawnPoint.name}'.",
                    spawnPoint);
            }

            SpawnPoints[key] = spawnPoint;
        }

        public static void Unregister(SceneSpawnPoint spawnPoint)
        {
            if (spawnPoint == null)
                return;

            string key = BuildKey(spawnPoint.gameObject.scene.name, spawnPoint.SpawnPointId);
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (SpawnPoints.TryGetValue(key, out SceneSpawnPoint existing) && existing == spawnPoint)
                SpawnPoints.Remove(key);
        }

        public static bool TryGetSpawnPoint(string sceneName, string spawnPointId, out SceneSpawnPoint spawnPoint)
        {
            spawnPoint = null;

            string key = BuildKey(sceneName, spawnPointId);
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning("[SceneSpawnRegistry] Spawn lookup rejected because sceneName or spawnPointId was empty.");
                return false;
            }

            if (!SpawnPoints.TryGetValue(key, out spawnPoint) || spawnPoint == null)
            {
                Debug.LogWarning($"[SceneSpawnRegistry] Spawn point '{spawnPointId}' was not found in loaded scene '{sceneName}'.");
                spawnPoint = null;
                return false;
            }

            return true;
        }

        private static string BuildKey(string sceneName, string spawnPointId)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(spawnPointId))
                return string.Empty;

            return sceneName.Trim() + "::" + spawnPointId.Trim();
        }
    }
}
