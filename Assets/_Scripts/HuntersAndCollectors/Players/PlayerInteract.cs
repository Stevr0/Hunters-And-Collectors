using HuntersAndCollectors.Vendors;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Handles interaction input for the local player.
    /// </summary>
    public sealed class PlayerInteract : NetworkBehaviour
    {
        [SerializeField] private float interactRange = 3f;

        private VendorProximity currentVendor;
        private VendorWindowUI vendorUI;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
                enabled = false;

            vendorUI = FindObjectOfType<VendorWindowUI>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                TryInteract();
            }
        }

        private void TryInteract()
        {
            var vendors = FindObjectsOfType<VendorProximity>();

            foreach (var vendor in vendors)
            {
                if (vendor.IsPlayerInRange)
                {
                    currentVendor = vendor;
                    OpenVendor();
                    break;
                }
            }
        }

        private void OpenVendor()
        {
            if (vendorUI == null)
                return;

            vendorUI.Open();
        }
    }
}
