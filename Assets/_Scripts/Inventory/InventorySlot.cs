namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Represents one inventory slot with empty flag and optional stack.
    /// </summary>
    public struct InventorySlot
    {
        public bool IsEmpty;
        public ItemStack Stack;
    }
}
