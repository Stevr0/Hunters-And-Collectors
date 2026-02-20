using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Vendors;
using UnityEngine;

namespace HuntersAndCollectors.UI.Vendor
{
    /// <summary>
    /// Minimal vendor window that sends open and checkout requests.
    /// </summary>
    public sealed class VendorWindowUI : MonoBehaviour
    {
        // Editor wiring checklist: assign interacting VendorInteractable before opening window.
        [SerializeField] private VendorInteractable vendorInteractable;

        /// <summary>
        /// Requests opening the active vendor and snapshot broadcast.
        /// </summary>
        public void RequestOpenVendor() => vendorInteractable?.RequestOpenVendorServerRpc();

        /// <summary>
        /// Sends checkout request payload assembled from selected cart lines.
        /// </summary>
        public void RequestCheckout(CheckoutRequest request) => vendorInteractable?.RequestCheckoutServerRpc(request);
    }
}
