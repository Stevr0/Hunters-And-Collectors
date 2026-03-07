using HuntersAndCollectors.Bootstrap;
using HuntersAndCollectors.Persistence;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace HuntersAndCollectors.UI.Menu
{
    public sealed class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private Button continueButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button manageSavesButton;
        [SerializeField] private Button quitButton;

        [Header("Panels")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject characterSelectPanel;
        [SerializeField] private GameObject manageSavesPanel;

        private void Awake()
        {
            EnsureEventSystemForMenuInput();

            if (continueButton != null) continueButton.onClick.AddListener(OnContinuePressed);
            if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGamePressed);
            if (manageSavesButton != null) manageSavesButton.onClick.AddListener(OnManageSavesPressed);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuitPressed);
        }

        private void OnDestroy()
        {
            if (continueButton != null) continueButton.onClick.RemoveListener(OnContinuePressed);
            if (startGameButton != null) startGameButton.onClick.RemoveListener(OnStartGamePressed);
            if (manageSavesButton != null) manageSavesButton.onClick.RemoveListener(OnManageSavesPressed);
            if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitPressed);
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshContinueState();
        }

        public void RefreshContinueState()
        {
            IReadOnlyList<SaveFileInfo> playerSaves = SaveDiscoveryService.DiscoverPlayerSaves();
            IReadOnlyList<SaveFileInfo> shardSaves = SaveDiscoveryService.DiscoverShardSaves();

            bool hasPlayer = playerSaves.Count > 0;
            bool hasShard = shardSaves.Count > 0;

            if (continueButton != null)
                continueButton.interactable = hasPlayer && hasShard;
        }

        public void OnContinuePressed()
        {
            IReadOnlyList<SaveFileInfo> playerSaves = SaveDiscoveryService.DiscoverPlayerSaves();
            IReadOnlyList<SaveFileInfo> shardSaves = SaveDiscoveryService.DiscoverShardSaves();
            if (playerSaves.Count == 0 || shardSaves.Count == 0)
                return;

            SessionSelectionState.SelectedPlayerKey = playerSaves[0].Key;
            SessionSelectionState.SelectedShardKey = shardSaves[0].Key;

            Bootstrapper bootstrapper = Bootstrapper.Instance != null ? Bootstrapper.Instance : FindFirstObjectByType<Bootstrapper>();
            if (bootstrapper != null)
                bootstrapper.StartGameSession(SessionSelectionState.SelectedPlayerKey, SessionSelectionState.SelectedShardKey);
        }

        public void OnStartGamePressed()
        {
            Debug.Log("[MainMenuUI] Start Game clicked.");

            if (mainMenuPanel != null)
                mainMenuPanel.SetActive(false);

            if (characterSelectPanel == null)
            {
                CharacterSelectUI found = FindFirstObjectByType<CharacterSelectUI>(FindObjectsInactive.Include);
                if (found != null)
                    characterSelectPanel = found.gameObject;
            }

            if (characterSelectPanel != null)
            {
                characterSelectPanel.SetActive(true);
                return;
            }

            Debug.LogWarning("[MainMenuUI] Character Select panel was not assigned/found. Falling back to direct session start.");

            if (string.IsNullOrWhiteSpace(SessionSelectionState.SelectedPlayerKey))
                SessionSelectionState.SelectedPlayerKey = "Client_0";
            if (string.IsNullOrWhiteSpace(SessionSelectionState.SelectedShardKey))
                SessionSelectionState.SelectedShardKey = "Shard_Default";

            Bootstrapper bootstrapper = Bootstrapper.Instance != null ? Bootstrapper.Instance : FindFirstObjectByType<Bootstrapper>();
            if (bootstrapper != null)
                bootstrapper.StartGameSession(SessionSelectionState.SelectedPlayerKey, SessionSelectionState.SelectedShardKey);
        }

        public void OnManageSavesPressed()
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (manageSavesPanel != null) manageSavesPanel.SetActive(true);
        }

        public void OnQuitPressed()
        {
            Bootstrapper bootstrapper = Bootstrapper.Instance != null ? Bootstrapper.Instance : FindFirstObjectByType<Bootstrapper>();
            if (bootstrapper != null && Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
            {
                bootstrapper.ReturnToMainMenu(true);
                return;
            }

            Application.Quit();
        }

        private static void EnsureEventSystemForMenuInput()
        {
            EventSystem current = EventSystem.current;
            if (current == null)
            {
                GameObject eventSystemGo = new("EventSystem", typeof(EventSystem));
                current = eventSystemGo.GetComponent<EventSystem>();
                Debug.Log("[MainMenuUI] Created EventSystem at runtime.");
            }

#if ENABLE_INPUT_SYSTEM
            // New Input System projects need InputSystemUIInputModule for button clicks.
            if (current.GetComponent<InputSystemUIInputModule>() == null)
            {
                current.gameObject.AddComponent<InputSystemUIInputModule>();
                Debug.Log("[MainMenuUI] Added InputSystemUIInputModule for menu clicks.");
            }
#else
            // Legacy input projects need StandaloneInputModule.
            if (current.GetComponent<StandaloneInputModule>() == null)
            {
                current.gameObject.AddComponent<StandaloneInputModule>();
                Debug.Log("[MainMenuUI] Added StandaloneInputModule for menu clicks.");
            }
#endif
        }
    }
}
