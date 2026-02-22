using UnityEngine;

namespace HuntersAndCollectors.Skills
{
    /// <summary>
    /// RunningSkillTuning
    /// -------------------------------------------------------
    /// Central place to calculate running speed from Running skill level.
    /// Keeps the "math" out of your movement script.
    /// </summary>
    public static class RunningSkillTuning
    {
        /// <summary>
        /// Calculates run speed based on:
        /// - walkSpeed (e.g. 2)
        /// - maxSpeed (e.g. 10)
        /// - runningLevel (0..N)
        ///
        /// Formula:
        /// multiplier = 1 + (level * 0.05)
        /// runSpeed = min(maxSpeed, walkSpeed * multiplier)
        ///
        /// Nice properties:
        /// - Level 0 => runSpeed == walkSpeed
        /// - Level 20 => 2x walkSpeed
        /// - Eventually caps at maxSpeed
        /// </summary>
        public static float GetRunSpeed(float walkSpeed, float maxSpeed, int runningLevel)
        {
            // Clamp for safety (even though SkillsNet caps it).
            int lvl = Mathf.Clamp(runningLevel, 0, 100);

            float t = lvl / 100f; // 0..1
            return Mathf.Lerp(walkSpeed, maxSpeed, t);
        }
    }
}
