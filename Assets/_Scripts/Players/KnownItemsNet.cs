using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Server-authoritative replicated known item registry.
    /// </summary>
    public sealed class KnownItemsNet : NetworkBehaviour
    {
        private readonly NetworkList<KnownItemEntry> known = new();

        /// <summary>
        /// Returns true if item is already known.
        /// </summary>
        public bool IsKnown(string itemId)
        {
            var key = new FixedString64Bytes(itemId);

            foreach (var k in known)
                if (k.ItemId.Equals(key))
                    return true;

            return false;
        }

        /// <summary>
        /// Gets base price or default fallback.
        /// </summary>
        public int GetBasePriceOrDefault(string itemId, int defaultPrice = 1)
        {
            var key = new FixedString64Bytes(itemId);

            foreach (var k in known)
                if (k.ItemId.Equals(key))
                    return k.BasePrice;

            return defaultPrice;
        }

        /// <summary>
        /// Adds item to known list with default base price.
        /// </summary>
        public void EnsureKnown(string itemId)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(itemId))
                return;

            var key = new FixedString64Bytes(itemId);

            foreach (var k in known)
                if (k.ItemId.Equals(key))
                    return;

            known.Add(new KnownItemEntry
            {
                ItemId = key,
                BasePrice = 1
            });
        }

        /// <summary>
        /// Sets base price (server only).
        /// </summary>
        public bool TrySetBasePrice(string itemId, int basePrice)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(itemId) || basePrice < 0)
                return false;

            var key = new FixedString64Bytes(itemId);

            for (int i = 0; i < known.Count; i++)
            {
                if (!known[i].ItemId.Equals(key))
                    continue;

                var entry = known[i];
                entry.BasePrice = basePrice;
                known[i] = entry; // IMPORTANT: triggers replication
                return true;
            }

            // If not known yet
            EnsureKnown(itemId);
            return TrySetBasePrice(itemId, basePrice);
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