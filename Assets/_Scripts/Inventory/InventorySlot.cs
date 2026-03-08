using HuntersAndCollectors.Items;

namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Tagged-union inventory slot.
    ///
    /// Supported payloads:
    /// - Empty
    /// - Stack (ItemStack)
    /// - Instance (ItemInstance)
    ///
    /// Backward-compatibility notes:
    /// - Durability + ItemInstanceData are retained as bridge fields because several
    ///   existing equipment/building systems already consume them.
    /// - For instance slots, these fields are kept in sync with the ItemInstance data.
    /// </summary>
    public struct InventorySlot
    {
        public bool IsEmpty;
        public InventorySlotContentType ContentType;

        // Stack payload (valid when ContentType == Stack).
        public ItemStack Stack;

        // Instance payload (valid when ContentType == Instance).
        public ItemInstance Instance;

        // Bridge fields used by existing systems.
        public int Durability;
        public ItemInstanceData InstanceData;
    }
}
