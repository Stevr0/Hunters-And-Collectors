using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Simple movement for testing world interaction.
    /// Only the owning client can move their player.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 6f;

        private CharacterController controller;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            // Only allow local player to control this object
            if (!IsOwner)
                enabled = false;
        }

        private void Update()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");

            Vector3 move = new Vector3(h, 0, v);

            if (move.sqrMagnitude > 1f)
                move.Normalize();

            controller.Move(move * moveSpeed * Time.deltaTime);
        }
    }
}
