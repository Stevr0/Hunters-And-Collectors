using HuntersAndCollectors.Bootstrap;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Persistence;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.UI.Menu
{
    /// <summary>
    /// Controls the in-game pause/menu window.
    ///
    /// IMPORTANT SETUP RULE:
    /// - This script should live on an object that stays ACTIVE at all times.
    /// - The actual menu panel can be enabled/disabled.
    /// - Do NOT put this script on the menu panel itself if the panel starts disabled,
    ///   because Update() will not run on inactive objects.
    /// </summary>
    public sealed class InGameMenuUI : MonoBehaviour
    {
        [Header("Panel Root")]
        [Tooltip("The actual panel GameObject that is shown/hidden.")]
        [SerializeField] private GameObject menuPanel;

        [Header("Keys")]
        [Tooltip("Key used to open/close the in-game menu.")]
        [SerializeField] private Key toggleKey = Key.Escape;

        [Tooltip("Hold this key to temporarily unlock the cursor while playing.")]
        [SerializeField] private Key temporaryUnlockKey = Key.LeftAlt;

        [Header("Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button saveNowButton;
        [SerializeField] private Button returnToMainMenuButton;
        [SerializeField] private Button quitButton;

        private bool isOpen;
        private bool gameplayLockClaimedByMenu;

        private void Awake()
        {
            // Wire button events in code so the menu still works
            // even if the OnClick list is empty in the inspector.
            if (resumeButton != null)
                resumeButton.onClick.AddListener(OnResumePressed);

            if (saveNowButton != null)
                saveNowButton.onClick.AddListener(OnSaveNowPressed);

            if (returnToMainMenuButton != null)
                returnToMainMenuButton.onClick.AddListener(OnReturnToMainMenuPressed);

            if (quitButton != null)
                quitButton.onClick.AddListener(OnQuitPressed);
        }

        private void OnDestroy()
        {
            // Always unhook listeners when destroyed.
            if (resumeButton != null)
                resumeButton.onClick.RemoveListener(OnResumePressed);

            if (saveNowButton != null)
                saveNowButton.onClick.RemoveListener(OnSaveNowPressed);

            if (returnToMainMenuButton != null)
                returnToMainMenuButton.onClick.RemoveListener(OnReturnToMainMenuPressed);

            if (quitButton != null)
                quitButton.onClick.RemoveListener(OnQuitPressed);
        }

        private void Start()
        {
            // Ensure the menu starts closed.
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
            // Safety: if this controller is disabled while it owns the gameplay lock,
            // release that lock so the player is not stuck.
            if (gameplayLockClaimedByMenu)
            {
                gameplayLockClaimedByMenu = false;
                InputState.UnlockGameplay();
            }
        }

        private void Update()
        {
            // Only allow the in-game menu during an active gameplay session.
            if (!IsGameplaySessionActive())
                return;

            // Toggle the menu when the chosen key is pressed this frame.
            if (IsTogglePressed())
                SetOpen(!isOpen);

            // Keep cursor state updated every frame.
            ApplyCursorState();
        }

        /// <summary>
        /// Resume gameplay by closing the menu.
        /// </summary>
        public void OnResumePressed()
        {
            SetOpen(false);
        }

        /// <summary>
        /// Request an immediate save.
        /// Current MVP behavior only works when this instance has server authority.
        /// </summary>
        public void OnSaveNowPressed()
        {
            if (SaveManager.Instance != null &&
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsServer)
            {
                SaveManager.Instance.SaveAllNow();
            }
            else
            {
                Debug.LogWarning("[InGameMenuUI] Save Now currently requires server authority.");
            }
        }

        /// <summary>
        /// Save first, then return to main menu.
        /// </summary>
        public void OnReturnToMainMenuPressed()
        {
            if (SaveManager.Instance != null &&
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsServer)
            {
                SaveManager.Instance.SaveAllNow();
            }

            Bootstrapper bootstrapper =
                Bootstrapper.Instance != null
                ? Bootstrapper.Instance
                : FindFirstObjectByType<Bootstrapper>();

            if (bootstrapper != null)
                bootstrapper.ReturnToMainMenu(false);
        }

        /// <summary>
        /// Save first, then quit.
        /// </summary>
        public void OnQuitPressed()
        {
            if (SaveManager.Instance != null &&
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsServer)
            {
                SaveManager.Instance.SaveAllNow();
            }

            Bootstrapper bootstrapper =
                Bootstrapper.Instance != null
                ? Bootstrapper.Instance
                : FindFirstObjectByType<Bootstrapper>();

            if (bootstrapper != null)
                bootstrapper.ReturnToMainMenu(true);
            else
                Application.Quit();
        }

        /// <summary>
        /// Open or close the menu panel and claim/release gameplay lock.
        /// </summary>
        private void SetOpen(bool value)
        {
            isOpen = value;

            if (menuPanel != null)
                menuPanel.SetActive(value);

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

        /// <summary>
        /// Controls cursor visibility/lock depending on menu/gameplay state.
        /// </summary>
        private void ApplyCursorState()
        {
            if (!IsGameplaySessionActive())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (InputState.GameplayLocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            bool temporaryUnlock = IsTemporaryUnlockHeld();
            Cursor.lockState = temporaryUnlock ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = temporaryUnlock;
        }

        /// <summary>
        /// Returns true only on the frame the toggle key is pressed.
        /// </summary>
        private bool IsTogglePressed()
        {
            if (Keyboard.current == null)
                return false;

            return IsKeyPressedThisFrame(toggleKey);
        }

        /// <summary>
        /// Returns true while the temporary unlock key is being held.
        /// </summary>
        private bool IsTemporaryUnlockHeld()
        {
            if (Keyboard.current == null)
                return false;

            return IsKeyHeld(temporaryUnlockKey);
        }

        /// <summary>
        /// Small helper to check a key press this frame using the new Input System.
        /// </summary>
        private static bool IsKeyPressedThisFrame(Key key)
        {
            return key switch
            {
                Key.Escape => Keyboard.current.escapeKey.wasPressedThisFrame,
                Key.M => Keyboard.current.mKey.wasPressedThisFrame,
                Key.LeftAlt => Keyboard.current.leftAltKey.wasPressedThisFrame,
                Key.RightAlt => Keyboard.current.rightAltKey.wasPressedThisFrame,
                _ => false
            };
        }

        /// <summary>
        /// Small helper to check whether a key is currently held using the new Input System.
        /// </summary>
        private static bool IsKeyHeld(Key key)
        {
            return key switch
            {
                Key.Escape => Keyboard.current.escapeKey.isPressed,
                Key.M => Keyboard.current.mKey.isPressed,
                Key.LeftAlt => Keyboard.current.leftAltKey.isPressed,
                Key.RightAlt => Keyboard.current.rightAltKey.isPressed,
                _ => false
            };
        }

        /// <summary>
        /// The menu only works once NGO is actually running a live session.
        /// </summary>
        private static bool IsGameplaySessionActive()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        }
    }
}