using HuntersAndCollectors.Actors;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using UnityEngine;

namespace HuntersAndCollectors.Stats
{
    /// <summary>
    /// Unified actor stats provider for every combat-capable entity.
    ///
    /// Pipeline:
    /// ActorDef -> ActorStatsProvider -> EffectiveStatsCalculator -> EffectiveStats
    ///
    /// This is intentionally actor-type agnostic: no player/NPC/dummy branching.
    /// Optional systems (equipment/skills/database) are composed when present.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorStatsProvider : MonoBehaviour, IStatsProvider
    {
        [Header("References")]
        [SerializeField] private ActorDefBinder defBinder;
        [SerializeField] private PlayerEquipmentNet equipment;
        [SerializeField] private SkillsNet skills;
        [SerializeField] private ItemDatabase itemDatabase;

        private const int DefaultBaseStrength = 10;
        private const int DefaultBaseDexterity = 10;
        private const int DefaultBaseIntelligence = 10;
        private const float DefaultBaseMoveSpeedMult = 1f;
        private const float DefaultBaseDamage = 0f;
        private const float DefaultBaseDefence = 0f;
        private const float DefaultBaseSwingSpeed = 1f;

        private bool warnedMissingActorDef;
        private bool warnedMissingItemDb;

        private void Awake()
        {
            AutoBindReferences();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoBindReferences();
        }
#endif

        public EffectiveStats GetEffectiveStats()
        {
            ActorDef def = defBinder != null ? defBinder.ActorDef : null;

            if (def == null && !warnedMissingActorDef)
            {
                warnedMissingActorDef = true;
                Debug.LogWarning($"[Stats] ActorStatsProvider missing ActorDefBinder/ActorDef on '{name}'. Using safe baseline defaults.", this);
            }

            if (equipment != null && itemDatabase == null && !warnedMissingItemDb)
            {
                warnedMissingItemDb = true;
                Debug.LogWarning($"[Stats] ActorStatsProvider on '{name}' has equipment but no ItemDatabase assigned. Attempting equipment-local item resolution.", this);
            }

            int baseStrength = def != null ? Mathf.Max(0, def.BaseStrength) : DefaultBaseStrength;
            int baseDexterity = def != null ? Mathf.Max(0, def.BaseDexterity) : DefaultBaseDexterity;
            int baseIntelligence = def != null ? Mathf.Max(0, def.BaseIntelligence) : DefaultBaseIntelligence;

            float baseMoveSpeedMult = def != null ? Mathf.Max(0.0001f, def.BaseMoveSpeedMult) : DefaultBaseMoveSpeedMult;
            float baseDamage = def != null ? Mathf.Max(0f, def.BaseDamage) : DefaultBaseDamage;
            float baseDefence = def != null ? Mathf.Max(0f, def.BaseDefence) : DefaultBaseDefence;
            float baseSwingSpeed = def != null ? Mathf.Max(0.0001f, def.BaseSwingSpeed) : DefaultBaseSwingSpeed;

            return EffectiveStatsCalculator.Compute(
                baseStrength,
                baseDexterity,
                baseIntelligence,
                baseMoveSpeedMult,
                baseDamage,
                baseDefence,
                baseSwingSpeed,
                equipment,
                skills,
                itemDatabase);
        }

        private void AutoBindReferences()
        {
            if (defBinder == null)
                defBinder = GetComponent<ActorDefBinder>();

            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            if (skills == null)
                skills = GetComponent<SkillsNet>();
        }
    }
}
