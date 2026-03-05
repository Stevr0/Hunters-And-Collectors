using UnityEngine;
using UnityEngine.Serialization;

namespace HuntersAndCollectors.Stats
{
    /// <summary>
    /// PlayerBaseStats
    /// -----------------------------------------------------------------------------
    /// This component lives on the player prefab and stores baseline values used by
    /// EffectiveStatsCalculator.
    ///
    /// Important:
    /// - Attributes are now Strength / Dexterity / Intelligence.
    /// - Max vitals are NOT authored here anymore. They are derived in the shared
    ///   calculator from these attributes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerBaseStats : MonoBehaviour
    {
        [Header("Base Attributes")]
        [SerializeField] private int baseStrength = 10;

        // FormerlySerializedAs keeps old prefab values when field names change.
        [FormerlySerializedAs("baseStamina")]
        [SerializeField] private int baseDexterity = 10;

        [SerializeField] private int baseIntelligence = 10;

        [Header("Base Combat / Movement")]
        [SerializeField] private float baseMoveSpeedMult = 1f;
        [SerializeField] private float baseDamage = 0f;
        [SerializeField] private float baseDefence = 0f;
        [SerializeField] private float baseSwingSpeed = 1f;

        public int BaseStrength => baseStrength;
        public int BaseDexterity => baseDexterity;
        public int BaseIntelligence => baseIntelligence;

        public float BaseMoveSpeedMult => baseMoveSpeedMult;
        public float BaseDamage => baseDamage;
        public float BaseDefence => baseDefence;
        public float BaseSwingSpeed => baseSwingSpeed;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Keep values in safe ranges for shared calculator math.
            if (baseStrength < 0) baseStrength = 0;
            if (baseDexterity < 0) baseDexterity = 0;
            if (baseIntelligence < 0) baseIntelligence = 0;

            if (baseMoveSpeedMult <= 0f) baseMoveSpeedMult = 1f;
            if (baseDamage < 0f) baseDamage = 0f;
            if (baseDefence < 0f) baseDefence = 0f;
            if (baseSwingSpeed <= 0f) baseSwingSpeed = 1f;
        }
#endif
    }
}
