using HuntersAndCollectors.Items;

namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Represents one inventory slot with empty flag and optional stack.
    ///
    /// Durability and InstanceData are per-item-instance values used by tools/
    /// equippables that should not merge with regular stackable resources.
    /// </summary>
    public struct InventorySlot
    {
        public bool IsEmpty;
        public ItemStack Stack;
        public int Durability;
        public ItemInstanceData InstanceData;
    }
}
