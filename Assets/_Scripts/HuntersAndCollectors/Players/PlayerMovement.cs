using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// PlayerMovement (Server Authoritative)
    /// -------------------------------------------------------
    /// Client (Owner):
    /// - Reads input
    /// - Applies local camera pitch
    /// - Sends movement intent to server (move vector, sprint flag, yaw delta)
    ///
    /// Server:
    /// - Applies yaw rotation to the player transform
    /// - Moves CharacterController using server-computed speed (Running skill)
    /// - Grants Running XP over time while sprinting + moving
    ///
    /// Notes:
    /// - Requires a NetworkTransform (or equivalent) to replicate server position/rotation to clients.
    /// - CharacterController should exist on the server instance as well.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        [Header("Speed Tuning")]
        [Tooltip("Walk speed (no sprint). Example: 2")]
        [SerializeField] private float walkSpeed = 2f;

        [Tooltip("Max possible speed when Running skill is 100. Example: 10")]
        [SerializeField] private float maxMoveSpeed = 10f;

        [Header("Mouse Look")]
        [SerializeField] private float mouseSensitivity = 2.5f;
        [SerializeField] private float pitchMin = -80f;
        [SerializeField] private float pitchMax = 80f;
        [SerializeField] private Transform cameraPivot; // assign in inspector (local visual only)

        [Header("Running XP (Server)")]
        [Tooltip("Seconds between running XP ticks while sprinting & moving.")]
        [SerializeField] private float runningXpTickSeconds = 1.0f;

        [Tooltip("XP granted per tick while sprinting & moving.")]
        [SerializeField] private int runningXpPerTick = 1;

        [Tooltip("Minimum input magnitude to count as 'moving' for running XP.")]
        [SerializeField] private float minMoveInputToCount = 0.1f;

        private CharacterController controller;
        private SkillsNet skillsNet;

        // --- Client-side input state (owner only) ---
        private PlayerInputActions input;
        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool sprintHeld;
        private float pitch;

        // --- Server-side cached intent (authoritative movement uses these) ---
        private Vector2 serverMoveInput;
        private bool serverSprintHeld;
        private float serverYawDelta;

        // --- Server-side XP ticking ---
        private float serverRunningXpTimer;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            skillsNet = GetComponent<SkillsNet>();

            // Owner input wrapper
            input = new PlayerInputActions();
        }

        public override void OnNetworkSpawn()
        {
            // Owner reads input. Everyone else (including server for non-owner clients) does not.
            if (IsOwner)
            {
                // Movement input
                input.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
                input.Player.Move.canceled += _ => moveInput = Vector2.zero;

                // Look input (Mouse Delta)
                input.Player.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();
                input.Player.Look.canceled += _ => lookInput = Vector2.zero;

                // Sprint input (you must have this action in your input actions)
                input.Player.Sprint.performed += _ => sprintHeld = true;
                input.Player.Sprint.canceled += _ => sprintHeld = false;

                input.Enable();

                // Lock cursor for FPS-style control
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            input?.Disable();

            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            // Owner: handle local camera pitch and send intent to server.
            if (IsOwner)
            {
                if (HuntersAndCollectors.Input.InputState.GameplayLocked)
                    return;

                HandleLocalPitchOnly();

                // Yaw should be authoritative, so compute yaw delta and send to server.
                float yawDelta = lookInput.x * mouseSensitivity;

                // Send intent to server (unreliable is fine for input streams).
                SendMoveIntentServerRpc(moveInput, sprintHeld, yawDelta);
            }

            // Server: apply movement every frame from the latest intent.
            if (IsServer)
            {
                HandleServerMovement();
                HandleServerRunningXp();
            }
        }

        /// <summary>
        /// Local-only pitch: does NOT affect gameplay, only cameraPivot.
        /// </summary>
        private void HandleLocalPitchOnly()
        {
            if (cameraPivot == null)
                return;

            pitch -= lookInput.y * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>
        /// Client -> Server: movement intent.
        /// We send:
        /// - move input (strafe/forward)
        /// - sprint held
        /// - yaw delta (mouse x)
        ///
        /// Server will:
        /// - rotate the player using yaw delta
        /// - compute speed using Running skill
        /// - move the CharacterController
        /// </summary>
        [ServerRpc]
        private void SendMoveIntentServerRpc(Vector2 move, bool sprint, float yawDelta)
        {
            // Cache the most recent intent for server movement step.
            serverMoveInput = Vector2.ClampMagnitude(move, 1f);
            serverSprintHeld = sprint;

            // Accumulate yaw delta. (If multiple packets arrive per frame, we don't lose rotation.)
            serverYawDelta += yawDelta;
        }

        private void HandleServerMovement()
        {
            // 1) Apply yaw rotation (authoritative)
            if (Mathf.Abs(serverYawDelta) > 0.0001f)
            {
                transform.Rotate(Vector3.up * serverYawDelta);
                serverYawDelta = 0f;
            }

            // 2) Build move direction in world space
            Vector3 move =
                transform.right * serverMoveInput.x +
                transform.forward * serverMoveInput.y;

            if (move.sqrMagnitude > 1f)
                move.Normalize();

            // 3) Compute speed (server-authoritative)
            float speed = walkSpeed;

            bool isTryingToMove = serverMoveInput.magnitude >= minMoveInputToCount;
            bool isSprinting = serverSprintHeld && isTryingToMove;

            if (isSprinting && skillsNet != null)
            {
                int runningLevel = skillsNet.Get(SkillId.Running).Level;
                speed = RunningSkillTuning.GetRunSpeed(walkSpeed, maxMoveSpeed, runningLevel);
            }

            // 4) Move the CharacterController on the server
            controller.Move(move * speed * Time.deltaTime);
        }

        private void HandleServerRunningXp()
        {
            if (skillsNet == null)
                return;

            // Only award XP while sprinting AND actually trying to move.
            bool isTryingToMove = serverMoveInput.magnitude >= minMoveInputToCount;
            bool isRunning = serverSprintHeld && isTryingToMove;

            if (!isRunning)
            {
                serverRunningXpTimer = 0f;
                return;
            }

            serverRunningXpTimer += Time.deltaTime;

            if (serverRunningXpTimer >= runningXpTickSeconds)
            {
                serverRunningXpTimer -= runningXpTickSeconds;

                // Server-authoritative XP gain
                skillsNet.AddXp(SkillId.Running, runningXpPerTick);
            }
        }
    }
}
