using HuntersAndCollectors.Actors;
using HuntersAndCollectors.Combat;
using HuntersAndCollectors.Persistence;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.UI.Menu;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.Bootstrap
{
    /// <summary>
    /// Menu-driven bootstrap coordinator.
    /// </summary>
    public sealed class Bootstrapper : MonoBehaviour
    {
        [SerializeField] private string gameplaySceneName = "SCN_Village";

        [Header("Session")]
        [SerializeField] private bool autoStartHostForLegacy = false;
        [SerializeField] private string defaultPlayerKey = "Client_0";
        [SerializeField] private string defaultShardKey = "Shard_Default";
        [SerializeField] private SaveManager saveManager;

        [Header("Spawn")]
        [SerializeField] private string spawnId = "Heartstone";

        [Header("Grounding")]
        [SerializeField] private bool snapPlayerToGround = true;
        [SerializeField] private LayerMask groundMask;
        [Min(0.1f)] [SerializeField] private float groundRayStartHeight = 50f;
        [Min(1f)] [SerializeField] private float groundRayDistance = 200f;
        [Min(0f)] [SerializeField] private float groundContactSkin = 0.05f;

        private bool requestedSceneLoad;
        private bool startupActorsSpawned;
        private bool shardInitialized;
        private string activePlayerKey;
        private string activeShardKey;
        private ActorSpawner actorSpawner;
        private Coroutine startupSpawnRoutine;
        private bool sceneLoadCallbackRegistered;
        private bool unitySceneLoadedRegistered;

        public static Bootstrapper Instance { get; private set; }

        public string GameplaySceneName => gameplaySceneName;

        public static bool TryRespawnPlayerAtDefaultSpawn(PlayerNetworkRoot playerRoot)
        {
            return Instance != null && Instance.ServerRespawnPlayerAtDefaultSpawn(playerRoot);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (saveManager == null)
                saveManager = FindFirstObjectByType<SaveManager>();

            if (saveManager == null)
            {
                GameObject saveManagerGo = new("SaveManager");
                saveManager = saveManagerGo.AddComponent<SaveManager>();
            }
        }

        private void OnEnable()
        {
            RegisterSceneLoadCallbackIfNeeded();
            RegisterUnitySceneLoadedCallbackIfNeeded();
        }

        private void OnDisable()
        {
            if (sceneLoadCallbackRegistered && NetworkManager.Singleton?.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnNetworkSceneLoadComplete;
                sceneLoadCallbackRegistered = false;
            }

            if (unitySceneLoadedRegistered)
            {
                SceneManager.sceneLoaded -= OnUnitySceneLoaded;
                unitySceneLoadedRegistered = false;
            }

            if (startupSpawnRoutine != null)
            {
                StopCoroutine(startupSpawnRoutine);
                startupSpawnRoutine = null;
            }
        }

        private void Start()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[Bootstrapper] No NetworkManager in scene.");
                return;
            }

            RegisterSceneLoadCallbackIfNeeded();
            RegisterUnitySceneLoadedCallbackIfNeeded();

            if (autoStartHostForLegacy)
                StartGameSession(defaultPlayerKey, defaultShardKey);
        }

        public void StartGameSession(string playerKey, string shardKey)
        {
            if (NetworkManager.Singleton == null)
                return;

            RegisterSceneLoadCallbackIfNeeded();
            RegisterUnitySceneLoadedCallbackIfNeeded();

            activePlayerKey = string.IsNullOrWhiteSpace(playerKey) ? defaultPlayerKey : playerKey.Trim();
            activeShardKey = string.IsNullOrWhiteSpace(shardKey) ? defaultShardKey : shardKey.Trim();

            if (NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[Bootstrapper] StartGameSession ignored because network session is already running.");
                return;
            }

            PlayerNetworkRoot.SetPlayerKeyOverride(NetworkManager.ServerClientId, activePlayerKey);

            if (!NetworkManager.Singleton.StartHost())
            {
                Debug.LogError("[Bootstrapper] Failed to start host.");
                return;
            }

            // Host startup can race callback availability. Keep retrying bind for a short window.
            StartCoroutine(CoEnsureCallbacksBoundAfterHostStart());

            Debug.Log($"[Bootstrapper] Host started for PlayerKey='{activePlayerKey}' ShardKey='{activeShardKey}'.");

            if (NetworkManager.Singleton.IsServer && !requestedSceneLoad)
            {
                requestedSceneLoad = true;
                shardInitialized = false;
                startupActorsSpawned = false;

                NetworkManager.Singleton.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Additive);
                Debug.Log($"[Bootstrapper] Loading gameplay scene additively: {gameplaySceneName}");
            }
        }

        public void ReturnToMainMenu(bool quitApplication)
        {
            StartCoroutine(ReturnToMainMenuRoutine(quitApplication));
        }

        private IEnumerator ReturnToMainMenuRoutine(bool quitApplication)
        {
            if (saveManager != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                saveManager.SaveAllNow();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            Scene gameplayScene = SceneManager.GetSceneByName(gameplaySceneName);
            if (gameplayScene.IsValid() && gameplayScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(gameplaySceneName);

            requestedSceneLoad = false;
            startupActorsSpawned = false;
            shardInitialized = false;
            activePlayerKey = string.Empty;
            activeShardKey = string.Empty;
            PlayerNetworkRoot.ClearPlayerKeyOverride(NetworkManager.ServerClientId);

            if (quitApplication)
                Application.Quit();
        }

        private void OnNetworkSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (sceneName != gameplaySceneName)
                return;

            if (!NetworkManager.Singleton.IsServer)
                return;

            Debug.Log($"[Bootstrapper] OnLoadComplete clientId={clientId} scene='{sceneName}' mode={mode}.");
            HandleGameplaySceneLoadedForServer(clientId);
        }

        private void RegisterSceneLoadCallbackIfNeeded()
        {
            if (sceneLoadCallbackRegistered)
                return;

            if (NetworkManager.Singleton?.SceneManager == null)
                return;

            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnNetworkSceneLoadComplete;
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnNetworkSceneLoadComplete;
            sceneLoadCallbackRegistered = true;
            Debug.Log("[Bootstrapper] Registered OnLoadComplete callback.");
        }

        private void RegisterUnitySceneLoadedCallbackIfNeeded()
        {
            if (unitySceneLoadedRegistered)
                return;

            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
            SceneManager.sceneLoaded += OnUnitySceneLoaded;
            unitySceneLoadedRegistered = true;
            Debug.Log("[Bootstrapper] Registered Unity sceneLoaded callback.");
        }

        private IEnumerator CoEnsureCallbacksBoundAfterHostStart()
        {
            const int maxFrames = 120;
            for (int i = 0; i < maxFrames; i++)
            {
                RegisterSceneLoadCallbackIfNeeded();
                RegisterUnitySceneLoadedCallbackIfNeeded();

                if (sceneLoadCallbackRegistered && unitySceneLoadedRegistered)
                    yield break;

                yield return null;
            }

            Debug.LogWarning("[Bootstrapper] Callback bind retry timed out; relying on available callbacks.");
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!string.Equals(scene.name, gameplaySceneName, System.StringComparison.Ordinal))
                return;

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                return;

            Debug.Log($"[Bootstrapper] Unity sceneLoaded fired for '{scene.name}' mode={mode}.");
            HandleGameplaySceneLoadedForServer(null);
        }

        private void HandleGameplaySceneLoadedForServer(ulong? specificClientId)
        {
            SetGameplaySceneActiveIfLoaded(gameplaySceneName);

            if (!shardInitialized)
            {
                if (saveManager == null)
                    saveManager = FindFirstObjectByType<SaveManager>();

                if (saveManager != null)
                {
                    saveManager.InitializeForShard(string.IsNullOrWhiteSpace(activeShardKey) ? defaultShardKey : activeShardKey);
                    shardInitialized = true;
                }
                else
                {
                    Debug.LogWarning("[Bootstrapper] SaveManager not found. Shard initialization skipped.");
                }
            }

            if (startupSpawnRoutine == null && !startupActorsSpawned)
                startupSpawnRoutine = StartCoroutine(SpawnStartupActorsWhenReady());

            if (specificClientId.HasValue)
            {
                StartCoroutine(TeleportClientAfterLoad(specificClientId.Value));
                return;
            }

            if (NetworkManager.Singleton == null)
                return;

            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                StartCoroutine(TeleportClientAfterLoad(clientId));
        }



        private void EnsureInGameMenuRuntimeReady()
        {
            InGameMenuUI[] menus = Resources.FindObjectsOfTypeAll<InGameMenuUI>();
            for (int i = 0; i < menus.Length; i++)
            {
                InGameMenuUI menu = menus[i];
                if (menu == null)
                    continue;

                GameObject go = menu.gameObject;
                if (go == null || !go.scene.IsValid())
                    continue;

                if (!go.activeSelf)
                {
                    go.SetActive(true);
                    Debug.Log("[Bootstrapper] Activated InGameMenuUI GameObject for ESC handling.");
                }

                if (!menu.enabled)
                    menu.enabled = true;

                return;
            }

            Debug.LogWarning("[Bootstrapper] InGameMenuUI was not found in loaded scenes.");
        }
        private void EnsureGameplayUiRootEnabled()
        {
            Scene gameplayScene = SceneManager.GetSceneByName(gameplaySceneName);
            if (!gameplayScene.IsValid() || !gameplayScene.isLoaded)
                return;

            GameObject[] roots = gameplayScene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                if (!string.Equals(root.name, "UIRoot", System.StringComparison.Ordinal))
                    continue;

                if (!root.activeSelf)
                {
                    root.SetActive(true);
                    Debug.Log("[Bootstrapper] Enabled gameplay UIRoot after scene load.");
                }

                return;
            }
        }
        private IEnumerator SpawnStartupActorsWhenReady()
        {
            const int maxFrames = 120;
            for (int i = 0; i < maxFrames && !startupActorsSpawned; i++)
            {
                TrySpawnStartupActors();
                if (startupActorsSpawned)
                    break;

                yield return null;
            }

            startupSpawnRoutine = null;
        }

        private IEnumerator TeleportClientAfterLoad(ulong clientId)
        {
            // Wait for the player's NetworkObject to exist. On some load timings it is not ready on frame 1.
            const int maxFramesToWaitForPlayerObject = 180;
            NetworkClient client = null;

            for (int i = 0; i < maxFramesToWaitForPlayerObject; i++)
            {
                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out client) &&
                    client.PlayerObject != null)
                {
                    break;
                }

                yield return null;
            }

            if (client == null || client.PlayerObject == null)
            {
                Debug.LogWarning($"[Bootstrapper] Teleport skipped - no PlayerObject for clientId={clientId} after wait.");
                yield break;
            }

            // Wait until server-side player save load has had a chance to stage any saved world position.
            const int maxFramesToWaitForPlayerLoad = 180;
            for (int i = 0; i < maxFramesToWaitForPlayerLoad; i++)
            {
                if (SaveManager.IsPlayerLoadedStatic(clientId))
                    break;

                yield return null;
            }

            NetworkObject playerObj = client.PlayerObject;
            PlayerNetworkRoot playerRoot = playerObj.GetComponent<PlayerNetworkRoot>();
            Vector3 target;
            Quaternion targetRotation;
            string spawnLabel;

            bool restoredSavedLocation = false;
            if (playerRoot != null && playerRoot.ServerTryGetLoadedWorldState(out string savedSceneName, out Vector3 savedPosition, out Quaternion savedRotation))
            {
                target = savedPosition;
                targetRotation = savedRotation;
                spawnLabel = "saved position";
                restoredSavedLocation = true;

                if (!string.IsNullOrWhiteSpace(savedSceneName) && !string.Equals(savedSceneName, gameplaySceneName, System.StringComparison.Ordinal))
                    yield return HuntersAndCollectors.World.AreaTransferService.EnsureInstance().ServerRestoreSavedLocation(playerRoot, savedSceneName, savedPosition, savedRotation);
                else
                    playerRoot.ServerSetCurrentWorldScene(gameplaySceneName);

                Debug.Log($"[PlayerLoad] Applied saved position for key={playerRoot.PlayerKey} scene='{savedSceneName}'");
            }
            else
            {
                if (!TryResolvePlayerSpawn(out Vector3 spawnPosition, out Quaternion spawnRotation))
                {
                    Debug.LogError($"[Bootstrapper] No spawn point found for id '{spawnId}' in scene '{gameplaySceneName}'.");
                    yield break;
                }

                if (snapPlayerToGround)
                    spawnPosition = ResolveGroundedPlayerPosition(playerObj, spawnPosition);

                target = spawnPosition;
                targetRotation = spawnRotation;
                spawnLabel = spawnId;
                playerRoot?.ServerSetCurrentWorldScene(gameplaySceneName);
            }

            Vector3 before = playerObj.transform.position;
            Debug.Log($"[Bootstrapper] Teleport attempt clientId={clientId} netId={playerObj.NetworkObjectId} FROM {before} TO {target}");

            TeleportPlayerToSpawn(playerObj, target, targetRotation, spawnLabel);

            yield return null;

            Vector3 after = playerObj.transform.position;
            Debug.Log($"[Bootstrapper] Teleport result clientId={clientId} pos={after}");

            if ((after - target).sqrMagnitude > 0.01f)
            {
                Debug.LogWarning("[Bootstrapper] Player snapped back after teleport. Forcing teleport again.");
                TeleportPlayerToSpawn(playerObj, target, targetRotation, spawnLabel);
            }
        }

        private bool ServerRespawnPlayerAtDefaultSpawn(PlayerNetworkRoot playerRoot)
        {
            if (playerRoot == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                return false;

            NetworkObject playerObject = playerRoot.NetworkObject;
            if (playerObject == null || !playerObject.IsSpawned)
                return false;

            Debug.Log($"[Respawn] Reusing existing player object id={playerObject.NetworkObjectId}");
            Debug.Log($"[Respawn] Player object activeInHierarchy={playerObject.gameObject.activeInHierarchy}");

            if (!TryResolvePlayerSpawn(out Vector3 spawnPosition, out Quaternion spawnRotation))
            {
                Debug.LogError($"[Respawn] No spawn point found for player key={playerRoot.PlayerKey} using spawnId='{spawnId}'.");
                return false;
            }

            if (snapPlayerToGround)
                spawnPosition = ResolveGroundedPlayerPosition(playerObject, spawnPosition);

            Debug.Log($"[Respawn] Respawning player key={playerRoot.PlayerKey} at spawnId={spawnId}");
            TeleportPlayerToSpawn(playerObject, spawnPosition, spawnRotation, spawnId);
            playerRoot.ServerSetCurrentWorldScene(gameplaySceneName);
            Debug.Log($"[Respawn] Teleported player key={playerRoot.PlayerKey} to pos=({spawnPosition.x:F3},{spawnPosition.y:F3},{spawnPosition.z:F3})");
            Debug.Log($"[Respawn] Teleport complete for surviving player object id={playerObject.NetworkObjectId}");

            PlayerVitalsNet vitals = playerRoot.GetComponent<PlayerVitalsNet>();
            if (vitals != null)
            {
                vitals.ServerResetToFull();
            }
            else
            {
                HealthNet health = playerRoot.GetComponent<HealthNet>();
                health?.ServerResetHealth();
            }

            Debug.Log($"[Respawn] Restored health for player key={playerRoot.PlayerKey}");
            return true;
        }
        private void TrySpawnStartupActors()
        {
            if (startupActorsSpawned)
                return;

            if (actorSpawner == null)
                actorSpawner = FindFirstObjectByType<ActorSpawner>();

            if (actorSpawner == null)
                return;

            actorSpawner.ServerSpawnConfiguredActorsOnce();
            startupActorsSpawned = true;
        }

        private bool TryResolvePlayerSpawn(out Vector3 position, out Quaternion rotation)
        {
            if (actorSpawner == null)
                actorSpawner = FindFirstObjectByType<ActorSpawner>();

            if (actorSpawner != null && actorSpawner.TryGetPlayerSpawnTransform(spawnId, out position, out rotation))
                return true;

            if (HuntersAndCollectors.World.SceneSpawnRegistry.TryGetSpawnPoint(gameplaySceneName, spawnId, out SceneSpawnPoint worldSpawn))
            {
                position = worldSpawn.transform.position;
                rotation = worldSpawn.transform.rotation;
                return true;
            }

            if (TryFindSpawnPointInScene(gameplaySceneName, spawnId, out var legacySpawn))
            {
                position = legacySpawn.transform.position;
                rotation = legacySpawn.transform.rotation;
                return true;
            }

            // Safe fallback for mis-typed ids: use first spawn in scene instead of dropping at origin.
            if (TryFindAnySpawnPointInScene(gameplaySceneName, out legacySpawn))
            {
                position = legacySpawn.transform.position;
                rotation = legacySpawn.transform.rotation;
                Debug.LogWarning($"[Bootstrapper] Spawn id '{spawnId}' not found. Falling back to first scene spawn '{legacySpawn.SpawnId}'.");
                return true;
            }

            position = default;
            rotation = default;
            return false;
        }

        private static bool TryFindSpawnPointInScene(string sceneName, string targetSpawnId, out SceneSpawnPoint spawnPoint)
        {
            var all = Object.FindObjectsByType<SceneSpawnPoint>(FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                SceneSpawnPoint sp = all[i];
                if (sp == null)
                    continue;

                if (sp.gameObject.scene.name != sceneName)
                    continue;

                // Case-insensitive to prevent brittle id mismatch (Heartstone vs HeartStone).
                if (!string.Equals(sp.SpawnId, targetSpawnId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                spawnPoint = sp;
                return true;
            }

            spawnPoint = null;
            return false;
        }

        private static bool TryFindAnySpawnPointInScene(string sceneName, out SceneSpawnPoint spawnPoint)
        {
            var all = Object.FindObjectsByType<SceneSpawnPoint>(FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                SceneSpawnPoint sp = all[i];
                if (sp == null)
                    continue;

                if (sp.gameObject.scene.name != sceneName)
                    continue;

                spawnPoint = sp;
                return true;
            }

            spawnPoint = null;
            return false;
        }

        private Vector3 ResolveGroundedPlayerPosition(NetworkObject playerObject, Vector3 desiredPosition)
        {
            EnsureGroundMaskInitialized();

            Vector3 rayOrigin = desiredPosition + Vector3.up * Mathf.Max(0.1f, groundRayStartHeight);
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, Mathf.Max(1f, groundRayDistance), groundMask, QueryTriggerInteraction.Ignore))
                return desiredPosition;

            float bottomOffset = 0f;
            if (playerObject != null)
            {
                CharacterController cc = playerObject.GetComponent<CharacterController>();
                if (cc != null)
                {
                    bottomOffset = cc.center.y - (cc.height * 0.5f);
                }
                else
                {
                    Collider col = playerObject.GetComponentInChildren<Collider>(true);
                    if (col != null)
                        bottomOffset = col.bounds.min.y - playerObject.transform.position.y;
                }
            }

            desiredPosition.y = hit.point.y - bottomOffset + Mathf.Max(0f, groundContactSkin);
            return desiredPosition;
        }

        private void EnsureGroundMaskInitialized()
        {
            if (groundMask.value != 0)
                return;

            int groundLayer = LayerMask.NameToLayer("Ground");
            groundMask = groundLayer >= 0 ? (1 << groundLayer) : Physics.DefaultRaycastLayers;
        }


        private void SetGameplaySceneActiveIfLoaded(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return;

            Scene gameplayScene = SceneManager.GetSceneByName(sceneName);
            if (!gameplayScene.IsValid() || !gameplayScene.isLoaded)
            {
                Debug.LogWarning($"[Bootstrapper] Active scene change skipped because gameplay scene '{sceneName}' is not loaded yet.", this);
                return;
            }

            if (SceneManager.GetActiveScene() == gameplayScene)
                return;

            if (SceneManager.SetActiveScene(gameplayScene))
                Debug.Log($"[Bootstrapper] Active scene changed to gameplay scene '{sceneName}'.", this);
            else
                Debug.LogWarning($"[Bootstrapper] Failed to set active scene to gameplay scene '{sceneName}'.", this);
        }

        public static string ResolveDefaultGameplaySceneName()
        {
            return Instance != null ? Instance.GameplaySceneName : string.Empty;
        }

        public static bool MoveRuntimeGameplayObjectToScene(GameObject gameplayObject, string targetSceneName, string ownerSystemLabel)
        {
            if (gameplayObject == null)
                return false;

            if (string.IsNullOrWhiteSpace(targetSceneName))
            {
                Debug.LogError($"[Bootstrapper] Runtime gameplay object '{gameplayObject.name}' could not be moved because targetSceneName was empty. owner='{ownerSystemLabel}'.", gameplayObject);
                return false;
            }

            Scene targetScene = SceneManager.GetSceneByName(targetSceneName.Trim());
            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                Debug.LogError($"[Bootstrapper] Runtime gameplay object '{gameplayObject.name}' could not be moved because scene '{targetSceneName}' is not loaded. owner='{ownerSystemLabel}'.", gameplayObject);
                return false;
            }

            string currentSceneName = gameplayObject.scene.IsValid() ? gameplayObject.scene.name : "<invalid>";
            if (string.Equals(currentSceneName, targetScene.name, System.StringComparison.Ordinal))
                return true;

            if (string.Equals(currentSceneName, "SCN_Bootstrap", System.StringComparison.Ordinal))
                Debug.LogWarning($"[Bootstrapper] Gameplay object '{gameplayObject.name}' was instantiated into SCN_Bootstrap by '{ownerSystemLabel}'. Moving it to '{targetScene.name}'.", gameplayObject);

            SceneManager.MoveGameObjectToScene(gameplayObject, targetScene);
            Debug.Log($"[Bootstrapper] Runtime gameplay object '{gameplayObject.name}' moved from '{currentSceneName}' to '{targetScene.name}' by '{ownerSystemLabel}'.", gameplayObject);
            return true;
        }
        private void MovePlayerObjectToGameplayScene(NetworkObject playerObject, string sceneName)
        {
            if (playerObject == null || string.IsNullOrWhiteSpace(sceneName))
                return;

            Scene destinationScene = SceneManager.GetSceneByName(sceneName);
            if (!destinationScene.IsValid() || !destinationScene.isLoaded)
            {
                Debug.LogWarning($"[Bootstrapper] Scene membership update skipped because scene '{sceneName}' is not loaded yet.", playerObject);
                return;
            }

            if (playerObject.gameObject.scene == destinationScene)
                return;

            SceneManager.MoveGameObjectToScene(playerObject.gameObject, destinationScene);
            Debug.Log($"[Bootstrapper] Player scene membership changed to '{sceneName}' for PlayerObjectId={playerObject.NetworkObjectId}.", playerObject);
        }
        private static void TeleportPlayerToSpawn(NetworkObject playerObject, Vector3 position, Quaternion rotation, string spawnLabel)
        {
            Transform playerTransform = playerObject.transform;
            CharacterController cc = playerObject.GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;

            playerTransform.SetPositionAndRotation(position, rotation);

            if (cc != null)
                cc.enabled = true;

            Debug.Log($"[Bootstrapper] Teleported PlayerObjectId={playerObject.NetworkObjectId} to spawn '{spawnLabel}'.");
        }
    }
}














