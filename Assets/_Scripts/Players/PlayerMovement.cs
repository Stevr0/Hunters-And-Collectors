using HuntersAndCollectors.Input;
using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// PlayerMovement (Server Authoritative)
    /// -------------------------------------------------------
    /// Third-person camera-aligned movement built on the project's existing
    /// server-authoritative model.
    ///
    /// Authority model:
    /// - The owning client reads local input.
    /// - The owning client converts 2D input into a WORLD-SPACE movement intent
    ///   using the local camera's facing direction.
    /// - That world-space intent is sent to the server, together with the raw 2D input.
    /// - The server performs the real movement, gravity, rotation, stamina drain,
    ///   and running XP.
    ///
    /// Why this is still server-authoritative:
    /// - The camera is NOT networked.
    /// - The camera does NOT directly move the player.
    /// - The client only sends desired intent.
    /// - The server remains the only side that moves the CharacterController.
    ///
    /// Backpedal rule:
    /// - Pressing S should move the player backward WITHOUT turning to face that direction.
    /// - To support that, the owner sends raw 2D input as well as world-space move intent.
    /// - The server uses the raw input to decide whether to rotate.
    /// - If raw Y input is negative enough, we treat that as "backpedal" and do not rotate.
    ///
    /// Animator approach (important for networking):
    /// - We DO NOT drive animations purely from local input, because non-owners do not have that input.
    /// - Instead, we derive movement from transform delta (position change) which is replicated to all clients.
    /// - This keeps animation readable for everyone without extra network variables.
    ///
    /// Animator parameters expected:
    /// - Float: MoveX      (local strafe, -1..+1)
    /// - Float: MoveY      (local forward/back, -1..+1)
    /// - Bool:  IsMoving
    /// - Bool:  IsGrounded
    /// - Float: Speed01    (0..1 speed blend)
    /// - Trigger: Gather
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        [Header("Speed Tuning")]
        [Tooltip("Walk speed (no sprint). Example: 2")]
        [SerializeField] private float walkSpeed = 2f;

        [Tooltip("Max possible speed when Running skill is 100. Example: 10")]
        [SerializeField] private float maxMoveSpeed = 10f;

        [Header("Camera Relative Movement")]
        [Tooltip("Optional explicit camera transform used for local movement basis. If left empty, we fall back to Camera.main.")]
        [SerializeField] private Transform cameraFallbackTransform;

        [Tooltip("Ignore tiny stick/noise values smaller than this when turning input into movement intent.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float inputDeadzone = 0.1f;

        [Tooltip("Minimum movement intent magnitude required before the character rotates toward movement.")]
        [Range(0f, 1f)]
        [SerializeField] private float minInputToRotate = 0.1f;

        [Tooltip("How quickly the server rotates the player toward the desired move direction, in degrees per second.")]
        [Min(0f)]
        [SerializeField] private float rotationSpeedDegreesPerSecond = 720f;

        [Header("Server Movement Physics")]
        [Tooltip("Downward acceleration applied by the server while airborne.")]
        [SerializeField] private float gravity = -30f;

        [Tooltip("Small downward velocity while grounded to keep CharacterController snapped to slopes/steps.")]
        [SerializeField] private float groundedStickVelocity = 2f;

        [Tooltip("Maximum falling speed in meters per second.")]
        [SerializeField] private float maxFallSpeed = 60f;

        [Header("Running XP (Server)")]
        [Tooltip("Seconds between running XP ticks while sprinting and moving.")]
        [SerializeField] private float runningXpTickSeconds = 1.0f;

        [Tooltip("XP granted per tick while sprinting and moving.")]
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
        // These values only exist on the owning player's machine.
        private PlayerInputActions input;
        private Vector2 moveInput;
        private bool sprintHeld;
        private bool sentLockedStop;

        // --- Server-side cached intent (authoritative movement uses these) ---
        // serverMoveIntentWorld:
        //     Camera-relative WORLD-SPACE planar movement direction generated on the owner.
        //
        // serverRawMoveInput:
        //     Raw 2D owner input (X/Y). We keep this so the server can distinguish
        //     between "forward-like movement" and "backpedal".
        //
        // Example:
        // - W  => rawInput.y positive   => rotate toward move direction
        // - S  => rawInput.y negative   => DO NOT rotate, backpedal instead
        private Vector3 serverMoveIntentWorld;
        private Vector2 serverRawMoveInput;
        private bool serverSprintHeld;
        private float serverVerticalVelocity;

        // --- Server-side XP / stamina ticking ---
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

            // If the user did not assign an animator, try to find one on children.
            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            // Reuse the generated Input System wrapper already used elsewhere in the project.
            input = new PlayerInputActions();

            // Initialize last position for animation-derived velocity.
            lastPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            // Only the owner reads input.
            if (IsOwner)
            {
                input.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
                input.Player.Move.canceled += _ => moveInput = Vector2.zero;

                input.Player.Sprint.performed += _ => sprintHeld = true;
                input.Player.Sprint.canceled += _ => sprintHeld = false;

                input.Enable();

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // Reset animation velocity tracking when the object spawns on a client.
            lastPosition = transform.position;

            if (IsServer)
            {
                playerCarry = GetComponent<PlayerCarryNet>();

                // Start slightly downward so CharacterController stays snapped to ground/slopes.
                serverVerticalVelocity = -Mathf.Abs(groundedStickVelocity);

                // Clear any stale cached input.
                serverMoveIntentWorld = Vector3.zero;
                serverRawMoveInput = Vector2.zero;
                serverSprintHeld = false;
            }
        }

        private void OnDisable()
        {
            input?.Disable();

            moveInput = Vector2.zero;
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
            // Owner: read local input and convert it into camera-relative world-space intent.
            if (IsOwner)
                HandleOwnerMovementIntent();

            // Server: apply authoritative movement every frame from the latest cached intent.
            if (IsServer)
            {
                HandleServerMovement();
                HandleServerRunningXp();
            }

            // Everyone: drive animator from actual replicated motion.
            UpdateAnimatorFromMotion();
        }

        /// <summary>
        /// Owner-only path that converts local 2D input into camera-relative world-space intent.
        ///
        /// Key idea:
        /// - W/S move along the camera's flattened forward axis.
        /// - A/D move along the camera's flattened right axis.
        /// - Camera pitch is ignored so movement stays planar on the ground.
        ///
        /// We also send raw input to the server so it can distinguish between:
        /// - "move/turn toward direction"
        /// - "move backward without turning"
        /// </summary>
        private void HandleOwnerMovementIntent()
        {
            bool gameplayLocked = InputState.GameplayLocked;
            if (gameplayLocked)
            {
                // While UI is open, push neutral intent so the server does not keep stale movement.
                if (!sentLockedStop || moveInput != Vector2.zero || sprintHeld)
                {
                    moveInput = Vector2.zero;
                    sprintHeld = false;
                    SendMoveIntentServerRpc(Vector3.zero, Vector2.zero, false);
                    sentLockedStop = true;
                }

                // Host path: clear server-side cached intent immediately this frame too.
                if (IsServer)
                {
                    serverMoveIntentWorld = Vector3.zero;
                    serverRawMoveInput = Vector2.zero;
                    serverSprintHeld = false;
                }

                return;
            }

            sentLockedStop = false;

            Vector3 desiredWorldMove = BuildCameraRelativeWorldMove(moveInput);

            // Send both the converted world-space move and the raw 2D input.
            // The server will use the raw input to decide whether to rotate.
            SendMoveIntentServerRpc(desiredWorldMove, moveInput, sprintHeld);
        }

        /// <summary>
        /// Converts 2D local input into a camera-relative WORLD-SPACE movement vector.
        ///
        /// Example:
        /// - W uses the flattened camera forward.
        /// - D uses the flattened camera right.
        /// - Diagonals are clamped so they are not faster than straight movement.
        ///
        /// If we do not have a camera yet, we safely fall back to the player's own forward/right.
        /// </summary>
        private Vector3 BuildCameraRelativeWorldMove(Vector2 rawInput)
        {
            // Apply a small deadzone to ignore tiny device noise.
            Vector2 filteredInput = rawInput.magnitude < inputDeadzone
                ? Vector2.zero
                : Vector2.ClampMagnitude(rawInput, 1f);

            if (filteredInput == Vector2.zero)
                return Vector3.zero;

            Transform movementReference = ResolveMovementReferenceTransform();

            Vector3 cameraForward = movementReference != null ? movementReference.forward : transform.forward;
            Vector3 cameraRight = movementReference != null ? movementReference.right : transform.right;

            // Flatten onto the XZ plane so camera pitch never pushes movement upward/downward.
            cameraForward.y = 0f;
            cameraRight.y = 0f;

            // If either vector becomes too small after flattening, fall back to the player's basis.
            if (cameraForward.sqrMagnitude < 0.0001f)
                cameraForward = transform.forward;

            if (cameraRight.sqrMagnitude < 0.0001f)
                cameraRight = transform.right;

            cameraForward.Normalize();
            cameraRight.Normalize();

            Vector3 move = (cameraForward * filteredInput.y) + (cameraRight * filteredInput.x);

            // Clamp magnitude so diagonal movement is not faster than straight movement.
            if (move.sqrMagnitude > 1f)
                move.Normalize();

            return move;
        }

        /// <summary>
        /// Resolves the transform used as the local movement basis.
        ///
        /// Priority:
        /// 1. Explicit inspector-assigned fallback transform.
        /// 2. Camera.main if one exists.
        /// 3. Null, which causes the caller to fall back to the player transform.
        /// </summary>
        private Transform ResolveMovementReferenceTransform()
        {
            if (cameraFallbackTransform != null)
                return cameraFallbackTransform;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                return mainCamera.transform;

            return null;
        }

        /// <summary>
        /// Client -> Server: movement intent.
        ///
        /// moveWorld:
        /// - Camera-relative WORLD-SPACE planar move vector.
        ///
        /// rawInput:
        /// - Owner's 2D move input. Needed so the server can detect backpedal.
        ///
        /// sprint:
        /// - Whether sprint is being held by the owner.
        /// </summary>
        [ServerRpc]
        private void SendMoveIntentServerRpc(Vector3 moveWorld, Vector2 rawInput, bool sprint)
        {
            // We only want planar movement.
            moveWorld.y = 0f;

            // Clamp intent so malformed input can never exceed magnitude 1.
            if (moveWorld.sqrMagnitude > 1f)
                moveWorld.Normalize();

            // Clamp raw input too, so the server never stores oversized values.
            if (rawInput.sqrMagnitude > 1f)
                rawInput = rawInput.normalized;

            serverMoveIntentWorld = moveWorld;
            serverRawMoveInput = rawInput;
            serverSprintHeld = sprint;
        }

        /// <summary>
        /// Server-authoritative movement step.
        ///
        /// Main flow:
        /// 1. Read the last cached owner intent.
        /// 2. Decide whether we should rotate.
        /// 3. Compute movement speed from walking/running/encumbrance.
        /// 4. Apply gravity.
        /// 5. Move the CharacterController.
        /// 6. Spend stamina for sprinting.
        ///
        /// Important backpedal rule:
        /// - If the player is pressing backward (raw Y negative enough),
        ///   we keep the current facing and move backward instead of turning.
        /// </summary>
        private void HandleServerMovement()
        {
            if (controller == null || !controller.enabled)
                return;

            // 1) Build a safe planar movement direction from the cached client intent.
            Vector3 move = serverMoveIntentWorld;
            move.y = 0f;

            float inputMagnitude = Mathf.Clamp01(move.magnitude);

            Vector3 moveDirection = inputMagnitude > 0.0001f
                ? (move / Mathf.Max(inputMagnitude, 0.0001f))
                : Vector3.zero;

            // 2) Decide whether the character should rotate toward movement direction.
            //
            // Backpedal rule:
            // - If raw Y input is negative enough, we treat that as "walk backward".
            // - In that case we do NOT rotate toward the movement direction.
            //
            // This means:
            // - W     => rotate and move forward
            // - S     => move backward, keep current facing
            // - S+A/D => still treated as backpedal for this first pass
            float rawY = serverRawMoveInput.y;

            bool hasMeaningfulMoveIntent =
                inputMagnitude >= minInputToRotate &&
                moveDirection.sqrMagnitude > 0.0001f;

            bool isPressingBackward = rawY < -inputDeadzone;

            if (hasMeaningfulMoveIntent && !isPressingBackward)
            {
                Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    rotationSpeedDegreesPerSecond * Time.deltaTime);
            }

            // 3) Compute speed (server-authoritative).
            float speed = walkSpeed;

            bool isTryingToMove = inputMagnitude >= minMoveInputToCount;
            bool wantsSprint = serverSprintHeld && isTryingToMove;

            float encumbranceMultiplier = playerCarry != null
                ? Mathf.Clamp(playerCarry.CurrentMovementMultiplier, 0f, 1f)
                : 1f;

            bool canMoveFromEncumbrance = encumbranceMultiplier > 0f;

            bool canSprint = canMoveFromEncumbrance &&
                             (!wantsSprint || playerVitals == null || playerVitals.CurrentStamina > 0);

            bool isSprinting = wantsSprint && canSprint;

            if (isSprinting && skillsNet != null)
            {
                int runningLevel = skillsNet.Get(SkillId.Running).Level;
                speed = RunningSkillTuning.GetRunSpeed(walkSpeed, maxMoveSpeed, runningLevel);
            }

            speed *= encumbranceMultiplier;

            // Scale final movement by input magnitude so analog devices keep proportional movement.
            float planarSpeed = speed * inputMagnitude;

            // 4) Apply server-side vertical physics so we stay grounded on height transitions.
            bool wasGrounded = controller.isGrounded;

            if (wasGrounded)
            {
                // Keep a small downward stick so the controller hugs slopes and steps.
                serverVerticalVelocity = -Mathf.Abs(groundedStickVelocity);
            }
            else
            {
                serverVerticalVelocity += gravity * Time.deltaTime;
                serverVerticalVelocity = Mathf.Max(serverVerticalVelocity, -Mathf.Abs(maxFallSpeed));
            }

            // 5) Move with combined horizontal + vertical velocity.
            Vector3 velocity = moveDirection * planarSpeed;
            velocity.y = serverVerticalVelocity;

            CollisionFlags flags = controller.Move(velocity * Time.deltaTime);

            bool hitGround = (flags & CollisionFlags.Below) != 0;
            if (hitGround && serverVerticalVelocity < 0f)
                serverVerticalVelocity = -Mathf.Abs(groundedStickVelocity);

            // 6) Server-authoritative sprint stamina drain.
            // If stamina runs out, next frame the player naturally falls back to walk.
            if (wantsSprint && planarSpeed > 0f && playerVitals != null && sprintStaminaCostPerSecond > 0f)
            {
                serverSprintStaminaSpendAccumulator += sprintStaminaCostPerSecond * Time.deltaTime;

                while (serverSprintStaminaSpendAccumulator >= 1f)
                {
                    serverSprintStaminaSpendAccumulator -= 1f;

                    if (!playerVitals.ServerSpendStamina(1))
                    {
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

        /// <summary>
        /// Server-side Running skill XP.
        ///
        /// XP is only granted while:
        /// - moving meaningfully
        /// - sprint is held
        /// - sprint is actually allowed
        /// </summary>
        private void HandleServerRunningXp()
        {
            if (skillsNet == null)
                return;

            float inputMagnitude = Mathf.Clamp01(serverMoveIntentWorld.magnitude);

            bool isTryingToMove = inputMagnitude >= minMoveInputToCount;
            bool wantsSprint = serverSprintHeld && isTryingToMove;

            bool canMoveFromEncumbrance = playerCarry == null || playerCarry.CurrentMovementMultiplier > 0f;
            bool canSprint = canMoveFromEncumbrance &&
                             (!wantsSprint || playerVitals == null || playerVitals.CurrentStamina > 0);

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
                skillsNet.AddXp(SkillId.Running, runningXpPerTick);
            }
        }

        /// <summary>
        /// Updates animator parameters based on REAL movement (transform delta).
        ///
        /// Why this method is good for NGO:
        /// - The server moves the object.
        /// - NetworkTransform replicates position/rotation to clients.
        /// - Clients can compute actual motion from transform delta.
        /// - So animation works for everyone without syncing extra variables.
        ///
        /// Important for backpedal:
        /// - If the character keeps facing forward and moves backward,
        ///   localVel.z becomes negative.
        /// - That lets the animator play backward-walk motion naturally.
        /// </summary>
        private void UpdateAnimatorFromMotion()
        {
            if (animator == null)
                return;

            Vector3 currentPos = transform.position;
            Vector3 worldVel = (currentPos - lastPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastPosition = currentPos;

            // Ignore vertical movement for locomotion blend.
            worldVel.y = 0f;

            // Convert world velocity into the player's current local space.
            // This lets the blend tree know whether we are moving forward, backward, or strafing
            // relative to where the character is facing.
            Vector3 localVel = transform.InverseTransformDirection(worldVel);

            float norm = Mathf.Max(maxMoveSpeed, 0.01f);
            float moveX = Mathf.Clamp(localVel.x / norm, -1f, 1f);
            float moveY = Mathf.Clamp(localVel.z / norm, -1f, 1f);

            bool isMoving = worldVel.magnitude > minSpeedToAnimate;
            float planarSpeed = worldVel.magnitude;
            float speed01 = Mathf.Clamp01(planarSpeed / Mathf.Max(maxMoveSpeed, 0.01f));

            animator.SetFloat(AnimSpeed01, speed01, animDampTime, Time.deltaTime);

            bool isGrounded = controller != null && controller.isGrounded;

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
        /// This ensures remote players see the pickup animation too.
        /// </summary>
        [ClientRpc]
        public void PlayGatherClientRpc()
        {
            PlayGatherAnimationLocal();
        }
    }
}