using System;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// ItemHoverBus
    /// --------------------------------------------------------------------
    /// A tiny client-only "event bus" for UI hover state.
    ///
    /// Why this exists:
    /// - Inventory slots should not need references to other windows.
    /// - Paperdoll (and later: crafting, vendor, tooltips) can all listen to the same hover.
    ///
    /// Networking note:
    /// - This is purely local UI state (NO networking, NO RPCs).
    /// </summary>
    public static class ItemHoverBus
    {
        /// <summary>
        /// Fired when the mouse is over an item slot that contains an item.
        /// Parameter: hovered itemId (stable string id, e.g., "IT_StoneAxe")
        /// </summary>
        public static event Action<string> HoveredItemChanged;

        /// <summary>
        /// Fired when the mouse leaves an item slot (or hovers an empty slot).
        /// </summary>
        public static event Action HoverCleared;

        /// <summary>
        /// Call when a UI element is hovered and you want the rest of the UI to know which item it is.
        /// </summary>
        public static void PublishHover(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                PublishClear();
                return;
            }

            HoveredItemChanged?.Invoke(itemId);
        }

        /// <summary>
        /// Call when hover ends (mouse leaves slot).
        /// </summary>
        public static void PublishClear()
        {
            HoverCleared?.Invoke();
        }
    }
}
