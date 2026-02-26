using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// WalletHudUI
    /// -------------------------
    /// Displays the local player's coin balance in a TMP text.
    ///
    /// How it works:
    /// - Waits for the local player NetworkObject to exist.
    /// - Gets WalletNet from that object.
    /// - Subscribes to the NetworkVariable change event.
    /// - Updates the UI whenever coins change.
    /// </summary>
    public sealed class WalletHudUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text coinsText;

        private HuntersAndCollectors.Players.WalletNet wallet;
        private bool subscribed;

        private void Awake()
        {
            if (coinsText == null)
                Debug.LogError("[WalletHudUI] coinsText is not assigned in the inspector.");
        }

        private void OnEnable()
        {
            TryBindToLocalWallet();
        }

        private void Update()
        {
            // If we haven't bound yet (player may not have spawned when UI enabled),
            // keep trying until it exists.
            if (!subscribed)
                TryBindToLocalWallet();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void TryBindToLocalWallet()
        {
            // NetworkManager might not exist yet if this UI loads very early.
            if (NetworkManager.Singleton == null)
                return;

            // LocalPlayer can be null briefly while connecting/spawning.
            var localPlayerObj = NetworkManager.Singleton.SpawnManager?.GetLocalPlayerObject();
            if (localPlayerObj == null)
                return;

            // Try get wallet from the local player object.
            if (!localPlayerObj.TryGetComponent(out HuntersAndCollectors.Players.WalletNet w))
                return;

            wallet = w;

            // Subscribe exactly once.
            if (!subscribed)
            {
                walletCoinsChangedSubscribe();
                subscribed = true;
            }

            // Update immediately so UI is correct right now.
            SetCoinsText(wallet.Coins);
        }

        private void walletCoinsChangedSubscribe()
        {
            // WalletNet uses a private NetworkVariable<int> coinsNet.
            // We can't access coinsNet directly from here.
            //
            // So: add a small public event in WalletNet OR add a public method in WalletNet
            // that lets us subscribe to its internal NetworkVariable.
            //
            // EASIEST production-safe approach:
            // - Add a public event OnCoinsChanged in WalletNet.
            //
            // For now, we’ll use a small required addition to WalletNet below.
            wallet.OnCoinsChanged += HandleCoinsChanged;
        }

        private void Unsubscribe()
        {
            if (wallet != null)
                wallet.OnCoinsChanged -= HandleCoinsChanged;

            wallet = null;
            subscribed = false;
        }

        private void HandleCoinsChanged(int previous, int current)
        {
            SetCoinsText(current);
        }

        private void SetCoinsText(int coins)
        {
            if (coinsText != null)
                coinsText.text = $"Coins: {coins}";
        }
    }
}