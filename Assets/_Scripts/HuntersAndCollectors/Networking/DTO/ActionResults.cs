using Unity.Netcode;

namespace HuntersAndCollectors.Networking.DTO
{
    /// <summary>
    /// Enumerates server-validated failure reasons for gameplay actions.
    /// </summary>
    public enum FailureReason : byte
    {
        None,
        InvalidRequest,
        OutOfRange,
        NotEnoughCoins,
        NotEnoughInventorySpace,
        OutOfStock,
        VendorNotFound,
        RecipeNotFound,
        MissingIngredients,
        NodeNotHarvestable,
        OnCooldown,
    }

    /// <summary>
    /// Result payload for checkout transactions.
    /// </summary>
    public struct TransactionResult : INetworkSerializable
    {
        public bool Success;
        public FailureReason Reason;
        public int TotalPrice;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Success);
            serializer.SerializeValue(ref Reason);
            serializer.SerializeValue(ref TotalPrice);
        }
    }

    /// <summary>
    /// Result payload for harvesting requests.
    /// </summary>
    public struct HarvestResult : INetworkSerializable
    {
        public bool Success;
        public FailureReason Reason;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Success);
            serializer.SerializeValue(ref Reason);
        }
    }

    /// <summary>
    /// Result payload for crafting requests.
    /// </summary>
    public struct CraftResult : INetworkSerializable
    {
        public bool Success;
        public FailureReason Reason;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Success);
            serializer.SerializeValue(ref Reason);
        }
    }
}
