using System.Collections.Generic;
using HuntersAndCollectors.Combat;
using HuntersAndCollectors.Skills;
using HuntersAndCollectors.Stats;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Server-authoritative NPC actor brain.
    ///
    /// This controller intentionally has no player/NPC/dummy branching.
    /// Any actor with ActorIdentityNet + DamageableNet + HealthNet can use it.
    ///
    /// Runtime behavior states:
    /// - Hold: stay at home anchor.
    /// - Patrol: wander around home anchor.
    /// - Chase: move toward hostile target.
    /// - Attack: resolve d20 melee attacks at range.
    /// - Retreat: move away when health is low.
    /// - ReturnHome: leash reset and idle recovery.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorAIController : MonoBehaviour
    {
        private enum BrainState
        {
            Hold,
            Patrol,
            Chase,
            Attack,
            Retreat,
            ReturnHome
        }

        [Header("Definition")]
        [SerializeField] private ActorAIDef aiDef;

        [Header("References")]
        [SerializeField] private ActorIdentityNet actorIdentity;
        [SerializeField] private DamageableNet selfDamageable;
        [SerializeField] private HealthNet health;
        [SerializeField] private SkillsNet skills;
        [SerializeField] private NavMeshAgent navAgent;

        [Header("Perception")]
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private LayerMask occlusionMask = ~0;

        [Header("Fallback Movement")]
        [Tooltip("Used when no NavMeshAgent is present/on-mesh.")]
        [Min(0.1f)]
        [SerializeField] private float fallbackMoveSpeed = 3.5f;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges;

        private readonly Collider[] _perceptionBuffer = new Collider[32];
        private readonly HashSet<ulong> _warnedMissingTargetStats = new();

        private IStatsProvider _selfStats;
        private NetworkObject _networkObject;

        private BrainState _state;
        private bool _initialized;

        private Vector3 _homePosition;
        private Quaternion _homeRotation;

        private DamageableNet _targetDamageable;
        private ActorIdentityNet _targetIdentity;

        private float _nextThinkTime;
        private float _nextAttackTime;
        private float _retreatUntilTime;
        private float _patrolPauseUntilTime;

        private Vector3 _patrolPoint;
        private bool _hasPatrolPoint;

        private bool _warnedMissingIdentity;
        private float _baseNavSpeed = 3.5f;
        private bool _warnedMissingStats;

        private void Awake()
        {
            AutoBind();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoBind();
            fallbackMoveSpeed = Mathf.Max(0.1f, fallbackMoveSpeed);
        }
#endif

        /// <summary>
        /// Allows spawner/bootstrap code to inject AI tuning at runtime.
        /// </summary>
        public void SetRuntimeDef(ActorAIDef def)
        {
            aiDef = def;
            _initialized = false;
        }

        private void Update()
        {
            if (!IsServerAuthoritativeInstance())
                return;

            ResolveDefFallbacks();

            if (!_initialized)
                InitializeServerBrain();

            if (aiDef == null)
                return;

            if (Time.time < _nextThinkTime)
                return;

            _nextThinkTime = Time.time + Mathf.Max(0.02f, aiDef.DecisionIntervalSeconds);
            TickBrain();
        }

        private void ResolveDefFallbacks()
        {
            if (aiDef != null)
                return;

            ActorDefBinder binder = GetComponent<ActorDefBinder>();
            if (binder != null && binder.ActorDef != null)
                aiDef = binder.ActorDef.AiDef;
        }

        private void InitializeServerBrain()
        {
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
            _state = aiDef != null && aiDef.IdleMode == ActorIdleMode.Patrol ? BrainState.Patrol : BrainState.Hold;
            _initialized = true;

            if (logStateChanges)
                Debug.Log($"[ActorAI] {name} state={_state}", this);
        }

        private void TickBrain()
        {
            ValidateOrAcquireTarget();

            if (_targetDamageable != null && ShouldRetreat())
            {
                EnterState(BrainState.Retreat);
                _retreatUntilTime = Time.time + Mathf.Max(0.1f, aiDef.RetreatDurationSeconds);
            }

            switch (_state)
            {
                case BrainState.Hold:
                    TickHold();
                    break;
                case BrainState.Patrol:
                    TickPatrol();
                    break;
                case BrainState.Chase:
                    TickChase();
                    break;
                case BrainState.Attack:
                    TickAttack();
                    break;
                case BrainState.Retreat:
                    TickRetreat();
                    break;
                case BrainState.ReturnHome:
                    TickReturnHome();
                    break;
            }
        }

        private void TickHold()
        {
            StopMovement();

            if (_targetDamageable != null)
            {
                EnterState(BrainState.Chase);
                return;
            }

            if (aiDef.IdleMode == ActorIdleMode.Patrol)
                EnterState(BrainState.Patrol);
        }

        private void TickPatrol()
        {
            if (_targetDamageable != null)
            {
                EnterState(BrainState.Chase);
                return;
            }

            if (Time.time < _patrolPauseUntilTime)
            {
                StopMovement();
                return;
            }

            if (!_hasPatrolPoint)
            {
                _patrolPoint = PickPatrolPointAroundHome();
                _hasPatrolPoint = true;
            }

            MoveTowards(_patrolPoint, aiDef.PatrolArrivalDistance);

            if (Vector3.Distance(transform.position, _patrolPoint) <= Mathf.Max(0.1f, aiDef.PatrolArrivalDistance))
            {
                _hasPatrolPoint = false;
                _patrolPauseUntilTime = Time.time + Mathf.Max(0f, aiDef.PatrolPauseSeconds);
                StopMovement();
            }
        }

        private void TickChase()
        {
            if (!HasValidTarget())
            {
                EnterState(BrainState.ReturnHome);
                return;
            }

            if (IsBeyondLeash())
            {
                ClearTarget();
                EnterState(BrainState.ReturnHome);
                return;
            }

            float distance = DistanceToTarget();
            if (distance <= Mathf.Max(0.1f, aiDef.AttackRange))
            {
                EnterState(BrainState.Attack);
                return;
            }

            MoveTowards(_targetDamageable.transform.position, Mathf.Max(0.1f, aiDef.AttackRange * 0.9f));
        }

        private void TickAttack()
        {
            if (!HasValidTarget())
            {
                EnterState(BrainState.ReturnHome);
                return;
            }

            if (IsBeyondLeash())
            {
                ClearTarget();
                EnterState(BrainState.ReturnHome);
                return;
            }

            float distance = DistanceToTarget();
            float maxAttackDistance = Mathf.Max(0.1f, aiDef.AttackRange + Mathf.Max(0f, aiDef.AttackRangeHysteresis));
            if (distance > maxAttackDistance)
            {
                EnterState(BrainState.Chase);
                return;
            }

            StopMovement();
            FaceTarget();
            TryPerformAttack();
        }

        private void TickRetreat()
        {
            if (_targetDamageable == null)
            {
                EnterState(BrainState.ReturnHome);
                return;
            }

            Vector3 away = transform.position - _targetDamageable.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = -transform.forward;

            away.Normalize();

            Vector3 retreatDestination = transform.position + away * Mathf.Max(0.1f, aiDef.RetreatDistance);
            MoveTowards(retreatDestination, 0.25f);

            float distance = DistanceToTarget();
            bool safeDistance = distance >= Mathf.Max(0.1f, aiDef.RetreatDistance);
            bool timedOut = Time.time >= _retreatUntilTime;

            if (safeDistance || timedOut)
            {
                ClearTarget();
                EnterState(BrainState.ReturnHome);
            }
        }

        private void TickReturnHome()
        {
            if (_targetDamageable != null && !ShouldRetreat() && !IsBeyondLeash())
            {
                EnterState(BrainState.Chase);
                return;
            }

            MoveTowards(_homePosition, Mathf.Max(0.1f, aiDef.ReturnHomeDistance));

            if (Vector3.Distance(transform.position, _homePosition) <= Mathf.Max(0.1f, aiDef.ReturnHomeDistance))
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, _homeRotation, 0.5f);
                EnterState(aiDef.IdleMode == ActorIdleMode.Patrol ? BrainState.Patrol : BrainState.Hold);
            }
        }

        private void ValidateOrAcquireTarget()
        {
            if (HasValidTarget())
                return;

            ClearTarget();

            if (actorIdentity == null)
            {
                if (!_warnedMissingIdentity)
                {
                    _warnedMissingIdentity = true;
                    Debug.LogWarning($"[ActorAI] Missing ActorIdentityNet on '{name}'.", this);
                }

                return;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(
                transform.position,
                Mathf.Max(0.1f, aiDef.AggroRange),
                _perceptionBuffer,
                ResolveTargetMask(),
                QueryTriggerInteraction.Ignore);

            float bestDistance = float.PositiveInfinity;
            DamageableNet bestTarget = null;
            ActorIdentityNet bestIdentity = null;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _perceptionBuffer[i];
                if (col == null)
                    continue;

                DamageableNet candidate = col.GetComponentInParent<DamageableNet>();
                if (candidate == null || candidate == selfDamageable)
                    continue;

                if (!candidate.IsSpawned)
                    continue;

                HealthNet candidateHealth = candidate.GetComponent<HealthNet>();
                if (candidateHealth != null && candidateHealth.CurrentHealth <= 0)
                    continue;

                ActorIdentityNet candidateIdentity = candidate.GetComponentInParent<ActorIdentityNet>();
                if (!HostilityResolver.CanAttack(actorIdentity, candidateIdentity))
                    continue;

                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance > aiDef.AggroRange)
                    continue;

                if (aiDef.RequireLineOfSight && !HasLineOfSightTo(candidate.transform.position, candidate.transform.root))
                    continue;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = candidate;
                    bestIdentity = candidateIdentity;
                }
            }

            if (bestTarget != null)
            {
                _targetDamageable = bestTarget;
                _targetIdentity = bestIdentity;
            }
        }

        private bool HasValidTarget()
        {
            if (_targetDamageable == null || _targetIdentity == null)
                return false;

            if (!_targetDamageable.IsSpawned)
                return false;

            if (!HostilityResolver.CanAttack(actorIdentity, _targetIdentity))
                return false;

            HealthNet targetHealth = _targetDamageable.GetComponent<HealthNet>();
            if (targetHealth != null && targetHealth.CurrentHealth <= 0)
                return false;

            float distance = DistanceToTarget();
            if (distance > Mathf.Max(aiDef.LoseTargetRange, aiDef.AttackRange + aiDef.AttackRangeHysteresis))
                return false;

            if (aiDef.RequireLineOfSight && !HasLineOfSightTo(_targetDamageable.transform.position, _targetDamageable.transform.root))
                return false;

            return true;
        }

        private bool ShouldRetreat()
        {
            if (health == null || aiDef == null)
                return false;

            return health.Health01 <= Mathf.Clamp01(aiDef.RetreatHealth01);
        }

        private bool IsBeyondLeash()
        {
            float leash = Mathf.Max(0.1f, aiDef.LeashRange);
            return Vector3.Distance(transform.position, _homePosition) > leash;
        }

        private float DistanceToTarget()
        {
            if (_targetDamageable == null)
                return float.PositiveInfinity;

            return Vector3.Distance(transform.position, _targetDamageable.transform.position);
        }

        private void TryPerformAttack()
        {
            if (Time.time < _nextAttackTime)
                return;

            if (_targetDamageable == null)
                return;

            if (!HostilityResolver.CanAttack(actorIdentity, _targetIdentity))
                return;

            EffectiveStats selfStats = GetSelfEffectiveStats();
            int baseDamage = Mathf.Max(1, Mathf.RoundToInt(selfStats.Damage));
            int targetDefence = ResolveTargetDefence(_targetDamageable);

            int skillBonus = 0;
            if (skills != null && !string.IsNullOrWhiteSpace(selfStats.ActiveCombatSkillId))
            {
                int level = Mathf.Clamp(skills.GetLevel(selfStats.ActiveCombatSkillId), 0, 100);
                skillBonus = level / 10;
            }

            int totalAttackBonus = Mathf.Max(0, aiDef.FlatAttackBonus) + skillBonus;
            CombatResolution resolution = CombatResolver.ResolveMeleeAttack(baseDamage, totalAttackBonus, targetDefence);

            float swingSpeed = Mathf.Max(0.01f, selfStats.SwingSpeed);
            float interval = aiDef.AttackIntervalOverrideSeconds > 0f
                ? aiDef.AttackIntervalOverrideSeconds
                : 1f / swingSpeed;

            _nextAttackTime = Time.time + Mathf.Max(0.02f, interval);

            if (!resolution.IsHit)
                return;

            Vector3 hitPoint = _targetDamageable.transform.position + Vector3.up;
            _targetDamageable.ServerTryApplyDamage(resolution.FinalDamage, ulong.MaxValue, hitPoint);
        }

        private int ResolveTargetDefence(DamageableNet targetDamageable)
        {
            const int fallbackDefence = 10;

            if (targetDamageable == null)
                return fallbackDefence;

            IStatsProvider provider = targetDamageable.GetComponentInParent<IStatsProvider>();
            if (provider != null)
            {
                EffectiveStats stats = provider.GetEffectiveStats();
                return Mathf.Max(0, Mathf.RoundToInt(stats.Defence));
            }

            ulong targetId = targetDamageable.NetworkObjectId;
            if (_warnedMissingTargetStats.Add(targetId))
                Debug.LogWarning($"[ActorAI] Missing IStatsProvider on target '{targetDamageable.name}', using defence {fallbackDefence}.", targetDamageable);

            return fallbackDefence;
        }

        private EffectiveStats GetSelfEffectiveStats()
        {
            if (_selfStats == null)
                _selfStats = GetComponent<IStatsProvider>();

            if (_selfStats != null)
                return _selfStats.GetEffectiveStats();

            if (!_warnedMissingStats)
            {
                _warnedMissingStats = true;
                Debug.LogWarning($"[ActorAI] Missing IStatsProvider on '{name}'. Using combat fallbacks.", this);
            }

            return new EffectiveStats
            {
                Damage = 1f,
                SwingSpeed = 1f,
                MoveSpeedMult = 1f,
                Defence = 0f,
                ActiveCombatSkillId = string.Empty
            };
        }

        private bool HasLineOfSightTo(Vector3 worldTarget, Transform expectedTargetRoot)
        {
            if (!aiDef.RequireLineOfSight)
                return true;

            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 target = worldTarget + Vector3.up * 1.0f;
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= 0.01f)
                return true;

            int mask = ResolveOcclusionMask();
            if (Physics.Raycast(origin, delta / distance, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
            {
                Transform hitRoot = hit.collider != null ? hit.collider.transform.root : null;
                Transform targetRoot = expectedTargetRoot;
                if (hitRoot != null && targetRoot != null && hitRoot == targetRoot)
                    return true;

                // Ignore own colliders as blockers.
                if (hitRoot != null && hitRoot == transform.root)
                    return true;

                return false;
            }

            return true;
        }

        private void MoveTowards(Vector3 destination, float stoppingDistance)
        {
            float moveScale = Mathf.Max(0.1f, aiDef.MoveSpeedMultiplier);
            float statMove = Mathf.Max(0.1f, GetSelfEffectiveStats().MoveSpeedMult);

            if (navAgent != null && navAgent.enabled)
            {
                if (navAgent.isOnNavMesh)
                {
                    navAgent.isStopped = false;
                    navAgent.speed = Mathf.Max(0.1f, _baseNavSpeed) * moveScale * statMove;
                    navAgent.stoppingDistance = Mathf.Max(0f, stoppingDistance);
                    navAgent.SetDestination(destination);
                    return;
                }
            }

            Vector3 delta = destination - transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist <= Mathf.Max(0f, stoppingDistance))
                return;

            float speed = Mathf.Max(0.1f, fallbackMoveSpeed * moveScale * statMove);
            Vector3 step = delta.normalized * (speed * Mathf.Max(0.01f, aiDef.DecisionIntervalSeconds));
            if (step.magnitude > dist)
                step = delta;

            transform.position += step;

            if (step.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(step.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 0.35f);
            }
        }

        private void StopMovement()
        {
            if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
                navAgent.isStopped = true;
        }

        private void FaceTarget()
        {
            if (_targetDamageable == null)
                return;

            Vector3 toTarget = _targetDamageable.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f)
                return;

            Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 0.45f);
        }

        private Vector3 PickPatrolPointAroundHome()
        {
            Vector2 ring = Random.insideUnitCircle * Mathf.Max(0.1f, aiDef.PatrolRadius);
            Vector3 point = _homePosition + new Vector3(ring.x, 0f, ring.y);

            if (navAgent != null && navAgent.enabled && NavMesh.SamplePosition(point, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                point = hit.position;

            return point;
        }

        private void EnterState(BrainState next)
        {
            if (_state == next)
                return;

            _state = next;
            if (logStateChanges)
                Debug.Log($"[ActorAI] {name} state={_state}", this);
        }

        private void ClearTarget()
        {
            _targetDamageable = null;
            _targetIdentity = null;
        }

        private bool IsServerAuthoritativeInstance()
        {
            if (_networkObject == null)
                _networkObject = GetComponent<NetworkObject>();

            if (_networkObject != null)
            {
                if (!_networkObject.IsSpawned)
                    return false;

                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsServer;
            }

            NetworkManager fallbackManager = NetworkManager.Singleton;
            return fallbackManager != null && fallbackManager.IsServer;
        }

        private int ResolveTargetMask()
        {
            if (targetMask.value != 0)
                return targetMask.value;

            if (aiDef != null && aiDef.TargetMask.value != 0)
                return aiDef.TargetMask.value;

            return Physics.DefaultRaycastLayers;
        }

        private int ResolveOcclusionMask()
        {
            if (occlusionMask.value != 0)
                return occlusionMask.value;

            if (aiDef != null && aiDef.OcclusionMask.value != 0)
                return aiDef.OcclusionMask.value;

            return Physics.DefaultRaycastLayers;
        }

        private void AutoBind()
        {
            if (actorIdentity == null)
                actorIdentity = GetComponent<ActorIdentityNet>();

            if (selfDamageable == null)
                selfDamageable = GetComponent<DamageableNet>();

            if (health == null)
                health = GetComponent<HealthNet>();

            if (skills == null)
                skills = GetComponent<SkillsNet>();

            if (navAgent == null)
                navAgent = GetComponent<NavMeshAgent>();

            if (navAgent != null)
                _baseNavSpeed = Mathf.Max(0.1f, navAgent.speed);

            if (_networkObject == null)
                _networkObject = GetComponent<NetworkObject>();

            if (_selfStats == null)
                _selfStats = GetComponent<IStatsProvider>();
        }
    }
}






