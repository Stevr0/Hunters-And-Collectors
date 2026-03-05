namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Represents one inventory slot with empty flag and optional stack.
    /// Durability is per-slot state for durable (MaxDurability > 0) items.
    /// </summary>
    public struct InventorySlot
    {
        public bool IsEmpty;
        public ItemStack Stack;
        public int Durability;
    }
}
