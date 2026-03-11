using System.Collections;
using System.Collections.Generic;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    /// <summary>
    /// Server-authoritative save coordinator.
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private float autosaveIntervalSeconds = 60f;

        private PlayerSaveService playerSaveService;
        private ShardSaveService shardSaveService;
        private readonly HashSet<ulong> loadedClientIds = new();
        private float autosaveTimer;
        private string activeShardKey = string.Empty;
        private bool initialized;

        public static SaveManager Instance { get; private set; }
        public string ActiveShardKey => activeShardKey;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SaveManager] Duplicate instance detected. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (itemDatabase == null)
            {
                ItemDatabase[] databases = Resources.FindObjectsOfTypeAll<ItemDatabase>();
                if (databases != null && databases.Length > 0)
                    itemDatabase = databases[0];
            }

            playerSaveService = new PlayerSaveService(itemDatabase);
            shardSaveService = new ShardSaveService(itemDatabase);
            SavePaths.EnsureDirectories();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            UnsubscribeFromNetworkCallbacks();
        }

        private void OnApplicationQuit()
        {
            // Server-authoritative shutdown save hook.
            if (IsServerReady() && initialized)
                SaveAllNow();
        }
        private void Update()
        {
            if (!IsServerReady())
                return;

            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveTimer < Mathf.Max(1f, autosaveIntervalSeconds))
                return;

            autosaveTimer = 0f;
            SaveAllNow();
        }

        public void InitializeForShard(string shardKey)
        {
            if (!IsServerReady())
            {
                Debug.LogWarning("[SaveManager] InitializeForShard ignored because server is not running.");
                return;
            }

            if (string.IsNullOrWhiteSpace(shardKey))
                shardKey = "Shard_Default";

            activeShardKey = shardKey.Trim();
            autosaveTimer = 0f;
            initialized = true;
            loadedClientIds.Clear();

            SubscribeToNetworkCallbacks();
            LoadShardNow();

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                StartCoroutine(LoadPlayerWhenReady(client.ClientId));

            Debug.Log($"[SaveManager] Initialized for shard '{activeShardKey}'.");
        }

        public void LoadShardNow()
        {
            if (!IsServerReady() || !initialized)
                return;

            shardSaveService.LoadOrCreateAndApply(activeShardKey);
            Debug.Log($"[SaveManager] Shard loaded: {activeShardKey}");
        }

        public void SaveShardNow()
        {
            if (!IsServerReady() || !initialized)
                return;

            shardSaveService.Save(activeShardKey);
        }

        public void LoadPlayerNow(PlayerNetworkRoot playerRoot)
        {
            if (!IsServerReady() || !initialized || playerRoot == null)
                return;

            if (!playerRoot.IsSpawned)
                return;

            playerSaveService.LoadOrCreateAndApply(playerRoot);
            loadedClientIds.Add(playerRoot.OwnerClientId);
            Debug.Log($"[SaveManager] Player loaded: {playerRoot.PlayerKey}");
        }

        public void SavePlayerNow(PlayerNetworkRoot playerRoot)
        {
            if (!IsServerReady() || !initialized || playerRoot == null)
                return;

            playerSaveService.Save(playerRoot);
        }

        public void SaveAllNow()
        {
            if (!IsServerReady() || !initialized)
                return;

            SaveShardNow();

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                    continue;

                PlayerNetworkRoot root = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
                if (root == null)
                    continue;

                SavePlayerNow(root);
            }

            Debug.Log("[SaveManager] SaveAllNow completed.");
        }

        public static void NotifyPlayerProgressChanged(PlayerNetworkRoot playerRoot)
        {
            if (Instance == null || playerRoot == null)
                return;

            if (!Instance.IsServerReady() || !Instance.initialized)
                return;

            Instance.SavePlayerNow(playerRoot);
        }

        public static void NotifyShardStateChanged()
        {
            if (Instance == null)
                return;

            if (!Instance.IsServerReady() || !Instance.initialized)
                return;

            Instance.SaveShardNow();
        }
        private IEnumerator LoadPlayerWhenReady(ulong clientId)
        {
            const int maxFrames = 300;

            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (!IsServerReady())
                    yield break;

                if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
                {
                    yield return null;
                    continue;
                }

                PlayerNetworkRoot root = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
                if (root == null || root.Wallet == null || root.Skills == null || root.KnownItems == null || root.Inventory == null)
                {
                    yield return null;
                    continue;
                }

                LoadPlayerNow(root);
                yield break;
            }

            Debug.LogWarning($"[SaveManager] Timed out waiting for player object readiness for client {clientId}.");
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServerReady() || !initialized)
                return;

            if (loadedClientIds.Contains(clientId))
                return;

            StartCoroutine(LoadPlayerWhenReady(clientId));
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (!initialized)
                return;

            // PlayerObject may already be gone at this point. If still available, save it.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
            {
                PlayerNetworkRoot root = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
                if (root != null)
                    SavePlayerNow(root);
            }

            loadedClientIds.Remove(clientId);
        }

        private bool IsServerReady()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkManager.Singleton.IsListening;
        }

        private void SubscribeToNetworkCallbacks()
        {
            if (NetworkManager.Singleton == null)
                return;

            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;

            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }

        private void UnsubscribeFromNetworkCallbacks()
        {
            if (NetworkManager.Singleton == null)
                return;

            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
    }
}



