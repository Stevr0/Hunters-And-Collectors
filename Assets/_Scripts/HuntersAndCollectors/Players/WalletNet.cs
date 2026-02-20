using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Server-authoritative wallet state that prevents negative coin balances.
    /// </summary>
    public sealed class WalletNet : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab with PlayerNetworkRoot.
        [SerializeField] private int startingCoins = 100;
        private int coins;

        /// <summary>Current authoritative coin amount.</summary>
        public int Coins => coins;

        public override void OnNetworkSpawn()
        {
            if (IsServer && coins < 0) coins = 0;
            if (IsServer && OwnerClientId == NetworkManager.ServerClientId && coins == 0) coins = startingCoins;
        }

        /// <summary>
        /// Attempts to spend coins and returns false when insufficient funds.
        /// </summary>
        public bool TrySpend(int amount)
        {
            if (!IsServer || amount <= 0 || coins < amount) return false;
            coins -= amount;
            return true;
        }

        /// <summary>
        /// Adds positive coin amount on the server.
        /// </summary>
        public void AddCoins(int amount)
        {
            if (!IsServer || amount <= 0) return;
            coins += amount;
        }

        /// <summary>
        /// Replaces coins after server load while clamping invalid data.
        /// </summary>
        public void SetCoinsFromSave(int value) => coins = value < 0 ? 0 : value;
    }
}
