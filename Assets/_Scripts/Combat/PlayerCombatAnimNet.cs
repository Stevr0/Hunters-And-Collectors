using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Replicated attack animation state for multiplayer melee combat.
    ///
    /// Why server-driven?
    /// - The owner has input, but remote clients do not.
    /// - Server authority ensures everyone sees the same accepted swings.
    ///
    /// Why counter-based trigger replication?
    /// - Bool flips can be missed by timing/race conditions.
    /// - A monotonically increasing counter is deterministic for all clients.
    ///
    /// Future extension:
    /// - Add hit-specific/finisher/combo counters without changing authority model.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerCombatAnimNet : NetworkBehaviour
    {
        public enum AttackStyle : byte
        {
            None = 0,
            Unarmed = 1,
            OneHand = 2,
            TwoHand = 3
        }

        [Header("Animator")]
        [SerializeField] private Animator animator;

        [Header("Timing")]
        [Tooltip("Seconds IsAttacking remains true after ServerPlayAttack.")]
        [SerializeField] private float attackStateDuration = 0.25f;

        // Replicated server-auth state (everyone reads, server writes).
        private readonly NetworkVariable<bool> isAttacking =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<byte> attackStyle =
            new((byte)AttackStyle.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> attackCounter =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Coroutine resetRoutine;
        private bool warnedMissingAnimator;

        private static readonly int AttackTriggerHash = Animator.StringToHash("Attack");
        private static readonly int AttackStyleHash = Animator.StringToHash("AttackStyle");
        private static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");

        private void Awake()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);
        }

        public override void OnNetworkSpawn()
        {
            isAttacking.OnValueChanged += OnAttackStateChanged;
            attackStyle.OnValueChanged += OnAttackStyleChanged;
            attackCounter.OnValueChanged += OnAttackCounterChanged;

            ApplyPersistentState();
        }

        public override void OnNetworkDespawn()
        {
            isAttacking.OnValueChanged -= OnAttackStateChanged;
            attackStyle.OnValueChanged -= OnAttackStyleChanged;
            attackCounter.OnValueChanged -= OnAttackCounterChanged;

            if (resetRoutine != null)
            {
                StopCoroutine(resetRoutine);
                resetRoutine = null;
            }
        }

        /// <summary>
        /// SERVER ONLY: replicate one attack animation event to all clients.
        /// </summary>
        public void ServerPlayAttack(AttackStyle style)
        {
            if (!IsServer)
                return;

            attackStyle.Value = (byte)style;
            attackCounter.Value++;
            isAttacking.Value = true;

            if (resetRoutine != null)
                StopCoroutine(resetRoutine);

            resetRoutine = StartCoroutine(ServerResetAttackStateRoutine());
        }

        private IEnumerator ServerResetAttackStateRoutine()
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, attackStateDuration));
            isAttacking.Value = false;
            resetRoutine = null;
        }

        private void OnAttackStateChanged(bool previousValue, bool newValue) => ApplyPersistentState();
        private void OnAttackStyleChanged(byte previousValue, byte newValue) => ApplyPersistentState();
        private void OnAttackCounterChanged(int previousValue, int newValue) => TriggerAttack();

        private void ApplyPersistentState()
        {
            if (!EnsureAnimator())
                return;

            animator.SetBool(IsAttackingHash, isAttacking.Value);
            animator.SetInteger(AttackStyleHash, attackStyle.Value);
        }

        private void TriggerAttack()
        {
            if (!EnsureAnimator())
                return;

            animator.SetInteger(AttackStyleHash, attackStyle.Value);
            animator.SetTrigger(AttackTriggerHash);
        }

        private bool EnsureAnimator()
        {
            if (animator != null)
                return true;

            if (!warnedMissingAnimator)
            {
                Debug.LogWarning("[Combat] PlayerCombatAnimNet missing Animator; combat animation replication skipped.", this);
                warnedMissingAnimator = true;
            }

            return false;
        }
    }
}
