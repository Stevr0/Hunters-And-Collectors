using UnityEngine;

namespace HuntersAndCollectors.Skills
{
    /// <summary>
    /// LumberjackingTuning
    /// ---------------------------------------------------------
    /// Central place for deterministic formulas so:
    /// - Server and designers have one source of truth
    /// - Outcomes scale smoothly with skill level (0..100)
    ///
    /// NOTE: Keep MVP deterministic: avoid randomness here.
    /// </summary>
    public static class LumberjackingTuning
    {
        /// <summary>
        /// Minimum time (seconds) between successful chop hits (server enforced).
        /// Higher skill => faster chopping.
        /// </summary>
        public static float GetChopHitCooldownSeconds(int lumberLevel)
        {
            lumberLevel = Mathf.Clamp(lumberLevel, 0, 100);

            // Example curve:
            // Level 0  => 1.00s
            // Level 50 => ~0.70s
            // Level 100=> 0.45s
            float t = lumberLevel / 100f;
            return Mathf.Lerp(1.00f, 0.45f, t);
        }

        /// <summary>
        /// Logs gained per successful hit.
        /// MVP: constant 1 for clean determinism.
        /// </summary>
        public static int GetLogsPerHit(int lumberLevel)
        {
            // You can later scale this (or keep constant forever).
            return 1;
        }

        /// <summary>
        /// Extra logs gained when the tree is felled.
        /// Deterministic scaling by level.
        /// </summary>
        public static int GetBonusLogsOnFell(int lumberLevel)
        {
            lumberLevel = Mathf.Clamp(lumberLevel, 0, 100);

            // +2 baseline, +1 every 20 levels.
            return 2 + (lumberLevel / 20);
        }

        /// <summary>
        /// XP for each successful hit.
        /// </summary>
        public static int GetXpPerHit() => 1;

        /// <summary>
        /// XP awarded when a tree is felled.
        /// </summary>
        public static int GetXpOnFell() => 5;

        /// <summary>
        /// Max interaction distance the server will accept.
        /// Keep this slightly larger than client raycast distance to reduce “feels bad”.
        /// </summary>
        public static float ServerMaxChopDistance => 3.25f;
    }
}
