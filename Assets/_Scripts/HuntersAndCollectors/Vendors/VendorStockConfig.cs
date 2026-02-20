using System;
using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// VendorStockConfig
    /// ---------------------------------------------------------
    /// ScriptableObject that defines initial stock for a vendor chest.
    ///
    /// Why this exists:
    /// - Avoid hardcoding stock in VendorChestNet.
    /// - Lets designers adjust vendor inventory without code changes.
    /// - Later, persistence will override this on load, but this is perfect for MVP.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Vendors/Vendor Stock Config", fileName = "VendorStockConfig")]
    public sealed class VendorStockConfig : ScriptableObject
    {
        [Serializable]
        public struct StockLine
        {
            [Tooltip("Stable item id (must exist in ItemDatabase).")]
            public string ItemId;

            [Tooltip("Quantity to add to chest on initial scene spawn.")]
            public int Quantity;
        }

        [SerializeField] private List<StockLine> lines = new();

        public IReadOnlyList<StockLine> Lines => lines;
    }
}
