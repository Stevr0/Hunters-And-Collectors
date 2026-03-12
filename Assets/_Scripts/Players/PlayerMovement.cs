using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// PlayerMovement (Server Authoritative)
    /// -------------------------------------------------------
    /// Adds Animator driving for basic locomotion.
    ///
    /// Animator approach (important for networking):
    /// - We DO NOT drive animations purely from input, because non-owners don't have input.
    /// - Instead, we derive movement from transform delta (position change) which is replicated to all clients.
    /// - This makes animations look correct for everyone with no extra network variables.
    ///
    /// Animator parameters expected:
    /// - Float: MoveX   (local strafe, -1..+1)
    /// - Float: MoveY   (local forward, -1..+1)
    /// - Bool:  IsMoving
    /// - Bool:  IsGrounded
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

        [Header("Server Movement Physics")]
        [Tooltip("Downward acceleration applied by the server while airborne.")]
        [SerializeField] private float gravity = -30f;

        [Tooltip("Small downward velocity while grounded to keep CharacterController snapped to slopes/steps.")]
        [SerializeField] private float groundedStickVelocity = 2f;

        [Tooltip("Maximum falling speed in meters per second.")]
        [SerializeField] private float maxFallSpeed = 60f;

        [Header("Running XP (Server)")]
        [Tooltip("Seconds between running XP ticks while sprinting & moving.")]
        [SerializeField] private float runningXpTickSeconds = 1.0f;

        [Tooltip("XP granted per tick while sprinting & moving.")]
        [SerializeField] private int runningXpPerTick = 1;

        [Tooltip("Minimum input magnitude to count as 'moving' for running XP.")]
        [SerializeField] private float minMoveInputToCount = 0.1f;

        [Header("Stamina (Server)")]
        [Tooltip("Stamina spent per second while sprinting and moving. Consumed on the server only.")]
        [SerializeField] private float sprintStaminaCostPerSecond = 8f;

        [Header("Animator (Optional)")]
        [Tooltip("Animator on the character (humanoid). If left null, we auto-find in children.")]
        [SerializeField] private Animator animator;

        [Tooltip("How quickly the blend tree values smooth toward the target.")]
        [SerializeField] private float animDampTime = 0.10f;

        [Tooltip("Minimum speed (m/s) before we consider the character moving for IsMoving.")]
        [SerializeField] private float minSpeedToAnimate = 0.05f;

        private CharacterController controller;
        private SkillsNet skillsNet;
        private PlayerVitalsNet playerVitals;
        private PlayerCarryNet playerCarry;

        // --- Client-side input state (owner only) ---
        private PlayerInputActions input;
        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool sprintHeld;
        private float pitch;
        private bool sentLockedStop;

        // --- Server-side cached intent (authoritative movement uses these) ---
        private Vector2 serverMoveInput;
        private bool serverSprintHeld;
        private float serverYawDelta;
        private float serverVerticalVelocity;

        // --- Server-side XP ticking ---
        private float serverRunningXpTimer;
        private float serverSprintStaminaSpendAccumulator;

        // --- Animator driving state (all instances) ---
        private Vector3 lastPosition;

        // Cache animator parameter hashes for performance (avoids string lookups every frame).
        private static readonly int AnimMoveX = Animator.StringToHash("MoveX");
        private static readonly int AnimMoveY = Animator.StringToHash("MoveY");
        private static readonly int AnimIsMoving = Animator.StringToHash("IsMoving");
        private static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int AnimSpeed01 = Animator.StringToHash("Speed01");
        private static readonly int AnimGather = Animator.StringToHash("Gather");

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            skillsNet = GetComponent<SkillsNet>();
            playerVitals = GetComponent<PlayerVitalsNet>();
            playerCarry = GetComponent<PlayerCarryNet>();

            // If user didn't assign animator, try to find it on children.
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            // Owner input wrapper
            input = new PlayerInputActions();

            // Initialize last position for animation-derived velocity
            lastPosition = transform.position;
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

            // Reset animation velocity tracking when the object spawns on a client
            lastPosition = transform.position;

            if (IsServer)
            {
                playerCarry = GetComponent<PlayerCarryNet>();
                serverVerticalVelocity = -Mathf.Abs(groundedStickVelocity);
            }
        }

        private void OnDisable()
        {
            input?.Disable();
            moveInput = Vector2.zero;
            lookInput = Vector2.zero;
            sprintHeld = false;
            sentLockedStop = false;

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
                bool gameplayLocked = HuntersAndCollectors.Input.InputState.GameplayLocked;
                if (gameplayLocked)
                {
                    // While UI is open, push neutral intent so the server does not keep stale movement.
                    if (!sentLockedStop || moveInput != Vector2.zero || sprintHeld || lookInput != Vector2.zero)
                    {
                        moveInput = Vector2.zero;
                        lookInput = Vector2.zero;
                        sprintHeld = false;
                        SendMoveIntentServerRpc(Vector2.zero, false, 0f);
                        sentLockedStop = true;
                    }

                    // Host path: zero server intent immediately this frame.
                    if (IsServer)
                    {
                        serverMoveInput = Vector2.zero;
                        serverSprintHeld = false;
                        serverYawDelta = 0f;
                    }
                }
                else
                {
                    sentLockedStop = false;

                    HandleLocalPitchOnly();

                    // Yaw should be authoritative, so compute yaw delta and send to server.
                    float yawDelta = lookInput.x * mouseSensitivity;

                    // Send intent to server (unreliable is fine for input streams).
                    SendMoveIntentServerRpc(moveInput, sprintHeld, yawDelta);
                }
            }

            // Server: apply movement every frame from the latest intent.
            if (IsServer)
            {
                HandleServerMovement();
                HandleServerRunningXp();
            }

            // Everyone: update animator from actual replicated motion.
            // This keeps animations correct for:
            // - The owner (local player)
            // - Other clients watching the player
            // - The host/server instance
            UpdateAnimatorFromMotion();
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
            if (controller == null || !controller.enabled)
                return;
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
            bool wantsSprint = serverSprintHeld && isTryingToMove;
            float encumbranceMultiplier = playerCarry != null ? Mathf.Clamp(playerCarry.CurrentMovementMultiplier, 0f, 1f) : 1f;
            bool canMoveFromEncumbrance = encumbranceMultiplier > 0f;
            bool canSprint = canMoveFromEncumbrance && (!wantsSprint || playerVitals == null || playerVitals.CurrentStamina > 0);
            bool isSprinting = wantsSprint && canSprint;

            if (isSprinting && skillsNet != null)
            {
                int runningLevel = skillsNet.Get(SkillId.Running).Level;
                speed = RunningSkillTuning.GetRunSpeed(walkSpeed, maxMoveSpeed, runningLevel);
            }

            speed *= encumbranceMultiplier;

            // 4) Apply server-side vertical physics so we stay grounded on height transitions.
            bool wasGrounded = controller.isGrounded;

            if (wasGrounded)
            {
                // Keep a slight downward pull so CharacterController remains snapped to the ground.
                serverVerticalVelocity = -Mathf.Abs(groundedStickVelocity);
            }
            else
            {
                serverVerticalVelocity += gravity * Time.deltaTime;
                serverVerticalVelocity = Mathf.Max(serverVerticalVelocity, -Mathf.Abs(maxFallSpeed));
            }

            // 5) Move with combined horizontal + vertical velocity.
            Vector3 velocity = move * speed;
            velocity.y = serverVerticalVelocity;
            CollisionFlags flags = controller.Move(velocity * Time.deltaTime);

            // If we hit ground while moving downward, keep the sticky grounded pull.
            bool hitGround = (flags & CollisionFlags.Below) != 0;
            if (hitGround && serverVerticalVelocity < 0f)
            {
                serverVerticalVelocity = -Mathf.Abs(groundedStickVelocity);
            }

            // 6) Server-authoritative sprint stamina drain. If stamina runs out, next frame will force walk.
            if (wantsSprint && speed > 0f && playerVitals != null && sprintStaminaCostPerSecond > 0f)
            {
                serverSprintStaminaSpendAccumulator += sprintStaminaCostPerSecond * Time.deltaTime;
                while (serverSprintStaminaSpendAccumulator >= 1f)
                {
                    serverSprintStaminaSpendAccumulator -= 1f;
                    if (!playerVitals.ServerSpendStamina(1))
                    {
                        // No stamina left to spend; stop carrying fractional debt.
                        serverSprintStaminaSpendAccumulator = 0f;
                        break;
                    }
                }
            }
            else
            {
                serverSprintStaminaSpendAccumulator = 0f;
            }
        }

        private void HandleServerRunningXp()
        {
            if (skillsNet == null)
                return;

            // Only award XP while sprinting, moving, and having enough stamina to actually sprint.
            bool isTryingToMove = serverMoveInput.magnitude >= minMoveInputToCount;
            bool wantsSprint = serverSprintHeld && isTryingToMove;
            bool canMoveFromEncumbrance = playerCarry == null || playerCarry.CurrentMovementMultiplier > 0f;
            bool canSprint = canMoveFromEncumbrance && (!wantsSprint || playerVitals == null || playerVitals.CurrentStamina > 0);
            bool isRunning = wantsSprint && canSprint;

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

        /// <summary>
        /// Updates animator parameters based on REAL movement (transform delta).
        /// Why this method is great for NGO:
        /// - The server moves the object.
        /// - NetworkTransform replicates position/rotation to clients.
        /// - Clients can compute "how fast am I moving" from position change.
        /// - So animation works for everyone without syncing extra variables.
        /// </summary>
        private void UpdateAnimatorFromMotion()
        {
            if (animator == null)
                return;

            // Compute world-space velocity from position delta.
            // (This works even on non-owners, because their transform is updated by replication.)
            Vector3 currentPos = transform.position;
            Vector3 worldVel = (currentPos - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastPosition = currentPos;

            // We only want horizontal motion for locomotion blending.
            worldVel.y = 0f;

            // Convert world velocity into LOCAL space so:
            // - local.x = strafe left/right
            // - local.z = move forward/back
            Vector3 localVel = transform.InverseTransformDirection(worldVel);

            // Convert to a -1..+1-ish range for blend tree parameters.
            // We divide by maxMoveSpeed so:
            // - walking becomes smaller values
            // - sprinting approaches 1.0 at max speed
            float norm = Mathf.Max(maxMoveSpeed, 0.01f);
            float moveX = Mathf.Clamp(localVel.x / norm, -1f, 1f);
            float moveY = Mathf.Clamp(localVel.z / norm, -1f, 1f);

            // Consider moving if our planar speed is above a tiny threshold.
            bool isMoving = worldVel.magnitude > minSpeedToAnimate;

            // Planar speed (ignore vertical) in meters/second.
            float planarSpeed = worldVel.magnitude;

            // Convert speed into a 0..1 value relative to your maxMoveSpeed.
            // This makes sprint naturally happen when speed approaches max.
            float speed01 = Mathf.Clamp01(planarSpeed / Mathf.Max(maxMoveSpeed, 0.01f));

            // Smoothly push Speed01 into the Animator.
            // (If you want it snappier, reduce animDampTime.)
            animator.SetFloat(AnimSpeed01, speed01, animDampTime, Time.deltaTime);

            // Grounded:
            // - On the server this is correct (controller is actually moving).
            // - On clients it can still often be okay, but if it's flaky we can replace it later
            //   with a small raycast-based ground check.
            bool isGrounded = controller != null && controller.isGrounded;

            // Set animator parameters with damping for smooth blends.
            animator.SetFloat(AnimMoveX, moveX, animDampTime, Time.deltaTime);
            animator.SetFloat(AnimMoveY, moveY, animDampTime, Time.deltaTime);
            animator.SetBool(AnimIsMoving, isMoving);
            animator.SetBool(AnimIsGrounded, isGrounded);
        }

        /// <summary>
        /// Plays the one-shot gathering animation locally.
        /// Safe to call on any client. Does nothing if animator is missing.
        /// </summary>
        public void PlayGatherAnimationLocal()
        {
            if (animator == null)
                return;

            animator.SetTrigger(AnimGather);
        }

        /// <summary>
        /// Server tells all clients to play the gather animation for this player.
        /// This ensures remote players SEE the pickup animation too.
        /// </summary>
        [ClientRpc]
        public void PlayGatherClientRpc()
        {
            PlayGatherAnimationLocal();
        }
    }
}
