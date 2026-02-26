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

            // Use a coroutine so we can wait a frame if VendorChestNet hasn't initialized yet.
            StartCoroutine(ApplyStockWhenReady());
        }

        private System.Collections.IEnumerator ApplyStockWhenReady()
        {
            if (applied) yield break;

            if (chest == null)
                chest = GetComponent<VendorChestNet>();

            // Wait until chest + grid exist (up to a small safety limit).
            const int maxFrames = 10;
            int frames = 0;

            while ((chest == null || chest.Grid == null) && frames < maxFrames)
            {
                frames++;
                yield return null; // wait 1 frame
            }

            if (chest == null || chest.Grid == null)
            {
                Debug.LogWarning("[VendorChestStockLoader] Chest/Grid not ready after wait. Stock not applied.");
                yield break;
            }

            if (stockConfig == null || stockConfig.Lines == null || stockConfig.Lines.Count == 0)
            {
                Debug.LogWarning("[VendorChestStockLoader] No stock config assigned.");
                yield break;
            }

            foreach (var line in stockConfig.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.ItemId)) continue;

                int qty = line.Quantity < 1 ? 1 : line.Quantity;
                chest.Grid.Add(line.ItemId, qty);
            }

            applied = true;

            chest.ForceBroadcastSnapshot();
            Debug.Log("[VendorChestStockLoader] Initial stock applied (after grid ready).");
        }
    }
}