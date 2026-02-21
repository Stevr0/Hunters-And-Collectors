using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    public sealed class VendorProximity : MonoBehaviour
    {
        [SerializeField] private VendorInteractable vendor; // assign in inspector
        public VendorInteractable Vendor => vendor;

        public bool IsPlayerInRange { get; private set; }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
                IsPlayerInRange = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.CompareTag("Player"))
                IsPlayerInRange = false;
        }
    }
}
