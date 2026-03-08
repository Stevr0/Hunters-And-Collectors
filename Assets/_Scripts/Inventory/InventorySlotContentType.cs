namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Explicit tagged content type for inventory slots.
    ///
    /// Why this exists:
    /// - We now support both stack payloads and unique item-instance payloads.
    /// - A tagged enum prevents ambiguous slot state and makes serialization clearer.
    /// </summary>
    public enum InventorySlotContentType : byte
    {
        Empty = 0,
        Stack = 1,
        Instance = 2
    }
}
