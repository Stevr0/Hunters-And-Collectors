using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    public enum ActorIdleMode
    {
        Hold = 0,
        Patrol = 1
    }

    [CreateAssetMenu(menuName = "HuntersAndCollectors/Actors/ActorAIDef")]
    public sealed class ActorAIDef : ScriptableObject
    {
        [Header("Idle")]
        public ActorIdleMode IdleMode = ActorIdleMode.Hold;

        [Header("Decision")]
        [Min(0.02f)] public float DecisionIntervalSeconds = 0.1f;

        [Header("Perception")]
        [Min(0.1f)] public float AggroRange = 10f;
        [Min(0.1f)] public float LoseTargetRange = 14f;
        public bool RequireLineOfSight = true;
        public LayerMask TargetMask = ~0;
        public LayerMask OcclusionMask = ~0;

        [Header("Combat")]
        [Min(0.1f)] public float AttackRange = 2.1f;
        [Min(0f)] public float AttackRangeHysteresis = 0.35f;
        [Min(0)] public int FlatAttackBonus = 0;
        [Min(0f)] public float AttackIntervalOverrideSeconds = 0f;

        [Header("Movement")]
        [Min(0.1f)] public float MoveSpeedMultiplier = 1f;
        [Min(0.1f)] public float LeashRange = 25f;
        [Min(0.1f)] public float ReturnHomeDistance = 0.6f;

        [Header("Patrol")]
        [Min(0.1f)] public float PatrolRadius = 6f;
        [Min(0f)] public float PatrolPauseSeconds = 1.2f;
        [Min(0.1f)] public float PatrolArrivalDistance = 0.75f;

        [Header("Retreat")]
        [Range(0f, 1f)] public float RetreatHealth01 = 0.2f;
        [Min(0.1f)] public float RetreatDistance = 7f;
        [Min(0.1f)] public float RetreatDurationSeconds = 2f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            DecisionIntervalSeconds = Mathf.Max(0.02f, DecisionIntervalSeconds);
            AggroRange = Mathf.Max(0.1f, AggroRange);
            LoseTargetRange = Mathf.Max(AggroRange, LoseTargetRange);
            AttackRange = Mathf.Max(0.1f, AttackRange);
            AttackRangeHysteresis = Mathf.Max(0f, AttackRangeHysteresis);
            FlatAttackBonus = Mathf.Max(0, FlatAttackBonus);
            AttackIntervalOverrideSeconds = Mathf.Max(0f, AttackIntervalOverrideSeconds);
            MoveSpeedMultiplier = Mathf.Max(0.1f, MoveSpeedMultiplier);
            LeashRange = Mathf.Max(0.1f, LeashRange);
            ReturnHomeDistance = Mathf.Max(0.1f, ReturnHomeDistance);
            PatrolRadius = Mathf.Max(0.1f, PatrolRadius);
            PatrolPauseSeconds = Mathf.Max(0f, PatrolPauseSeconds);
            PatrolArrivalDistance = Mathf.Max(0.1f, PatrolArrivalDistance);
            RetreatHealth01 = Mathf.Clamp01(RetreatHealth01);
            RetreatDistance = Mathf.Max(0.1f, RetreatDistance);
            RetreatDurationSeconds = Mathf.Max(0.1f, RetreatDurationSeconds);
        }
#endif
    }
}
