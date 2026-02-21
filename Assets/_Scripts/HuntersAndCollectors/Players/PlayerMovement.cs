using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Players
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;

        private CharacterController controller;
        private PlayerInputActions input;
        private Vector2 moveInput;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = new PlayerInputActions();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            input.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
            input.Player.Move.canceled += _ => moveInput = Vector2.zero;

            input.Enable();
        }

        private void OnDisable()
        {
            input?.Disable();
        }

        private void Update()
        {
            Vector3 move = new Vector3(moveInput.x, 0, moveInput.y);

            if (move.sqrMagnitude > 1f)
                move.Normalize();

            controller.Move(move * moveSpeed * Time.deltaTime);
        }
    }
}
