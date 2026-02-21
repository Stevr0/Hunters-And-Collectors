using HuntersAndCollectors.Vendors;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Players
{
    public sealed class PlayerInteract : NetworkBehaviour
    {
        private PlayerInputActions input;
        private VendorWindowUI vendorUI;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            input = new PlayerInputActions();

            input.Player.Interact.performed += _ => TryInteract();

            input.Enable();

            vendorUI = FindObjectOfType<VendorWindowUI>();
        }

        private void OnDisable()
        {
            input?.Disable();
        }

        private void TryInteract()
        {
            var vendors = FindObjectsOfType<VendorProximity>();

            foreach (var vendor in vendors)
            {
                if (vendor.IsPlayerInRange)
                {
                    vendorUI?.Open();
                    break;
                }
            }
        }
    }
}
