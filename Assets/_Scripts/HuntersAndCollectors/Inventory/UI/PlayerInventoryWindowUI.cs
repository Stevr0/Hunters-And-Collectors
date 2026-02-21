using System.Collections.Generic;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Items;              // ItemDatabase lives here in your project
using HuntersAndCollectors.Networking.DTO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Inventory.UI
{
    public sealed class PlayerInventoryWindowUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private InventoryRowUI rowPrefab;

        [Header("Item Name Resolver")]
        [Tooltip("Used to map ItemId -> DisplayName. If null, ItemId is shown.")]
        [SerializeField] private ItemDatabase itemDatabase;

        private Inventory.PlayerInventoryNet currentInventoryNet;
        private readonly List<InventoryRowUI> rows = new();

        private bool gameplayLockHeld;

        // If you're using polling version, keep your signature fields here.
        private int lastRenderedNonEmptyCount = -1;
        private int lastRenderedSlotsLength = -1;

        private void Awake()
        {
            if (closeButton)
                closeButton.onClick.AddListener(Close);

            if (titleText)
                titleText.text = "Inventory";
        }

        private void OnEnable()
        {
            if (!gameplayLockHeld)
            {
                InputState.LockGameplay();
                gameplayLockHeld = true;
            }

            TryBindToLocalPlayerInventory();
            TryRenderIfChanged();
        }

        private void OnDisable()
        {
            currentInventoryNet = null;
            ClearRows();

            lastRenderedNonEmptyCount = -1;
            lastRenderedSlotsLength = -1;

            if (gameplayLockHeld)
            {
                InputState.UnlockGameplay();
                gameplayLockHeld = false;
            }
        }

        private void Update()
        {
            // Polling MVP. If you later add OnSnapshotChanged, you can remove Update().
            TryRenderIfChanged();
        }

        public void Open() => gameObject.SetActive(true);
        public void Close() => gameObject.SetActive(false);
        public void Toggle() => gameObject.SetActive(!gameObject.activeSelf);

        private void TryBindToLocalPlayerInventory()
        {
            if (currentInventoryNet != null)
                return;

            var all = FindObjectsOfType<Inventory.PlayerInventoryNet>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var inv = all[i];
                if (inv != null && inv.IsOwner)
                {
                    currentInventoryNet = inv;
                    Debug.Log($"[PlayerInventoryWindowUI] Bound to local PlayerInventoryNet. netId={inv.NetworkObjectId}");
                    return;
                }
            }
        }

        private void TryRenderIfChanged()
        {
            if (currentInventoryNet == null)
                return;

            var snapshot = currentInventoryNet.LastSnapshot;
            if (snapshot.Slots == null)
                return;

            int slotsLen = snapshot.Slots.Length;

            int nonEmpty = 0;
            for (int i = 0; i < slotsLen; i++)
                if (!snapshot.Slots[i].IsEmpty) nonEmpty++;

            if (slotsLen == lastRenderedSlotsLength && nonEmpty == lastRenderedNonEmptyCount)
                return;

            lastRenderedSlotsLength = slotsLen;
            lastRenderedNonEmptyCount = nonEmpty;

            Render(snapshot);
        }

        private void Render(InventorySnapshot snapshot)
        {
            if (contentRoot == null || rowPrefab == null)
                return;

            ClearRows();

            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                var slot = snapshot.Slots[i];
                if (slot.IsEmpty)
                    continue;

                string itemId = slot.ItemId.ToString();
                int qty = slot.Quantity;

                // Resolve display name using ItemDatabase.
                string displayName = GetDisplayName(itemId);

                var row = Instantiate(rowPrefab, contentRoot);
                rows.Add(row);

                row.Bind(displayName, qty);
            }
        }

        /// <summary>
        /// Converts an itemId into a friendly display name using the ItemDatabase.
        /// Falls back to itemId if database is missing or item is unknown.
        /// </summary>
        private string GetDisplayName(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return string.Empty;

            if (itemDatabase == null)
                return itemId;

            // Uses your existing ItemDatabase API (same pattern as VendorChestNet).
            if (itemDatabase.TryGet(itemId, out var def) && def != null)
                return def.DisplayName;

            return itemId;
        }

        private void ClearRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i] != null)
                    Destroy(rows[i].gameObject);
            }
            rows.Clear();
        }
    }
}
