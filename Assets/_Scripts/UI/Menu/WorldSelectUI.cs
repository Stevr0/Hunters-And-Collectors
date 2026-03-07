using HuntersAndCollectors.Bootstrap;
using HuntersAndCollectors.Persistence;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI.Menu
{
    public sealed class WorldSelectUI : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown shardDropdown;
        [SerializeField] private TMP_InputField newShardKeyInput;
        [SerializeField] private TMP_InputField newShardDisplayNameInput;

        [Header("Buttons")]
        [SerializeField] private Button createWorldButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button backButton;

        [Header("Panels")]
        [SerializeField] private GameObject worldPanel;
        [SerializeField] private GameObject characterPanel;
        [SerializeField] private GameObject uiRoot;

        private readonly List<string> currentKeys = new();
        private bool startRequested;

        private void Awake()
        {
            if (createWorldButton != null) createWorldButton.onClick.AddListener(OnCreateWorldPressed);
            if (startGameButton != null) startGameButton.onClick.AddListener(OnStartGamePressed);
            if (backButton != null) backButton.onClick.AddListener(OnBackPressed);
        }

        private void OnDestroy()
        {
            if (createWorldButton != null) createWorldButton.onClick.RemoveListener(OnCreateWorldPressed);
            if (startGameButton != null) startGameButton.onClick.RemoveListener(OnStartGamePressed);
            if (backButton != null) backButton.onClick.RemoveListener(OnBackPressed);
        }

        private void OnEnable()
        {
            startRequested = false;
            if (startGameButton != null)
                startGameButton.interactable = true;

            RefreshList();
        }

        public void RefreshList()
        {
            currentKeys.Clear();

            IReadOnlyList<SaveFileInfo> shardSaves = SaveDiscoveryService.DiscoverShardSaves();
            for (int i = 0; i < shardSaves.Count; i++)
                currentKeys.Add(shardSaves[i].Key);

            MenuIndexData menuIndex = MenuIndexService.Load();
            for (int i = 0; i < menuIndex.shards.Count; i++)
            {
                string key = menuIndex.shards[i].key;
                if (string.IsNullOrWhiteSpace(key) || currentKeys.Contains(key))
                    continue;

                currentKeys.Add(key);
            }

            if (shardDropdown == null)
                return;

            shardDropdown.ClearOptions();
            shardDropdown.AddOptions(currentKeys.Count > 0 ? currentKeys : new List<string> { "<No Worlds>" });
            shardDropdown.value = 0;
            shardDropdown.RefreshShownValue();
        }

        public void OnCreateWorldPressed()
        {
            string key = newShardKeyInput != null ? newShardKeyInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                key = $"Shard_{System.DateTime.UtcNow.Ticks}";

            string displayName = newShardDisplayNameInput != null ? newShardDisplayNameInput.text : key;
            MenuIndexService.AddShard(key.Trim(), displayName);
            SessionSelectionState.SelectedShardKey = key.Trim();
            RefreshList();

            int index = currentKeys.FindIndex(k => k == SessionSelectionState.SelectedShardKey);
            if (index >= 0 && shardDropdown != null)
                shardDropdown.value = index;
        }

        public void OnStartGamePressed()
        {
            if (startRequested)
                return;

            if (currentKeys.Count == 0)
                return;

            int index = shardDropdown != null ? Mathf.Clamp(shardDropdown.value, 0, currentKeys.Count - 1) : 0;
            SessionSelectionState.SelectedShardKey = currentKeys[index];

            Bootstrapper bootstrapper = Bootstrapper.Instance != null ? Bootstrapper.Instance : FindFirstObjectByType<Bootstrapper>();
            if (bootstrapper != null)
            {
                startRequested = true;
                if (startGameButton != null)
                    startGameButton.interactable = false;

                // Hide world-select menu immediately so gameplay is not covered after load.
                if (worldPanel != null)
                    worldPanel.SetActive(false);
                else
                    gameObject.SetActive(false);

                // Show gameplay UI root if it was hidden for front-end menu flow.
                EnsureUiRootEnabled();

                bootstrapper.StartGameSession(SessionSelectionState.SelectedPlayerKey, SessionSelectionState.SelectedShardKey);
            }
        }

        public void OnBackPressed()
        {
            if (worldPanel != null) worldPanel.SetActive(false);
            if (characterPanel != null) characterPanel.SetActive(true);
        }

        private void EnsureUiRootEnabled()
        {
            if (uiRoot == null)
                uiRoot = FindSceneObjectByNameIncludingInactive("UIRoot");

            if (uiRoot != null)
                uiRoot.SetActive(true);
        }

        private static GameObject FindSceneObjectByNameIncludingInactive(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            // Includes inactive objects. Filter to scene instances only.
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null)
                    continue;

                if (!go.scene.IsValid())
                    continue;

                if (!string.Equals(go.name, objectName, System.StringComparison.Ordinal))
                    continue;

                return go;
            }

            return null;
        }
    }
}
