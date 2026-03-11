using System;
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
    /// </summary>
    [Serializable]
    public struct SpawnEntry
    {
        [Tooltip("ActorDef that should be spawned when this weighted entry is selected.")]
        public ActorDef actorDef;

        [Min(0f)]
        [Tooltip("Relative selection weight. Higher values are chosen more often.")]
        public float weight;

        /// <summary>
        /// Returns true only when this row can participate in weighted selection.
        /// </summary>
        public bool IsEligible => actorDef != null && weight > 0f;
    }
}
