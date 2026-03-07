using Unity.Netcode;
using UnityEngine;
using System;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// WalletNet
    /// --------------------------------------------------------------------
    /// Server-authoritative coin wallet replicated to all clients.
    /// </summary>
    public sealed class WalletNet : NetworkBehaviour
    {
        [Header("MVP Defaults")]
        [Tooltip("Coins granted to a newly created character (server only).")]
        [SerializeField] private int startingCoins = 100;

        public event Action<int, int> OnCoinsChanged;

        private readonly NetworkVariable<int> coinsNet =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Coins => coinsNet.Value;

        public override void OnNetworkSpawn()
        {
            coinsNet.OnValueChanged += HandleCoinsChanged;

            if (!IsServer)
                return;

            if (coinsNet.Value < 0)
                coinsNet.Value = 0;

            if (coinsNet.Value == 0)
                coinsNet.Value = Mathf.Max(0, startingCoins);
        }

        public override void OnDestroy()
        {
            coinsNet.OnValueChanged -= HandleCoinsChanged;
            base.OnDestroy();
        }

        private void HandleCoinsChanged(int previousValue, int newValue)
        {
            OnCoinsChanged?.Invoke(previousValue, newValue);
        }

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

        public void AddCoins(int amount)
        {
            if (!IsServer)
                return;

            if (amount <= 0)
                return;

            coinsNet.Value += amount;
        }

        public void SetCoinsFromSave(int value)
        {
            if (!IsServer)
                return;

            coinsNet.Value = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Explicit server-side restore API used by persistence.
        /// </summary>
        public void ServerSetCoins(int value)
        {
            SetCoinsFromSave(value);
        }
    }
}

