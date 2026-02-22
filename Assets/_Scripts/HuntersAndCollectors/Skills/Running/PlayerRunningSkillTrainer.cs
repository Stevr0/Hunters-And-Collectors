using System.Collections;
using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// PlayerRunningSkillTrainer
    /// -------------------------------------------------------
    /// Increases the "Running" skill while the player is sprinting (Shift) AND actually moving.
    ///
    /// Why this exists:
    /// - We want server-authoritative XP gains (clients can't cheat XP easily).
    /// - The client only sends intent: "I am sprinting + moving".
    /// - The server decides whether to award XP over time.
    ///
    /// MVP assumptions:
    /// - Your movement input is available on the client.
    /// - Server trusts the client "enough" for MVP (you can tighten validation later).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRunningSkillTrainer : NetworkBehaviour
    {
        [Header("Input (New Input System)")]
        [Tooltip("Bind this to your Sprint/Run action (usually Left Shift).")]
        [SerializeField] private InputActionReference sprintAction;

        [Header("Movement Sensing")]
        [Tooltip("If you already have a movement script, you can reference it instead of using CharacterController velocity, etc.")]
        [SerializeField] private CharacterController characterController;

        [Tooltip("How fast the player must be moving to count as 'running'.")]
        [SerializeField] private float minMoveSpeedToCount = 0.1f;

        [Header("XP Tuning (Server Only)")]
        [Tooltip("XP granted each tick while running.")]
        [SerializeField] private int xpPerTick = 1;

        [Tooltip("Seconds between XP ticks while running.")]
        [SerializeField] private float xpTickSeconds = 1.0f;

        private SkillsNet skillsNet;

        // Server state
        private bool serverIsRunning;
        private Coroutine serverXpRoutine;

        // Client state
        private bool clientSprintHeld;

        public override void OnNetworkSpawn()
        {
            skillsNet = GetComponent<SkillsNet>();

            // Only the owning client should read input and send intent
            if (!IsOwner)
                return;

            if (sprintAction != null)
            {
                sprintAction.action.performed += OnSprintPerformed;
                sprintAction.action.canceled += OnSprintCanceled;
                sprintAction.action.Enable();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && sprintAction != null)
            {
                sprintAction.action.performed -= OnSprintPerformed;
                sprintAction.action.canceled -= OnSprintCanceled;
                sprintAction.action.Disable();
            }

            // Stop server routine if despawning
            if (IsServer)
                StopServerRunning();
        }

        private void Update()
        {
            // Only the owning client drives the "intent" updates
            if (!IsOwner)
                return;

            // We only want to award XP when sprint is held AND player is moving.
            bool isMoving = IsPlayerMoving();
            bool wantsRun = clientSprintHeld && isMoving;

            // We could spam ServerRpc every frame - don't.
            // Instead: only notify server when the desired running state changes.
            // We'll track previous wantsRun in a field.
            HandleRunIntentChange(wantsRun);
        }

        private bool lastSentWantsRun;

        private void HandleRunIntentChange(bool wantsRun)
        {
            if (wantsRun == lastSentWantsRun)
                return;

            lastSentWantsRun = wantsRun;

            // Tell server start/stop XP ticking
            SetRunningServerRpc(wantsRun);
        }

        private bool IsPlayerMoving()
        {
            // Easiest generic MVP check:
            // - If you have a CharacterController, use its velocity magnitude.
            // - Otherwise you can replace this with your movement script’s input magnitude.
            if (characterController != null)
                return characterController.velocity.magnitude > minMoveSpeedToCount;

            // Fallback: we can't detect movement reliably without a ref
            return false;
        }

        private void OnSprintPerformed(InputAction.CallbackContext ctx)
        {
            clientSprintHeld = true;
        }

        private void OnSprintCanceled(InputAction.CallbackContext ctx)
        {
            clientSprintHeld = false;
        }

        [ServerRpc]
        private void SetRunningServerRpc(bool isRunning)
        {
            // Server receives client intent.
            // MVP: accept it. Later: validate using server-side velocity/position deltas.
            if (isRunning)
                StartServerRunning();
            else
                StopServerRunning();
        }

        private void StartServerRunning()
        {
            if (!IsServer)
                return;

            if (serverIsRunning)
                return;

            serverIsRunning = true;

            // Start XP ticking
            serverXpRoutine = StartCoroutine(ServerGrantXpWhileRunning());
        }

        private void StopServerRunning()
        {
            if (!IsServer)
                return;

            if (!serverIsRunning)
                return;

            serverIsRunning = false;

            if (serverXpRoutine != null)
            {
                StopCoroutine(serverXpRoutine);
                serverXpRoutine = null;
            }
        }

        private IEnumerator ServerGrantXpWhileRunning()
        {
            // Server-only loop while running is active
            while (serverIsRunning)
            {
                if (skillsNet != null)
                {
                    // Grant Running XP
                    skillsNet.AddXp(SkillId.Running, xpPerTick);
                }

                yield return new WaitForSeconds(xpTickSeconds);
            }
        }
    }
}
