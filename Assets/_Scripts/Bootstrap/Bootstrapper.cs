using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections;

namespace HuntersAndCollectors.Bootstrap
{
    /// <summary>
    /// Bootstrapper
    /// -----------------------------------------------------
    /// MVP bootstrap:
    /// - Persist Bootstrap scene objects (UI, NetworkManager, services)
    /// - Start Host automatically
    /// - Load gameplay scene additively so Bootstrap content stays alive
    ///
    /// Spawn rule:
    /// - After the gameplay scene finishes loading (server-side),
    ///   teleport all connected players to a SceneSpawnPoint inside that scene.
    /// </summary>
    public sealed class Bootstrapper : MonoBehaviour
    {
        [SerializeField] private string gameplaySceneName = "SCN_Village";

        [Header("Spawn")]
        [SerializeField] private string spawnId = "Heartstone";

        private bool _requestedSceneLoad;

        private void Awake()
        {
            // Keep this object (and its children) alive when loading gameplay scenes.
            // IMPORTANT: Put your Canvas/VendorWindowUI as children of this same GameObject.
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            Debug.Log("[Bootstrapper] OnEnable called.");

            if (NetworkManager.Singleton == null)
            {
                Debug.LogWarning("[Bootstrapper] OnEnable: NetworkManager.Singleton is NULL (cannot subscribe yet).");
                return;
            }

            if (NetworkManager.Singleton.SceneManager == null)
            {
                Debug.LogWarning("[Bootstrapper] OnEnable: NetworkManager.SceneManager is NULL (cannot subscribe yet).");
                return;
            }

            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnNetworkSceneLoadComplete;
            Debug.Log("[Bootstrapper] Subscribed to SceneManager.OnLoadComplete.");
        }

        private void OnDisable()
        {
            Debug.Log("[Bootstrapper] OnDisable called.");

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnNetworkSceneLoadComplete;
                Debug.Log("[Bootstrapper] Unsubscribed from SceneManager.OnLoadComplete.");
            }
        }

        private void Start()
        {
            // Safety check
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[Bootstrapper] No NetworkManager in scene.");
                return;
            }

            // We only want to request a scene load once.
            if (_requestedSceneLoad)
                return;

            // Start Host automatically (MVP behavior)
            if (!NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.StartHost();
                Debug.Log("[Bootstrapper] Host started.");
            }
            // Subscribe here (Start) so we never miss the NetworkManager lifecycle.
            NetworkManager.Singleton.SceneManager.OnLoadComplete -= OnNetworkSceneLoadComplete;
            NetworkManager.Singleton.SceneManager.OnLoadComplete += OnNetworkSceneLoadComplete;
            Debug.Log("[Bootstrapper] Start: Subscribed to SceneManager.OnLoadComplete.");

            // Only the server/host should initiate NGO scene loads.
            if (NetworkManager.Singleton.IsServer)
            {
                _requestedSceneLoad = true;

                // Additive keeps Bootstrap scene content (Canvas/UI) alive.
                NetworkManager.Singleton.SceneManager.LoadScene(
                    gameplaySceneName,
                    LoadSceneMode.Additive
                );

                Debug.Log($"[Bootstrapper] Loading gameplay scene additively: {gameplaySceneName}");
            }
        }

        /// <summary>
        /// Called on BOTH server and clients when an NGO scene load completes for a client.
        /// We only act on the SERVER because spawn position is server-authoritative.
        /// </summary>
        private void OnNetworkSceneLoadComplete(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (sceneName != gameplaySceneName)
                return;

            if (!NetworkManager.Singleton.IsServer)
                return;

            // Delay to ensure:
            // - additive scene objects exist + can be found
            // - player object exists
            // - any spawn/movement scripts have run at least once
            StartCoroutine(TeleportClientAfterLoad(clientId));
        }

        private IEnumerator TeleportClientAfterLoad(ulong clientId)
        {
            // Wait 1 frame so scene hierarchy settles.
            yield return null;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            {
                Debug.LogWarning($"[Bootstrapper] Teleport skipped - no PlayerObject for clientId={clientId} yet.");
                yield break;
            }

            if (!TryFindSpawnPointInScene(gameplaySceneName, spawnId, out var spawn))
            {
                Debug.LogError($"[Bootstrapper] No SceneSpawnPoint with id '{spawnId}' found in scene '{gameplaySceneName}'. " +
                               $"(Make sure HeartstoneSpawnPoint has the SceneSpawnPoint component and SpawnId matches.)");
                yield break;
            }

            var playerObj = client.PlayerObject;

            // Log positions so we can prove what happened.
            var before = playerObj.transform.position;
            var target = spawn.transform.position;

            Debug.Log($"[Bootstrapper] Teleport attempt clientId={clientId} netId={playerObj.NetworkObjectId} FROM {before} TO {target}");

            TeleportPlayerToSpawn(playerObj, spawn);

            // Wait another frame and check if something snapped them back.
            yield return null;

            var after = playerObj.transform.position;
            Debug.Log($"[Bootstrapper] Teleport result clientId={clientId} pos={after}");

            // If it got overwritten, force it again one more time.
            // This commonly happens when the owner's NetworkTransform sends its first state.
            if ((after - target).sqrMagnitude > 0.01f)
            {
                Debug.LogWarning($"[Bootstrapper] Player snapped back after teleport. Forcing teleport again.");
                TeleportPlayerToSpawn(playerObj, spawn);
            }
        }

        /// <summary>
        /// Finds a SceneSpawnPoint that belongs to a specific Unity Scene by name.
        /// This matters when you load additively (multiple scenes exist at once).
        /// </summary>
        private static bool TryFindSpawnPointInScene(string sceneName, string spawnId, out SceneSpawnPoint spawnPoint)
        {
            // Note: FindObjectsByType finds objects across loaded scenes.
            var all = Object.FindObjectsByType<SceneSpawnPoint>(FindObjectsSortMode.None);

            for (int i = 0; i < all.Length; i++)
            {
                var sp = all[i];
                if (sp == null)
                    continue;

                // Ensure it is in the correct additive scene.
                if (sp.gameObject.scene.name != sceneName)
                    continue;

                // Ensure it matches the requested spawn id.
                if (!string.Equals(sp.SpawnId, spawnId, System.StringComparison.Ordinal))
                    continue;

                spawnPoint = sp;
                return true;
            }

            spawnPoint = null;
            return false;
        }

        /// <summary>
        /// Server-side teleport.
        /// Disables CharacterController (if present) before moving to avoid collision snapping.
        /// NetworkTransform / NGO will replicate the new position to clients.
        /// </summary>
        private static void TeleportPlayerToSpawn(NetworkObject playerObject, SceneSpawnPoint spawn)
        {
            var playerTransform = playerObject.transform;

            // If you use CharacterController-based movement, disable before teleporting.
            var cc = playerObject.GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;

            playerTransform.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);

            if (cc != null)
                cc.enabled = true;

            Debug.Log($"[Bootstrapper] Teleported PlayerObjectId={playerObject.NetworkObjectId} to spawn '{spawn.SpawnId}'.");
        }
    }
}