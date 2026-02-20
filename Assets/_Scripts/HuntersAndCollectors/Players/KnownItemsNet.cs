using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Tracks known item ids and negotiable base prices on the authoritative server.
    /// </summary>
    public sealed class KnownItemsNet : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab with PlayerNetworkRoot.
        private readonly Dictionary<string, int> known = new();

        /// <summary>
        /// Returns true when item id already exists in known-item registry.
        /// </summary>
        public bool IsKnown(string itemId) => known.ContainsKey(itemId ?? string.Empty);

        /// <summary>
        /// Gets configured base price or default fallback for unknown item ids.
        /// </summary>
        public int GetBasePriceOrDefault(string itemId, int defaultPrice = 1)
        {
            return known.TryGetValue(itemId ?? string.Empty, out var value) ? value : defaultPrice;
        }

        /// <summary>
        /// Ensures item id exists with default base price when first discovered.
        /// </summary>
        public void EnsureKnown(string itemId)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(itemId) || known.ContainsKey(itemId)) return;
            known[itemId] = 1;
        }

        /// <summary>
        /// Sets base price when value is non-negative.
        /// </summary>
        public bool TrySetBasePrice(string itemId, int basePrice)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(itemId) || basePrice < 0) return false;
            EnsureKnown(itemId);
            known[itemId] = basePrice;
            return true;
        }

        /// <summary>
        /// Owner-authorized RPC for base price updates.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        public void RequestSetBasePriceServerRpc(string itemId, int basePrice)
        {
            TrySetBasePrice(itemId, basePrice);
        }
    }
}
