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
        public struct ActionResult : INetworkSerializable
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
    /// Result payload for checkout transactions.
    /// </summary>
    public struct TransactionResult : INetworkSerializable
    {
        public ActionResult Result;
        public int TotalPrice;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            Result.NetworkSerialize(serializer);
            serializer.SerializeValue(ref TotalPrice);
        }
    }

    /// <summary>
    /// Result payload for harvesting requests.
    /// </summary>
    public struct HarvestResult : INetworkSerializable
    {
        public ActionResult Result;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            Result.NetworkSerialize(serializer);
        }
    }

    /// <summary>
    /// Result payload for crafting requests.
    /// </summary>
    public struct CraftResult : INetworkSerializable
    {
        public ActionResult Result;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            Result.NetworkSerialize(serializer);
        }
    }
}