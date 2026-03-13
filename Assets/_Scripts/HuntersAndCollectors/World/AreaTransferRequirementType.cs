namespace HuntersAndCollectors.World
{
    /// <summary>
    /// First-pass requirement model for authored area transfers.
    /// This keeps transfer validation data-driven without introducing a larger quest system yet.
    /// </summary>
    public enum AreaTransferRequirementType
    {
        None,
        Item,
        Flag,
        ItemAndFlag
    }
}
