using HuntersAndCollectors.Crafting;
using Unity.Collections;
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
        CraftFailed,
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
    /// Includes debug info for UI/analytics.
    /// </summary>
    public struct CraftResult : INetworkSerializable
    {
        public ActionResult Result;
        public FixedString64Bytes RecipeId;
        public CraftingCategory Category;
        public byte AttemptIndex;
        public byte AttemptsRequested;
        public byte SkillLevel;
        public float RolledChance;
        public float RollValue;
        public bool IngredientsConsumed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            Result.NetworkSerialize(serializer);
            serializer.SerializeValue(ref RecipeId);
            serializer.SerializeValue(ref Category);
            serializer.SerializeValue(ref AttemptIndex);
            serializer.SerializeValue(ref AttemptsRequested);
            serializer.SerializeValue(ref SkillLevel);
            serializer.SerializeValue(ref RolledChance);
            serializer.SerializeValue(ref RollValue);
            serializer.SerializeValue(ref IngredientsConsumed);
        }
    }
}
