using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// WalletNet
    /// --------------------------------------------------------------------
    /// Server-authoritative coin wallet replicated to all clients.
    ///
    /// Key rules:
    /// - Only the server may change coin balances.
    /// - Clients can read the balance (for UI), but cannot write.
    /// - Starting coins are applied once on server spawn (unless loaded from save).
    /// </summary>
    public sealed class WalletNet : NetworkBehaviour
    {
        [Header("MVP Defaults")]
        [Tooltip("Coins granted to a newly created character (server only).")]
        [SerializeField] private int startingCoins = 100;

        // Replicated value. Everyone can read; only server can write.
        private readonly NetworkVariable<int> coinsNet =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// Current coin amount (replicated).
        /// Safe to read on client for UI.
        /// </summary>
        public int Coins => coinsNet.Value;

        /// <summary>
        /// Server-side initialization.
        /// IMPORTANT: This runs for every player object on the server.
        /// </summary>
        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            // Clamp invalid values (defensive)
            if (coinsNet.Value < 0)
                coinsNet.Value = 0;

            // If no save system has set coins yet, apply starting coins.
            // NOTE: This applies to every newly spawned player, not just host.
            if (coinsNet.Value == 0)
                coinsNet.Value = Mathf.Max(0, startingCoins);
        }

        /// <summary>
        /// Attempts to spend coins (server only).
        /// Returns false if insufficient funds or invalid request.
        /// </summary>
        public bool TrySpend(int amount)
        {
            if (!IsServer)
                return false;

            if (amount <= 0)
                return false;

            if (coinsNet.Value < amount)
                return false;

            coinsNet.Value -= amount;
            return true;
        }

        /// <summary>
        /// Adds coins (server only).
        /// </summary>
        public void AddCoins(int amount)
        {
            if (!IsServer)
                return;

            if (amount <= 0)
                return;

            coinsNet.Value += amount;
        }

        /// <summary>
        /// Used by persistence load on the SERVER to restore coins.
        /// Must clamp invalid save data.
        /// </summary>
        public void SetCoinsFromSave(int value)
        {
            if (!IsServer)
                return;

            coinsNet.Value = value < 0 ? 0 : value;
        }
    }
}
