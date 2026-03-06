using System;
using Unity.Collections;
using Unity.Netcode;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Small replicated food-slot payload for UI.
    ///
    /// Notes:
    /// - This is NOT persistence data in first pass.
    /// - RemainingSeconds is quantized by server updates (whole-second style) to reduce churn.
    /// - ItemId stays authoritative and is always sourced from server inventory validation.
    /// </summary>
    [Serializable]
    public struct ActiveFoodBuff : INetworkSerializable, IEquatable<ActiveFoodBuff>
    {
        public FixedString64Bytes ItemId;
        public int MaxHealthBonus;
        public int MaxStaminaBonus;
        public float HealthRegenBonus;
        public float StaminaRegenBonus;
        public float RemainingSeconds;

        public bool IsValid => ItemId.Length > 0 && RemainingSeconds > 0f;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref MaxHealthBonus);
            serializer.SerializeValue(ref MaxStaminaBonus);
            serializer.SerializeValue(ref HealthRegenBonus);
            serializer.SerializeValue(ref StaminaRegenBonus);
            serializer.SerializeValue(ref RemainingSeconds);
        }

        public bool Equals(ActiveFoodBuff other)
        {
            return ItemId.Equals(other.ItemId)
                && MaxHealthBonus == other.MaxHealthBonus
                && MaxStaminaBonus == other.MaxStaminaBonus
                && Math.Abs(HealthRegenBonus - other.HealthRegenBonus) < 0.0001f
                && Math.Abs(StaminaRegenBonus - other.StaminaRegenBonus) < 0.0001f
                && Math.Abs(RemainingSeconds - other.RemainingSeconds) < 0.0001f;
        }

        public override bool Equals(object obj)
        {
            return obj is ActiveFoodBuff other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ItemId.GetHashCode(), MaxHealthBonus, MaxStaminaBonus, HealthRegenBonus, StaminaRegenBonus, RemainingSeconds);
        }
    }
}
