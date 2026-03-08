namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Owner-visible reasons for authoritative consume-food requests.
    /// Keep stable so UI can map responses reliably.
    /// </summary>
    public enum FoodConsumeResult
    {
        None = 0,
        InvalidRequest = 1,
        SlotOutOfRange = 2,
        SlotEmpty = 3,
        ItemNotFood = 4,
        AlreadyActive = 5,
        NoFoodSlotAvailable = 6,
        InventoryRemoveFailed = 7,
        ConfigError = 8
    }
}
