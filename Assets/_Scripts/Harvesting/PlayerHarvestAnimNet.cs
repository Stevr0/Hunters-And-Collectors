using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Replicated harvesting animation state.
    /// ------------------------------------------------------------
    /// Server sets: "this player is harvesting with tool X"
    /// Everyone reads: drives Animator so all clients see swings.
    /// </summary>
    public enum HarvestToolAnim : byte
    {
        None = 0,
        Axe = 1,
        Pickaxe = 2
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerHarvestAnimNet : NetworkBehaviour
    {
        [Header("Animator")]
        [Tooltip("Animator on the player model.")]
        [SerializeField] private Animator animator;

        [Header("Animation Timing")]
        [Tooltip("Seconds the IsHarvesting flag stays true after a swing.")]
        [SerializeField] private float swingStateDuration = 0.35f;

        // Server authoritative replicated state.
        private readonly NetworkVariable<bool> isHarvesting =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<HarvestToolAnim> toolAnim =
            new(HarvestToolAnim.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ushort> swingSequence =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Coroutine swingResetRoutine;

        private static readonly int IsHarvestingHash = Animator.StringToHash("IsHarvesting");
        private static readonly int HarvestToolHash = Animator.StringToHash("HarvestTool");
        private static readonly int HarvestSwingTriggerHash = Animator.StringToHash("HarvestSwing");

        private void Awake()
        {
            // Optional convenience: auto-find animator if not wired.
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);
        }

        public override void OnNetworkSpawn()
        {
            isHarvesting.OnValueChanged += OnStateChanged;
            toolAnim.OnValueChanged += OnToolChanged;
            swingSequence.OnValueChanged += OnSwingSequenceChanged;

            ApplyToAnimator(); // late join safe
        }

        public override void OnNetworkDespawn()
        {
            isHarvesting.OnValueChanged -= OnStateChanged;
            toolAnim.OnValueChanged -= OnToolChanged;
            swingSequence.OnValueChanged -= OnSwingSequenceChanged;

            if (swingResetRoutine != null)
            {
                StopCoroutine(swingResetRoutine);
                swingResetRoutine = null;
            }
        }

        /// <summary>
        /// SERVER ONLY: enable/disable harvesting animation.
        /// </summary>
        public void ServerSetHarvesting(bool harvesting, HarvestToolAnim tool)
        {
            if (!IsServer)
                return;

            // Always set tool first, then bool, so clients can enter state with correct tool.
            toolAnim.Value = harvesting ? tool : HarvestToolAnim.None;
            isHarvesting.Value = harvesting;
        }

        public void ServerPlaySwing(HarvestToolAnim tool)
        {
            if (!IsServer)
                return;

            toolAnim.Value = tool;
            swingSequence.Value++;

            if (swingResetRoutine != null)
            {
                StopCoroutine(swingResetRoutine);
                swingResetRoutine = null;
            }

            isHarvesting.Value = true;
            swingResetRoutine = StartCoroutine(ServerResetSwingRoutine());
        }

        private IEnumerator ServerResetSwingRoutine()
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, swingStateDuration));
            isHarvesting.Value = false;
            toolAnim.Value = HarvestToolAnim.None;
            swingResetRoutine = null;
        }

        private void OnStateChanged(bool prev, bool next) => ApplyToAnimator();
        private void OnToolChanged(HarvestToolAnim prev, HarvestToolAnim next) => ApplyToAnimator();
        private void OnSwingSequenceChanged(ushort prev, ushort next) => TriggerSwing();

        private void ApplyToAnimator()
        {
            if (animator == null)
                return;

            // Animator params you must create:
            // Bool: IsHarvesting
            // Int:  HarvestTool  (0=None, 1=Axe, 2=Pickaxe)
            animator.SetBool(IsHarvestingHash, isHarvesting.Value);
            animator.SetInteger(HarvestToolHash, (int)toolAnim.Value);
        }

        private void TriggerSwing()
        {
            if (animator == null)
                return;

            animator.SetTrigger(HarvestSwingTriggerHash);
        }
    }
}