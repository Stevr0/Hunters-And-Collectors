using HuntersAndCollectors.Players;
using UnityEngine;

namespace HuntersAndCollectors.UI.KnownItems
{
    /// <summary>
    /// Minimal known-items window for updating base prices via server rpc.
    /// </summary>
    public sealed class KnownItemsWindowUI : MonoBehaviour
    {
        // Editor wiring checklist: assign local player's KnownItemsNet when UI opens.
        [SerializeField] private KnownItemsNet knownItems;

        /// <summary>
        /// Sends a base-price update request to the authoritative server.
        /// </summary>
        public void RequestSetBasePrice(string itemId, int basePrice)
        {
            knownItems?.RequestSetBasePriceServerRpc(itemId, basePrice);
        }
    }
}
