using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
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

            vendorUI = FindObjectOfType<VendorWindowUI>(true); // true finds inactive too
            input = new PlayerInputActions();

            input.Player.Interact.performed += _ => TryInteract();
            input.Enable();
        }

        private void OnDisable()
        {
            input?.Disable();
        }

        private void TryInteract()
        {
            if (vendorUI == null)
            {
                Debug.LogWarning("[PlayerInteract] No VendorWindowUI found in scene.");
                return;
            }

            // Super simple MVP: find any vendor proximity we're inside.
            // Later we can optimize this to nearest vendor only.
            var proximities = FindObjectsOfType<VendorProximity>();

            foreach (var p in proximities)
            {
                if (!p.IsPlayerInRange)
                    continue;

                if (p.Vendor == null)
                {
                    Debug.LogWarning("[PlayerInteract] VendorProximity has no VendorInteractable assigned.");
                    continue;
                }

                // Bind the UI to THIS vendor.
                vendorUI.Open(p.Vendor);
                return;
            }

            Debug.Log("[PlayerInteract] No vendor in range.");
        }
    }
}
