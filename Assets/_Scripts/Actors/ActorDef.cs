using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Broad authored classification for an actor blueprint.
    ///
    /// This is static content metadata only. Runtime state machines and live instances should not
    /// mutate this value; they should instead read it during initialization or for rule lookups.
    /// </summary>
    public enum ActorCategory
    {
        Player = 0,
        Creature = 1,
        Humanoid = 2,
        Vendor = 3,
        Dummy = 4,
        Boss = 5,
        Critter = 6
    }

    /// <summary>
    /// Coarse body-size buckets for authored content.
    ///
    /// These values are intended for design-time rules, spawn filtering, and future combat logic.
    /// They are not a replacement for the actual collider configuration on spawned prefabs.
    /// </summary>
    public enum ActorSize
    {
        Small = 0,
        Medium = 1,
        Large = 2,
        Boss = 3
    }

    /// <summary>
    /// First-pass elemental damage categories for static actor authoring.
    ///
    /// Runtime damage systems can expand this later without needing to re-think the ActorDef layout.
    /// </summary>
    public enum DamageType
    {
        Physical = 0,
        Fire = 1,
        Frost = 2,
        Poison = 3,
        Arcane = 4
    }

    /// <summary>
    /// Authored default behaviour intent for an actor type.
    ///
    /// This is not live AI state. Runtime systems may read it during setup to choose the right
    /// controller/profile, but they should store active behaviour elsewhere.
    /// </summary>
    public enum BehaviourMode
    {
        Passive = 0,
        Defensive = 1,
        Aggressive = 2,
        Vendor = 3,
        Dummy = 4
    }

    /// <summary>
    /// Static authored blueprint for an actor type.
    ///
    /// Important architectural rule:
    /// - This asset contains design-time data only.
    /// - Runtime systems should read from ActorDef during initialization/spawn.
    /// - Live state such as current HP, current target, active buffs, world position, and AI state
    ///   must live on runtime components or save data, never in this asset.
    ///
    /// Compatibility note:
    /// - The project already has runtime systems that read older baseline fields such as strength,
    ///   damage, defence, and swing speed.
    /// - Those values are preserved here so existing systems continue to work while the definition
    ///   grows into a more production-ready content asset.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Actors/ActorDef")]
    public sealed class ActorDef : ScriptableObject
    {
        private const float MinPositiveFloat = 0.0001f;
        private const int MinResistance = -100;
        private const int MaxResistance = 100;

        [Serializable]
        public struct StartingSkill
        {
            [Tooltip("String skill id to seed on spawn. Prefer using values from SkillId.")]
            public string SkillId;

            [Range(0, 100)]
            [Tooltip("Starting level applied when the actor instance is initialized on the server.")]
            public int Level;
        }

        [Header("Identity")]
        [FormerlySerializedAs("ActorId")]
        [SerializeField] private string actorId = string.Empty;
        [FormerlySerializedAs("DisplayName")]
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private Sprite icon;
        [SerializeField] private GameObject prefab;
        [TextArea(2, 6)]
        [SerializeField] private string description = string.Empty;

        [Header("Classification")]
        [SerializeField] private ActorCategory category = ActorCategory.Creature;
        [FormerlySerializedAs("DefaultFactionId")]
        [SerializeField] private int factionId = 0;
        [FormerlySerializedAs("DefaultPvpEnabled")]
        [SerializeField] private bool defaultPvpEnabled = false;
        [SerializeField] private bool isHostile = false;
        [SerializeField] private bool canBeTargeted = true;
        [SerializeField] private bool canBeDamaged = true;
        [SerializeField] private bool canInteract = false;
        [SerializeField] private bool countsAsWildlife = false;
        [SerializeField] private bool countsAsCivilized = false;
        [SerializeField] private bool bossFlag = false;
        [SerializeField] private bool eliteFlag = false;

        [Header("Base Vitals")]
        [Min(1)]
        [SerializeField] private int baseHealth = 100;
        [Min(0)]
        [SerializeField] private int baseStamina = 50;
        [Min(0)]
        [SerializeField] private int baseMana = 0;

        [Header("Base Regeneration")]
        [Min(0f)]
        [SerializeField] private float baseHealthRegenPerSecond = 0f;
        [Min(0f)]
        [SerializeField] private float baseStaminaRegenPerSecond = 0f;
        [Min(0f)]
        [SerializeField] private float baseManaRegenPerSecond = 0f;

        [Header("Combat")]
        [SerializeField] private int attackBonus = 0;
        [SerializeField] private int defenceBonus = 0;
        [Min(0)]
        [SerializeField] private int baseDamageMin = 1;
        [Min(0)]
        [SerializeField] private int baseDamageMax = 3;
        [Min(MinPositiveFloat)]
        [SerializeField] private float attackRange = 1.75f;
        [Min(MinPositiveFloat)]
        [SerializeField] private float attackIntervalSeconds = 1.2f;
        [Min(0f)]
        [SerializeField] private float aggroRange = 8f;
        [Min(0f)]
        [SerializeField] private float leashRange = 18f;
        [SerializeField] private DamageType primaryDamageType = DamageType.Physical;

        [Header("Resistances")]
        [Range(MinResistance, MaxResistance)]
        [SerializeField] private int physicalResistance = 0;
        [Range(MinResistance, MaxResistance)]
        [SerializeField] private int fireResistance = 0;
        [Range(MinResistance, MaxResistance)]
        [SerializeField] private int frostResistance = 0;
        [Range(MinResistance, MaxResistance)]
        [SerializeField] private int poisonResistance = 0;
        [Range(MinResistance, MaxResistance)]
        [SerializeField] private int arcaneResistance = 0;

        [Header("Movement")]
        [Min(0f)]
        [SerializeField] private float walkSpeed = 2f;
        [Min(0f)]
        [SerializeField] private float runSpeed = 4f;
        [Min(0f)]
        [SerializeField] private float rotationSpeed = 540f;
        [SerializeField] private bool canRoam = true;
        [Min(0f)]
        [SerializeField] private float roamRadius = 8f;

        [Header("Body / Size")]
        [SerializeField] private ActorSize size = ActorSize.Medium;
        [Min(MinPositiveFloat)]
        [SerializeField] private float collisionRadius = 0.4f;
        [Min(MinPositiveFloat)]
        [SerializeField] private float height = 1.8f;
        [Min(0f)]
        [SerializeField] private float interactionRadius = 2f;

        [Header("AI")]
        [FormerlySerializedAs("AiDef")]
        [SerializeField] private ActorAIDef aiProfile;
        [SerializeField] private BehaviourMode defaultBehaviourMode = BehaviourMode.Defensive;

        [Header("Loot / Rewards")]
        [SerializeField] private ActorLootTableDef lootTable;
        [Min(0)]
        [SerializeField] private int coinDropMin = 0;
        [Min(0)]
        [SerializeField] private int coinDropMax = 0;
        [Min(0)]
        [SerializeField] private int xpReward = 0;
        [SerializeField] private string xpSkillId = string.Empty;

        [Header("Spawn Metadata")]
        [Min(0)]
        [SerializeField] private int spawnCost = 1;
        [Min(1)]
        [SerializeField] private int difficultyTier = 1;
        [SerializeField] private bool allowZoneSpawning = true;
        [SerializeField] private bool uniqueActor = false;
        [Min(0)]
        [SerializeField] private int globalAliveLimit = 0;

        [Header("Legacy Runtime Compatibility")]
        [Tooltip("Current runtime stats still read these authored attributes. Keep them populated until the stat pipeline is migrated.")]
        [FormerlySerializedAs("BaseStrength")]
        [SerializeField] private int baseStrength = 10;
        [FormerlySerializedAs("BaseDexterity")]
        [SerializeField] private int baseDexterity = 10;
        [FormerlySerializedAs("BaseIntelligence")]
        [SerializeField] private int baseIntelligence = 10;
        [FormerlySerializedAs("BaseMoveSpeedMult")]
        [SerializeField] private float baseMoveSpeedMult = 1f;
        [FormerlySerializedAs("BaseDamage")]
        [SerializeField] private float baseDamage = 0f;
        [FormerlySerializedAs("BaseDefence")]
        [SerializeField] private float baseDefence = 0f;
        [FormerlySerializedAs("BaseSwingSpeed")]
        [SerializeField] private float baseSwingSpeed = 1f;

        [Header("Starting Skills")]
        [FormerlySerializedAs("StartingSkills")]
        [SerializeField] private StartingSkill[] startingSkills;

        public string ActorId => actorId;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public GameObject Prefab => prefab;
        public string Description => description;

        public ActorCategory Category => category;
        public int FactionId => factionId;
        public int DefaultFactionId => factionId;
        public bool DefaultPvpEnabled => defaultPvpEnabled;
        public bool IsHostile => isHostile;
        public bool CanBeTargeted => canBeTargeted;
        public bool CanBeDamaged => canBeDamaged;
        public bool CanInteract => canInteract;
        public bool CountsAsWildlife => countsAsWildlife;
        public bool CountsAsCivilized => countsAsCivilized;
        public bool BossFlag => bossFlag;
        public bool EliteFlag => eliteFlag;
        public bool IsBossVariant => bossFlag;
        public bool IsEliteVariant => eliteFlag;

        public int BaseHealth => baseHealth;
        public int BaseStamina => baseStamina;
        public int BaseMana => baseMana;
        public float BaseHealthRegenPerSecond => baseHealthRegenPerSecond;
        public float BaseStaminaRegenPerSecond => baseStaminaRegenPerSecond;
        public float BaseManaRegenPerSecond => baseManaRegenPerSecond;

        public int AttackBonus => attackBonus;
        public int DefenceBonus => defenceBonus;
        public int BaseDamageMin => baseDamageMin;
        public int BaseDamageMax => baseDamageMax;
        public float AttackRange => attackRange;
        public float AttackIntervalSeconds => attackIntervalSeconds;
        public float AggroRange => aggroRange;
        public float LeashRange => leashRange;
        public float HomeRadius => leashRange;
        public DamageType PrimaryDamageType => primaryDamageType;

        public int PhysicalResistance => physicalResistance;
        public int FireResistance => fireResistance;
        public int FrostResistance => frostResistance;
        public int PoisonResistance => poisonResistance;
        public int ArcaneResistance => arcaneResistance;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float MoveSpeed => runSpeed;
        public float RotationSpeed => rotationSpeed;
        public bool CanRoam => canRoam;
        public float RoamRadius => roamRadius;

        public ActorSize Size => size;
        public float CollisionRadius => collisionRadius;
        public float Height => height;
        public float InteractionRadius => interactionRadius;

        public ActorAIDef AiProfile => aiProfile;
        public ActorAIDef AiDef => aiProfile;
        public BehaviourMode DefaultBehaviourMode => defaultBehaviourMode;

        public ActorLootTableDef LootTable => lootTable;
        public int CoinDropMin => coinDropMin;
        public int CoinDropMax => coinDropMax;
        public int XpReward => xpReward;
        public string XpSkillId => xpSkillId;

        public int SpawnCost => spawnCost;
        public int DifficultyTier => difficultyTier;
        public bool AllowZoneSpawning => allowZoneSpawning;
        public bool UniqueActor => uniqueActor;
        public int GlobalAliveLimit => globalAliveLimit;

        // These compatibility properties preserve current callers while keeping the inspector grouped.
        public int BaseStrength => baseStrength;
        public int BaseDexterity => baseDexterity;
        public int BaseIntelligence => baseIntelligence;
        public float BaseMoveSpeedMult => baseMoveSpeedMult;
        public float BaseDamage => baseDamage;
        public float BaseDefence => baseDefence;
        public float BaseSwingSpeed => baseSwingSpeed;
        public StartingSkill[] StartingSkills => startingSkills;

#if UNITY_EDITOR
        private void OnValidate()
        {
            actorId = SanitizeSingleLine(actorId);
            displayName = SanitizeSingleLine(displayName);
            description = description == null ? string.Empty : description.Trim();
            xpSkillId = SanitizeSingleLine(xpSkillId);

            baseHealth = Mathf.Max(1, baseHealth);
            baseStamina = Mathf.Max(0, baseStamina);
            baseMana = Mathf.Max(0, baseMana);

            baseHealthRegenPerSecond = Mathf.Max(0f, baseHealthRegenPerSecond);
            baseStaminaRegenPerSecond = Mathf.Max(0f, baseStaminaRegenPerSecond);
            baseManaRegenPerSecond = Mathf.Max(0f, baseManaRegenPerSecond);

            baseDamageMin = Mathf.Max(0, baseDamageMin);
            baseDamageMax = Mathf.Max(baseDamageMin, baseDamageMax);
            attackRange = Mathf.Max(MinPositiveFloat, attackRange);
            attackIntervalSeconds = Mathf.Max(MinPositiveFloat, attackIntervalSeconds);
            aggroRange = Mathf.Max(0f, aggroRange);
            leashRange = Mathf.Max(aggroRange, leashRange);

            physicalResistance = Mathf.Clamp(physicalResistance, MinResistance, MaxResistance);
            fireResistance = Mathf.Clamp(fireResistance, MinResistance, MaxResistance);
            frostResistance = Mathf.Clamp(frostResistance, MinResistance, MaxResistance);
            poisonResistance = Mathf.Clamp(poisonResistance, MinResistance, MaxResistance);
            arcaneResistance = Mathf.Clamp(arcaneResistance, MinResistance, MaxResistance);

            walkSpeed = Mathf.Max(0f, walkSpeed);
            runSpeed = Mathf.Max(walkSpeed, runSpeed);
            rotationSpeed = Mathf.Max(0f, rotationSpeed);
            roamRadius = Mathf.Max(0f, roamRadius);

            collisionRadius = Mathf.Max(MinPositiveFloat, collisionRadius);
            height = Mathf.Max(MinPositiveFloat, height);
            interactionRadius = Mathf.Max(0f, interactionRadius);

            coinDropMin = Mathf.Max(0, coinDropMin);
            coinDropMax = Mathf.Max(coinDropMin, coinDropMax);
            xpReward = Mathf.Max(0, xpReward);

            spawnCost = Mathf.Max(0, spawnCost);
            difficultyTier = Mathf.Max(1, difficultyTier);
            globalAliveLimit = Mathf.Max(0, globalAliveLimit);

            baseStrength = Mathf.Max(0, baseStrength);
            baseDexterity = Mathf.Max(0, baseDexterity);
            baseIntelligence = Mathf.Max(0, baseIntelligence);
            baseMoveSpeedMult = Mathf.Max(MinPositiveFloat, baseMoveSpeedMult);
            baseDamage = Mathf.Max(0f, baseDamage);
            baseDefence = Mathf.Max(0f, baseDefence);
            baseSwingSpeed = Mathf.Max(MinPositiveFloat, baseSwingSpeed);

            if (startingSkills == null)
                return;

            for (int i = 0; i < startingSkills.Length; i++)
            {
                StartingSkill skill = startingSkills[i];
                skill.SkillId = SanitizeSingleLine(skill.SkillId);
                skill.Level = Mathf.Clamp(skill.Level, 0, 100);
                startingSkills[i] = skill;
            }
        }

        private static string SanitizeSingleLine(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
#endif
    }
}
