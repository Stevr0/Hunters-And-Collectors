using HuntersAndCollectors.Bootstrap;
using HuntersAndCollectors.Persistence;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI.Menu
{
    public sealed class CharacterSelectUI : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown playerDropdown;
        [SerializeField] private TMP_InputField newPlayerKeyInput;
        [SerializeField] private TMP_InputField newPlayerDisplayNameInput;

        [Header("Buttons")]
        [SerializeField] private Button createCharacterButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button backButton;

        [Header("Panels")]
        [SerializeField] private GameObject characterPanel;
        [SerializeField] private GameObject worldPanel;
        [SerializeField] private GameObject mainMenuPanel;

        private readonly List<string> currentKeys = new();

        private void Awake()
        {
            if (createCharacterButton != null) createCharacterButton.onClick.AddListener(OnCreateCharacterPressed);
            if (continueButton != null) continueButton.onClick.AddListener(OnContinuePressed);
            if (backButton != null) backButton.onClick.AddListener(OnBackPressed);
        }

        private void OnDestroy()
        {
            if (createCharacterButton != null) createCharacterButton.onClick.RemoveListener(OnCreateCharacterPressed);
            if (continueButton != null) continueButton.onClick.RemoveListener(OnContinuePressed);
            if (backButton != null) backButton.onClick.RemoveListener(OnBackPressed);
        }

        private void OnEnable()
        {
            RefreshList();
        }

        public void RefreshList()
        {
            currentKeys.Clear();

            IReadOnlyList<SaveFileInfo> playerSaves = SaveDiscoveryService.DiscoverPlayerSaves();
            for (int i = 0; i < playerSaves.Count; i++)
                currentKeys.Add(playerSaves[i].Key);

            MenuIndexData menuIndex = MenuIndexService.Load();
            for (int i = 0; i < menuIndex.players.Count; i++)
            {
                string key = menuIndex.players[i].key;
                if (string.IsNullOrWhiteSpace(key) || currentKeys.Contains(key))
                    continue;

                currentKeys.Add(key);
            }

            if (playerDropdown == null)
                return;

            playerDropdown.ClearOptions();
            playerDropdown.AddOptions(currentKeys.Count > 0 ? currentKeys : new List<string> { "<No Characters>" });
            playerDropdown.value = 0;
            playerDropdown.RefreshShownValue();
        }

        public void OnCreateCharacterPressed()
        {
            string key = newPlayerKeyInput != null ? newPlayerKeyInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                key = $"Client_{System.DateTime.UtcNow.Ticks}";

            string displayName = newPlayerDisplayNameInput != null ? newPlayerDisplayNameInput.text : key;
            MenuIndexService.AddPlayer(key.Trim(), displayName);
            SessionSelectionState.SelectedPlayerKey = key.Trim();
            RefreshList();

            int index = currentKeys.FindIndex(k => k == SessionSelectionState.SelectedPlayerKey);
            if (index >= 0 && playerDropdown != null)
                playerDropdown.value = index;
        }

        public void OnContinuePressed()
        {
            if (currentKeys.Count == 0)
            {
                SessionSelectionState.SelectedPlayerKey = "Client_0";
            }
            else
            {
                int index = playerDropdown != null ? Mathf.Clamp(playerDropdown.value, 0, currentKeys.Count - 1) : 0;
                SessionSelectionState.SelectedPlayerKey = currentKeys[index];
            }

            if (characterPanel != null)
                characterPanel.SetActive(false);

            if (worldPanel == null)
            {
                WorldSelectUI found = FindFirstObjectByType<WorldSelectUI>(FindObjectsInactive.Include);
                if (found != null)
                    worldPanel = found.gameObject;
            }

            if (worldPanel != null)
            {
                worldPanel.SetActive(true);
                return;
            }

            Debug.LogWarning("[CharacterSelectUI] World panel missing, falling back to direct start.");
            if (string.IsNullOrWhiteSpace(SessionSelectionState.SelectedShardKey))
                SessionSelectionState.SelectedShardKey = "Shard_Default";

            Bootstrapper bootstrapper = Bootstrapper.Instance != null ? Bootstrapper.Instance : FindFirstObjectByType<Bootstrapper>();
            if (bootstrapper != null)
                bootstrapper.StartGameSession(SessionSelectionState.SelectedPlayerKey, SessionSelectionState.SelectedShardKey);
        }

        public void OnBackPressed()
        {
            if (characterPanel != null) characterPanel.SetActive(false);
            if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        }
    }
}
