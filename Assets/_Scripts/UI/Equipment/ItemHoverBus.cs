using System;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// ItemHoverBus
    /// --------------------------------------------------------------------
    /// A tiny client-only event bus for hover tooltip payloads.
    ///
    /// Networking note:
    /// - Pure local UI state (no networking, no RPC).
    /// </summary>
    public static class ItemHoverBus
    {
        /// <summary>
        /// Fired when the mouse is over a slot containing an item.
        /// Payload already includes instance-aware tooltip values.
        /// </summary>
        public static event Action<ItemTooltipData> HoveredItemChanged;

        /// <summary>
        /// Fired when the mouse leaves an item slot.
        /// </summary>
        public static event Action HoverCleared;

        public static void PublishHover(ItemTooltipData tooltipData)
        {
            if (string.IsNullOrWhiteSpace(tooltipData.ItemId))
            {
                PublishClear();
                return;
            }

            HoveredItemChanged?.Invoke(tooltipData);
        }

        /// <summary>
        /// Legacy helper for older callers that only know itemId.
        /// </summary>
        public static void PublishHover(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                PublishClear();
                return;
            }

            ItemTooltipData data = new ItemTooltipData { ItemId = itemId };
            HoveredItemChanged?.Invoke(data);
        }

        public static void PublishClear()
        {
            HoverCleared?.Invoke();
        }
    }
}
