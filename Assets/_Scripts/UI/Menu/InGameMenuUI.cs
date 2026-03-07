using HuntersAndCollectors.Bootstrap;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Persistence;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI.Menu
{
    public sealed class InGameMenuUI : MonoBehaviour
    {
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private KeyCode toggleKey = KeyCode.Escape;
        [SerializeField] private KeyCode temporaryUnlockKey = KeyCode.LeftAlt;

        [Header("Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button saveNowButton;
        [SerializeField] private Button returnToMainMenuButton;
        [SerializeField] private Button quitButton;

        private bool isOpen;
        private bool gameplayLockClaimedByMenu;

        private void Awake()
        {
            // Temporary editor-safe binding: if legacy scene serialization still uses Escape, force M.
            if (toggleKey == KeyCode.Escape)
            {
                toggleKey = KeyCode.M;
                Debug.Log("[InGameMenuUI] Toggle key remapped from Escape to M for in-editor testing.");
            }

            if (resumeButton != null) resumeButton.onClick.AddListener(OnResumePressed);
            if (saveNowButton != null) saveNowButton.onClick.AddListener(OnSaveNowPressed);
            if (returnToMainMenuButton != null) returnToMainMenuButton.onClick.AddListener(OnReturnToMainMenuPressed);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuitPressed);
        }

        private void OnDestroy()
        {
            if (resumeButton != null) resumeButton.onClick.RemoveListener(OnResumePressed);
            if (saveNowButton != null) saveNowButton.onClick.RemoveListener(OnSaveNowPressed);
            if (returnToMainMenuButton != null) returnToMainMenuButton.onClick.RemoveListener(OnReturnToMainMenuPressed);
            if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitPressed);
        }

        private void Start()
        {
            if (menuPanel != null)
                menuPanel.SetActive(false);

            isOpen = false;
            ApplyCursorState();
        }

        private void OnEnable()
        {
            ApplyCursorState();
        }

        private void OnDisable()
        {
            // If menu UI gets disabled while open, release any gameplay lock we claimed.
            if (gameplayLockClaimedByMenu)
            {
                gameplayLockClaimedByMenu = false;
                InputState.UnlockGameplay();
            }
        }

        private void Update()
        {
            if (!IsGameplaySessionActive())
                return;

            if (UnityEngine.Input.GetKeyDown(toggleKey))
                SetOpen(!isOpen);

            // Keep cursor state synced every frame for temporary unlock key and other UI locks.
            ApplyCursorState();
        }

        public void OnResumePressed()
        {
            SetOpen(false);
        }

        public void OnSaveNowPressed()
        {
            if (SaveManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                SaveManager.Instance.SaveAllNow();
            else
                Debug.LogWarning("[InGameMenuUI] Save Now requires server authority. Add a request relay for remote clients.");
        }

        public void OnReturnToMainMenuPressed()
        {
            if (SaveManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                SaveManager.Instance.SaveAllNow();

            Bootstrapper bootstrapper = Bootstrapper.Instance != null ? Bootstrapper.Instance : FindFirstObjectByType<Bootstrapper>();
            if (bootstrapper != null)
                bootstrapper.ReturnToMainMenu(false);
        }

        public void OnQuitPressed()
        {
            if (SaveManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                SaveManager.Instance.SaveAllNow();

            Bootstrapper bootstrapper = Bootstrapper.Instance != null ? Bootstrapper.Instance : FindFirstObjectByType<Bootstrapper>();
            if (bootstrapper != null)
                bootstrapper.ReturnToMainMenu(true);
            else
                Application.Quit();
        }

        private void SetOpen(bool value)
        {
            isOpen = value;
            if (menuPanel != null)
                menuPanel.SetActive(value);

            // Opening this menu should lock gameplay controls once; closing it releases that lock.
            if (isOpen && !gameplayLockClaimedByMenu)
            {
                gameplayLockClaimedByMenu = true;
                InputState.LockGameplay();
            }
            else if (!isOpen && gameplayLockClaimedByMenu)
            {
                gameplayLockClaimedByMenu = false;
                InputState.UnlockGameplay();
            }

            ApplyCursorState();
        }

        private void ApplyCursorState()
        {
            if (!IsGameplaySessionActive())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            // Respect global gameplay lock (inventory/vendor/crafting/menu, etc.).
            if (InputState.GameplayLocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            // Temporary free cursor while holding Alt in gameplay.
            bool temporaryUnlock = UnityEngine.Input.GetKey(temporaryUnlockKey);
            Cursor.lockState = temporaryUnlock ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = temporaryUnlock;
        }

        private static bool IsGameplaySessionActive()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }
    }
}
