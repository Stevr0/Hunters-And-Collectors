using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// VendorChestStockLoader
    /// ---------------------------------------------------------
    /// Server-only initial stock applier.
    ///
    /// Rules:
    /// - Runs only on the server.
    /// - Applies stock ONE TIME per network spawn.
    /// - Designed for MVP until persistence is wired.
    ///
    /// Later:
    /// - When persistence exists, you either disable this component
    ///   or make it apply only if no save data was loaded.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VendorChestStockLoader : NetworkBehaviour
    {
        [SerializeField] private VendorChestNet chest;
        [SerializeField] private VendorStockConfig stockConfig;

        private bool applied;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            if (applied) return;

            // Auto-resolve chest if not assigned
            if (chest == null)
                chest = GetComponent<VendorChestNet>();

            if (chest == null || chest.Grid == null)
            {
                Debug.LogWarning("[VendorChestStockLoader] Chest/Grid not ready. Stock not applied.");
                return;
            }

            if (stockConfig == null || stockConfig.Lines == null || stockConfig.Lines.Count == 0)
            {
                Debug.LogWarning("[VendorChestStockLoader] No stock config assigned.");
                return;
            }

            // Apply stock
            foreach (var line in stockConfig.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.ItemId)) continue;

                var qty = line.Quantity < 1 ? 1 : line.Quantity;
                chest.Grid.Add(line.ItemId, qty);
            }

            applied = true;

            // Broadcast so all clients see current chest state
            chest.ForceBroadcastSnapshot();

            Debug.Log("[VendorChestStockLoader] Initial stock applied.");
        }
    }
}
