using HuntersAndCollectors.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.CameraSystem
{
    /// <summary>
    /// ThirdPersonOrbitCamera
    /// --------------------------------------------------------------------
    /// Local-only presentation camera inspired by Valheim-style orbit control.
    ///
    /// Important authority rule:
    /// - This script never sends anything over the network.
    /// - It never changes gameplay state.
    /// - It only moves the local Unity Camera for presentation.
    ///
    /// High-level behaviour:
    /// 1. Build a pivot point above the followed target.
    /// 2. Rotate around that pivot using yaw + pitch.
    /// 3. Move backward by the desired zoom distance.
    /// 4. Spherecast toward that position so walls push the camera closer.
    /// 5. Smoothly move the actual camera to the collision-adjusted result.
    /// 6. Always look back at the pivot so the player stays framed.
    ///
    /// Attach this to the one persistent runtime camera in the scene.
    /// A separate binder assigns the local owning player as the follow target.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonOrbitCamera : MonoBehaviour
    {
        [Header("Follow")]
        [Tooltip("The transform the camera orbits around. This is assigned locally at runtime for the owning player.")]
        [SerializeField] private Transform followTarget;

        [Tooltip("Vertical offset added to the target position so the camera looks slightly above the player's feet.")]
        [SerializeField] private float lookHeightOffset = 1.6f;

        [Tooltip("How long the pivot point takes to smooth toward the moving player.")]
        [Min(0.01f)]
        [SerializeField] private float followSmoothing = 0.08f;

        [Header("Orbit")]
        [Tooltip("Horizontal orbit sensitivity. Higher values rotate faster when moving the mouse left/right.")]
        [SerializeField] private float yawSpeed = 2.5f;

        [Tooltip("Vertical orbit sensitivity. Higher values rotate faster when moving the mouse up/down.")]
        [SerializeField] private float pitchSpeed = 2.0f;

        [Tooltip("Lowest allowed pitch angle. More negative values look closer to horizontal.")]
        [SerializeField] private float minPitch = 15f;

        [Tooltip("Highest allowed pitch angle. Higher values look more downward.")]
        [SerializeField] private float maxPitch = 65f;

        [Tooltip("Initial downward viewing angle when a target is first assigned.")]
        [SerializeField] private float defaultPitch = 40f;

        [Tooltip("How long yaw/pitch smoothing takes. Smaller values feel snappier.")]
        [Min(0.01f)]
        [SerializeField] private float rotationSmoothing = 0.05f;

        [Header("Zoom")]
        [Tooltip("Starting orbit distance when a target is first assigned.")]
        [SerializeField] private float defaultDistance = 7f;

        [Tooltip("Closest the camera may zoom toward the player.")]
        [SerializeField] private float minZoom = 3f;

        [Tooltip("Farthest the camera may zoom away from the player.")]
        [SerializeField] private float maxZoom = 9f;

        [Tooltip("How strongly the mouse wheel changes zoom. Mouse wheel deltas are normalized internally.")]
        [SerializeField] private float zoomSpeed = 1f;

        [Tooltip("How long zoom smoothing takes. Smaller values respond faster.")]
        [Min(0.01f)]
        [SerializeField] private float zoomSmoothing = 0.06f;

        [Header("Collision")]
        [Tooltip("Radius used for the camera spherecast. A slightly wider radius helps avoid clipping through walls.")]
        [Min(0.01f)]
        [SerializeField] private float collisionRadius = 0.2f;

        [Tooltip("How far to stay in front of the obstacle after a collision hit is found.")]
        [Min(0f)]
        [SerializeField] private float collisionBuffer = 0.1f;

        [Tooltip("Layers that should block the camera, such as terrain, buildings, and world geometry.")]
        [SerializeField] private LayerMask collisionLayers = ~0;

        [Header("Debug")]
        [SerializeField] private bool drawDebugGizmos;

        private const float ScrollNormalization = 0.01f;
        private const float MinimumCastDistance = 0.05f;

        private PlayerInputActions input;
        private Vector2 lookInput;

        private float desiredYaw;
        private float desiredPitch;
        private float currentYaw;
        private float currentPitch;
        private float desiredDistance;
        private float currentDistance;

        private float yawVelocity;
        private float pitchVelocity;
        private float distanceVelocity;

        private Vector3 smoothedPivotPosition;
        private Vector3 pivotVelocity;
        private Vector3 cameraVelocity;

        private bool hasInitializedFromTarget;

        /// <summary>
        /// Public read-only access in case another local-only presentation system needs it.
        /// </summary>
        public Transform FollowTarget => followTarget;

        private void Awake()
        {
            // Reuse the same generated input wrapper already used by movement/combat.
            input = new PlayerInputActions();

            // We only need look input from the wrapper.
            input.Player.Look.performed += OnLookPerformed;
            input.Player.Look.canceled += OnLookCanceled;
        }

        private void OnEnable()
        {
            input?.Enable();
        }

        private void OnDisable()
        {
            if (input != null)
                input.Disable();

            lookInput = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (input != null)
            {
                input.Player.Look.performed -= OnLookPerformed;
                input.Player.Look.canceled -= OnLookCanceled;
                input.Dispose();
                input = null;
            }
        }

        /// <summary>
        /// Assigns the camera target locally.
        /// This should only be called by local presentation code, never by gameplay authority.
        /// </summary>
        public void SetFollowTarget(Transform target)
        {
            followTarget = target;

            if (followTarget == null)
            {
                hasInitializedFromTarget = false;
                return;
            }

            // When a new local player becomes the target we align the camera to that player
            // immediately so the first frame feels stable instead of drifting in from old data.
            Vector3 pivot = GetDesiredPivotPosition();
            smoothedPivotPosition = pivot;

            desiredYaw = followTarget.eulerAngles.y;
            currentYaw = desiredYaw;

            desiredPitch = Mathf.Clamp(defaultPitch, minPitch, maxPitch);
            currentPitch = desiredPitch;

            desiredDistance = Mathf.Clamp(defaultDistance, minZoom, maxZoom);
            currentDistance = desiredDistance;

            transform.position = ComputeCollisionAdjustedPosition(smoothedPivotPosition, currentYaw, currentPitch, currentDistance);
            transform.LookAt(smoothedPivotPosition);

            yawVelocity = 0f;
            pitchVelocity = 0f;
            distanceVelocity = 0f;
            pivotVelocity = Vector3.zero;
            cameraVelocity = Vector3.zero;
            hasInitializedFromTarget = true;
        }

        private void LateUpdate()
        {
            // No local target yet: safely do nothing.
            if (followTarget == null)
                return;

            if (!hasInitializedFromTarget)
                SetFollowTarget(followTarget);

            HandleOrbitInput();
            HandleZoomInput();

            // Smooth the pivot so the camera gently follows player motion.
            Vector3 desiredPivot = GetDesiredPivotPosition();
            smoothedPivotPosition = Vector3.SmoothDamp(
                smoothedPivotPosition,
                desiredPivot,
                ref pivotVelocity,
                followSmoothing);

            // Smooth yaw/pitch so orbit movement is responsive without feeling jittery.
            currentYaw = Mathf.SmoothDampAngle(currentYaw, desiredYaw, ref yawVelocity, rotationSmoothing);
            currentPitch = Mathf.SmoothDampAngle(currentPitch, desiredPitch, ref pitchVelocity, rotationSmoothing);
            currentDistance = Mathf.SmoothDamp(currentDistance, desiredDistance, ref distanceVelocity, zoomSmoothing);

            // Compute the camera's ideal orbit position, then push it inward if blocked by geometry.
            Vector3 collisionAdjustedPosition = ComputeCollisionAdjustedPosition(smoothedPivotPosition, currentYaw, currentPitch, currentDistance);

            // Smooth the final camera position so follow/collision changes feel polished.
            transform.position = Vector3.SmoothDamp(
                transform.position,
                collisionAdjustedPosition,
                ref cameraVelocity,
                followSmoothing);

            // Always look back at the pivot so the player stays centered in view.
            transform.LookAt(smoothedPivotPosition);
        }

        private void HandleOrbitInput()
        {
            if (InputState.GameplayLocked)
                return;

            // Mouse delta is already frame-based, so we intentionally do not multiply by deltaTime.
            desiredYaw += lookInput.x * yawSpeed;
            desiredPitch -= lookInput.y * pitchSpeed;
            desiredPitch = Mathf.Clamp(desiredPitch, minPitch, maxPitch);
        }

        private void HandleZoomInput()
        {
            if (InputState.GameplayLocked || Mouse.current == null)
                return;

            float scrollY = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) <= Mathf.Epsilon)
                return;

            // Input System mouse wheel values are fairly large on desktop, so we normalize them.
            float zoomDelta = scrollY * ScrollNormalization * zoomSpeed;
            desiredDistance = Mathf.Clamp(desiredDistance - zoomDelta, minZoom, maxZoom);
        }

        private Vector3 GetDesiredPivotPosition()
        {
            if (followTarget == null)
                return transform.position;

            return followTarget.position + (Vector3.up * lookHeightOffset);
        }

        private Vector3 ComputeCollisionAdjustedPosition(Vector3 pivot, float yaw, float pitch, float distance)
        {
            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 orbitForward = orbitRotation * Vector3.forward;
            Vector3 desiredPosition = pivot - (orbitForward * distance);

            Vector3 castDirection = desiredPosition - pivot;
            float castDistance = castDirection.magnitude;

            if (castDistance <= MinimumCastDistance)
                return desiredPosition;

            Vector3 castDirectionNormalized = castDirection / castDistance;

            if (Physics.SphereCast(
                    pivot,
                    collisionRadius,
                    castDirectionNormalized,
                    out RaycastHit hit,
                    castDistance,
                    collisionLayers,
                    QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(MinimumCastDistance, hit.distance - collisionBuffer);
                return pivot + (castDirectionNormalized * safeDistance);
            }

            return desiredPosition;
        }

        private void OnLookPerformed(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            lookInput = context.ReadValue<Vector2>();
        }

        private void OnLookCanceled(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            lookInput = Vector2.zero;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxPitch = Mathf.Max(minPitch, maxPitch);
            defaultPitch = Mathf.Clamp(defaultPitch, minPitch, maxPitch);
            minZoom = Mathf.Max(0.5f, minZoom);
            maxZoom = Mathf.Max(minZoom, maxZoom);
            defaultDistance = Mathf.Clamp(defaultDistance, minZoom, maxZoom);
            followSmoothing = Mathf.Max(0.01f, followSmoothing);
            rotationSmoothing = Mathf.Max(0.01f, rotationSmoothing);
            zoomSmoothing = Mathf.Max(0.01f, zoomSmoothing);
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            collisionBuffer = Mathf.Max(0f, collisionBuffer);
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawDebugGizmos)
                return;

            Vector3 pivot = followTarget != null ? GetDesiredPivotPosition() : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(pivot, 0.1f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, collisionRadius);
            Gizmos.DrawLine(pivot, transform.position);
        }
#endif
    }
}
