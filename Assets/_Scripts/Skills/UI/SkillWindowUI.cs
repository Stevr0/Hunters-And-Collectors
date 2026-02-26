using System.Collections.Generic;
using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// SkillsWindowUI
    /// ---------------------------------------------------------
    /// Reads the local player's SkillsNet (replicated NetworkList)
    /// and builds a scrollable list of SkillRowUI entries.
    ///
    /// Key ideas:
    /// - SkillsNet is server-authoritative, clients only READ.
    /// - NetworkList raises OnListChanged on clients when server updates it.
    /// - We rebuild rows (simple + safe for MVP).
    /// </summary>
    public sealed class SkillsWindowUI : MonoBehaviour
    {
        [Header("Content + Prefab")]
        [Tooltip("Assign SkillsWindow/Panel/Scroll View/Viewport/Content")]
        [SerializeField] private Transform content;

        [Tooltip("Row prefab with SkillRowUI on it.")]
        [SerializeField] private SkillRowUI rowPrefab;

        // Cached SkillsNet for the local player
        private SkillsNet localSkills;

        // Keep created rows so we can destroy/rebuild cleanly
        private readonly List<GameObject> spawnedRows = new();

        private void OnEnable()
        {
            // Try immediately (works if player already spawned).
            TryBindToLocalPlayerSkills();

            // Also listen for player spawn events (covers cases where UI opens early).
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            // When we connect, the player object may spawn a bit later.
            // Try binding again.
            TryBindToLocalPlayerSkills();
        }

        private void TryBindToLocalPlayerSkills()
        {
            if (localSkills != null)
                return; // already bound

            if (NetworkManager.Singleton == null)
                return;

            // Local player NetworkObject (only valid after spawn)
            var playerObj = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            if (playerObj == null)
                return;

            localSkills = playerObj.GetComponent<SkillsNet>();
            if (localSkills == null)
            {
                Debug.LogWarning("[SkillsWindowUI] Local player has no SkillsNet component.");
                return;
            }

            // Subscribe to networked list changes
            localSkills.Skills.OnListChanged += OnSkillsListChanged;

            // Build initial UI from current list contents
            RebuildAllRows();
        }

        private void Unsubscribe()
        {
            if (localSkills != null)
            {
                localSkills.Skills.OnListChanged -= OnSkillsListChanged;
                localSkills = null;
            }

            ClearRows();
        }

        private void OnSkillsListChanged(NetworkListEvent<SkillEntry> changeEvent)
        {
            // MVP approach: rebuild everything.
            // Later you can optimize to update only the changed row.
            RebuildAllRows();
        }

        private void RebuildAllRows()
        {
            if (content == null || rowPrefab == null)
            {
                Debug.LogWarning("[SkillsWindowUI] Missing references (content or rowPrefab).");
                return;
            }

            if (localSkills == null)
                return;

            ClearRows();

            // Create one row per skill entry in the NetworkList
            foreach (var skill in localSkills.Skills)
            {
                // FixedString -> normal string
                string id = skill.Id.ToString();

                // XP curve: 10 * (level + 1)
                int xpToNext = 10 * (skill.Level + 1);

                var row = Instantiate(rowPrefab, content);
                row.Set(id, skill.Level, skill.Xp, xpToNext);

                spawnedRows.Add(row.gameObject);
            }
        }
        public void Toggle()
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }
        private void ClearRows()
        {
            for (int i = 0; i < spawnedRows.Count; i++)
            {
                if (spawnedRows[i] != null)
                    Destroy(spawnedRows[i]);
            }
            spawnedRows.Clear();
        }
    }
}