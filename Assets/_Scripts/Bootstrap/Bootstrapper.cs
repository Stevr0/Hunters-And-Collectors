using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.Bootstrap
{
    /// <summary>
    /// Bootstrapper
    /// -----------------------------------------------------
    /// MVP bootstrap:
    /// - Persist Bootstrap scene objects (UI, NetworkManager, services)
    /// - Start Host automatically
    /// - Load gameplay scene additively so Bootstrap content stays alive
    /// </summary>
    public sealed class Bootstrapper : MonoBehaviour
    {
        [SerializeField] private string gameplaySceneName = "SCN_Village";

        private bool _requestedSceneLoad;

        private void Awake()
        {
            // Keep this object (and its children) alive when loading gameplay scenes.
            // IMPORTANT: Put your Canvas/VendorWindowUI as children of this same GameObject.
            DontDestroyOnLoad(gameObject);
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
            if (!_requestedSceneLoad)
            {
                // Start Host automatically (MVP behavior)
                if (!NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.StartHost();
                    Debug.Log("[Bootstrapper] Host started.");
                }

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
        }
    }
}