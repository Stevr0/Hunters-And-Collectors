using HuntersAndCollectors.Persistence;
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI.Menu
{
    public sealed class ManageSavesUI : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown domainDropdown;
        [SerializeField] private TMP_Dropdown fileDropdown;
        [SerializeField] private TMP_Text detailsText;
        [SerializeField] private TMP_InputField deleteConfirmInput;

        [Header("Buttons")]
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button backupButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button backButton;

        [Header("Panels")]
        [SerializeField] private GameObject managePanel;
        [SerializeField] private GameObject mainMenuPanel;

        private readonly List<SaveFileInfo> currentFiles = new();

        private void Awake()
        {
            if (refreshButton != null) refreshButton.onClick.AddListener(Refresh);
            if (backupButton != null) backupButton.onClick.AddListener(OnBackupPressed);
            if (deleteButton != null) deleteButton.onClick.AddListener(OnDeletePressed);
            if (backButton != null) backButton.onClick.AddListener(OnBackPressed);

            if (domainDropdown != null) domainDropdown.onValueChanged.AddListener(OnDomainDropdownChanged);
            if (fileDropdown != null) fileDropdown.onValueChanged.AddListener(OnFileDropdownChanged);
        }

        private void OnDestroy()
        {
            if (refreshButton != null) refreshButton.onClick.RemoveListener(Refresh);
            if (backupButton != null) backupButton.onClick.RemoveListener(OnBackupPressed);
            if (deleteButton != null) deleteButton.onClick.RemoveListener(OnDeletePressed);
            if (backButton != null) backButton.onClick.RemoveListener(OnBackPressed);

            if (domainDropdown != null) domainDropdown.onValueChanged.RemoveListener(OnDomainDropdownChanged);
            if (fileDropdown != null) fileDropdown.onValueChanged.RemoveListener(OnFileDropdownChanged);
        }

        private void OnDomainDropdownChanged(int _)
        {
            Refresh();
        }

        private void OnFileDropdownChanged(int _)
        {
            UpdateDetails();
        }

        private void OnEnable()
        {
            Refresh();
        }

        public void Refresh()
        {
            bool playersDomain = domainDropdown == null || domainDropdown.value == 0;
            IReadOnlyList<SaveFileInfo> discovered = playersDomain
                ? SaveDiscoveryService.DiscoverPlayerSaves()
                : SaveDiscoveryService.DiscoverShardSaves();

            currentFiles.Clear();
            currentFiles.AddRange(discovered);

            if (fileDropdown != null)
            {
                fileDropdown.ClearOptions();
                var labels = new List<string>();
                for (int i = 0; i < currentFiles.Count; i++)
                    labels.Add(currentFiles[i].Key);

                if (labels.Count == 0)
                    labels.Add("<None>");

                fileDropdown.AddOptions(labels);
                fileDropdown.value = 0;
                fileDropdown.RefreshShownValue();
            }

            UpdateDetails();
        }

        public void UpdateDetails()
        {
            if (detailsText == null)
                return;

            if (currentFiles.Count == 0)
            {
                detailsText.text = "No save files found.";
                return;
            }

            int index = fileDropdown != null ? Mathf.Clamp(fileDropdown.value, 0, currentFiles.Count - 1) : 0;
            SaveFileInfo info = currentFiles[index];
            detailsText.text =
                $"Key: {info.Key}\n" +
                $"Path: {info.FilePath}\n" +
                $"Last Modified (UTC): {info.LastModifiedUtc:yyyy-MM-dd HH:mm:ss}\n" +
                $"Schema: {info.SchemaVersion}\n" +
                $"Corrupted: {info.IsCorrupted}\n" +
                $"Status: {info.ParseStatus}";
        }

        public void OnBackupPressed()
        {
            if (currentFiles.Count == 0)
                return;

            SaveFileInfo info = currentFiles[Mathf.Clamp(fileDropdown.value, 0, currentFiles.Count - 1)];
            if (SaveDiscoveryService.BackupSaveFile(info.FilePath, out string backupPath, out string error))
                Debug.Log($"[ManageSavesUI] Backup created: {backupPath}");
            else
                Debug.LogError($"[ManageSavesUI] Backup failed: {error}");

            Refresh();
        }

        public void OnDeletePressed()
        {
            if (currentFiles.Count == 0)
                return;

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.LogWarning("[ManageSavesUI] Delete blocked while a live network session is running.");
                return;
            }

            string confirm = deleteConfirmInput != null ? deleteConfirmInput.text : string.Empty;
            if (!string.Equals(confirm, "DELETE", StringComparison.Ordinal))
            {
                Debug.LogWarning("[ManageSavesUI] Delete confirmation mismatch. Type DELETE to confirm.");
                return;
            }

            SaveFileInfo info = currentFiles[Mathf.Clamp(fileDropdown.value, 0, currentFiles.Count - 1)];
            if (SaveDiscoveryService.DeleteSaveFile(info.FilePath, out string error))
                Debug.Log($"[ManageSavesUI] Deleted save: {info.FilePath}");
            else
                Debug.LogError($"[ManageSavesUI] Delete failed: {error}");

            if (deleteConfirmInput != null)
                deleteConfirmInput.text = string.Empty;

            Refresh();
        }

        public void OnBackPressed()
        {
            if (managePanel != null) managePanel.SetActive(false);
            if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        }
    }
}
