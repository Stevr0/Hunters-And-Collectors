using System;
using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    public enum RosterSpawnUsage
    {
        Any = 0,
        Zone = 1,
        SpawnPoint = 2
    }

    [CreateAssetMenu(menuName = "HuntersAndCollectors/Actors/Scene Enemy Roster", fileName = "SceneEnemyRoster")]
    public sealed class SceneEnemyRosterDef : ScriptableObject
    {
        [Serializable]
        public struct RosterEntry
        {
            [Tooltip("Optional tag used to target a specific zone/point family. Empty means the row is valid for the whole scene roster.")]
            public string spawnTag;

            [Tooltip("Restricts which runtime authoring component may consume this row.")]
            public RosterSpawnUsage usage;

            [Tooltip("Weighted spawn data for this scene roster row.")]
            public SpawnEntry spawn;

            public bool Matches(string requestedTag, RosterSpawnUsage requestedUsage, bool includeElite, bool includeBoss, bool isNight)
            {
                if (!spawn.IsEligible)
                    return false;

                if (usage != RosterSpawnUsage.Any && requestedUsage != RosterSpawnUsage.Any && usage != requestedUsage)
                    return false;

                if (!includeElite && spawn.isElite)
                    return false;

                if (!includeBoss && spawn.isBoss)
                    return false;

                if (!spawn.AllowsTimeOfDay(isNight))
                    return false;

                return TagsMatch(spawnTag, requestedTag);
            }

            public void Clamp()
            {
                spawnTag = spawnTag == null ? string.Empty : spawnTag.Trim();
                spawn.weight = Mathf.Max(0f, spawn.weight);
                spawn.minGroupSize = Mathf.Max(1, spawn.minGroupSize);
                spawn.maxGroupSize = Mathf.Max(spawn.minGroupSize, spawn.maxGroupSize);
            }

            private static bool TagsMatch(string entryTag, string requestedTag)
            {
                if (string.IsNullOrWhiteSpace(entryTag))
                    return true;

                if (string.IsNullOrWhiteSpace(requestedTag))
                    return false;

                return string.Equals(entryTag.Trim(), requestedTag.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        [Header("Identity")]
        [SerializeField] private string sceneName = string.Empty;
        [SerializeField] private string areaId = string.Empty;

        [Header("Progression")]
        [Min(1)]
        [SerializeField] private int recommendedTierMin = 1;
        [Min(1)]
        [SerializeField] private int recommendedTierMax = 1;

        [Header("Roster Entries")]
        [SerializeField] private List<RosterEntry> entries = new();

        public string SceneName => sceneName;
        public string AreaId => areaId;
        public int RecommendedTierMin => Mathf.Max(1, recommendedTierMin);
        public int RecommendedTierMax => Mathf.Max(RecommendedTierMin, recommendedTierMax);
        public IReadOnlyList<RosterEntry> Entries => entries;

        public int AppendMatches(List<SpawnEntry> buffer, string spawnTag, RosterSpawnUsage usage, bool includeElite, bool includeBoss, bool isNight)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            int appended = 0;
            if (entries == null || entries.Count == 0)
                return appended;

            for (int i = 0; i < entries.Count; i++)
            {
                RosterEntry entry = entries[i];
                if (!entry.Matches(spawnTag, usage, includeElite, includeBoss, isNight))
                    continue;

                buffer.Add(entry.spawn);
                appended++;
            }

            return appended;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            sceneName = sceneName == null ? string.Empty : sceneName.Trim();
            areaId = areaId == null ? string.Empty : areaId.Trim();
            recommendedTierMin = Mathf.Max(1, recommendedTierMin);
            recommendedTierMax = Mathf.Max(recommendedTierMin, recommendedTierMax);

            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                RosterEntry entry = entries[i];
                entry.Clamp();
                entries[i] = entry;
            }
        }
#endif
    }
}
