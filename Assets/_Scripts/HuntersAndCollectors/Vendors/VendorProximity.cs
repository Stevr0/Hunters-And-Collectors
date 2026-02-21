using HuntersAndCollectors.Players;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// Detects when a player enters vendor interaction range.
    /// </summary>
    public sealed class VendorProximity : MonoBehaviour
    {
        public bool IsPlayerInRange { get; private set; }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<PlayerNetworkRoot>() != null)
            {
                IsPlayerInRange = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponent<PlayerNetworkRoot>() != null)
            {
                IsPlayerInRange = false;
            }
        }
    }
}
