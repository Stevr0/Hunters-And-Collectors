using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Single weighted row in an ActorSpawnZone spawn table.
    ///
    /// Design notes:
    /// - Weight is not a percentage. It is a relative selection weight.
    /// - Entries with a null ActorDef or a non-positive weight are treated as ineligible.
    /// - The zone filters invalid entries at runtime so authored data can stay flexible.
    /// - Group size is optional. Existing scene data that never populated these fields still resolves to 1.
    /// </summary>
    [System.Serializable]
    public struct SpawnEntry
    {
        [Tooltip("ActorDef that should be spawned when this weighted entry is selected.")]
        public ActorDef actorDef;

        [Min(0f)]
        [Tooltip("Relative selection weight. Higher values are chosen more often.")]
        public float weight;

        [Min(1)]
        [Tooltip("Minimum number of actors to spawn when this entry is selected.")]
        public int minGroupSize;

        [Min(1)]
        [Tooltip("Maximum number of actors to spawn when this entry is selected.")]
        public int maxGroupSize;

        [Tooltip("If false, this entry is skipped during daytime-aware roster selection.")]
        public bool allowDaytime;

        [Tooltip("If false, this entry is skipped during nighttime-aware roster selection.")]
        public bool allowNighttime;

        [Tooltip("Marks this entry as an elite option so components can include/exclude it explicitly.")]
        public bool isElite;

        [Tooltip("Marks this entry as a boss option so components can include/exclude it explicitly.")]
        public bool isBoss;

        /// <summary>
        /// Returns true only when this row can participate in weighted selection.
        /// </summary>
        public bool IsEligible => actorDef != null && weight > 0f;

        public int ResolveGroupSize()
        {
            int min = Mathf.Max(1, minGroupSize);
            int max = Mathf.Max(min, maxGroupSize);
            return Random.Range(min, max + 1);
        }

        public bool AllowsTimeOfDay(bool isNight)
        {
            // Default serialized bools are false for older assets, so treat "both false" as unrestricted.
            if (isNight)
                return allowNighttime || (!allowNighttime && !allowDaytime);

            return allowDaytime || (!allowNighttime && !allowDaytime);
        }
    }
}
