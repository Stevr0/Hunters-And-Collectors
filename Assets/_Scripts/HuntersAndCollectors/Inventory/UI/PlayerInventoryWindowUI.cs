using System.Collections.Generic;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Networking.DTO;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// PlayerInventoryWindowUI
    /// --------------------------------------------------------------------
    /// MVP inventory window that renders the LOCAL PLAYER's inventory snapshot.
    ///
    /// Design goals (MVP):
    /// - Read-only list view (name + qty) so vendor checkout can "feel real".
    /// - Uses the same snapshot pattern as VendorWindowUI:
    ///     - Subscribe to OnSnapshotChanged
    ///     - Render LastSnapshot immediately if available
    /// - Locks gameplay input while open so mouse can interact with UI.
    ///
    /// Later upgrades:
    /// - Grid layout (W x H)
    /// - Drag/drop between inventory & vendor cart
    /// - Tooltips, splitting stacks, etc.
    /// </summary>
    public sealed class PlayerInventoryWindowUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Tooltip("Parent for row instances (ScrollView/Viewport/Content).")]
        [SerializeField] private Transform contentRoot;

        [Tooltip("Row prefab (InventoryRowUI).")]
        [SerializeField] private InventoryRowUI rowPrefab;

        [Header("Optional")]
        [Tooltip("If assigned, used to convert ItemId -> DisplayName. If null, we show ItemId.")]
        [SerializeField] private Vendors.VendorChestNet itemNameResolver; // reuse your ItemDatabase access via VendorChestNet.GetDisplayName()

        // The local player's inventory net component we are currently bound to.
        private PlayerInventoryNet currentInventoryNet;

        // Spawned row instances we created (so we can destroy/clear on refresh).
        private readonly List<InventoryRowUI> rows = new();

        private bool gameplayLockHeld;

        private void Awake()
        {
            if (closeButton)
                closeButton.onClick.AddListener(Close);

            if (titleText)
                titleText.text = "Inventory";

            // Start hidden by default (common for windows)
            // gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            // Lock gameplay so mouse is free for UI.
            if (!gameplayLockHeld)
            {
                InputState.LockGameplay();
                gameplayLockHeld = true;
            }

            // Bind (or re-bind) when the window opens.
            // This makes the window resilient if the player spawns after the UI exists.
            TryBindToLocalPlayerInventory();

            // If we have cached data, render immediately so it doesn't feel "empty" for a frame.
            if (currentInventoryNet != null && currentInventoryNet.LastSnapshot.Slots != null)
                Render(currentInventoryNet.LastSnapshot);
        }

        private void OnDisable()
        {
            // Unsubscribe from snapshot event.
            if (currentInventoryNet != null)
                currentInventoryNet.OnSnapshotChanged -= HandleSnapshotChanged;

            currentInventoryNet = null;

            ClearRows();

            // Unlock gameplay when closing.
            if (gameplayLockHeld)
            {
                InputState.UnlockGameplay();
                gameplayLockHeld = false;
            }
        }

        /// <summary>
        /// Toggle helper you can call from a hotkey (e.g., I key).
        /// </summary>
        public void Toggle()
        {
            gameObject.SetActive(!gameObject.activeSelf);
        }

        public void Open()
        {
            gameObject.SetActive(true);
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Finds the LOCAL player's PlayerInventoryNet and subscribes to snapshot updates.
        /// </summary>
        private void TryBindToLocalPlayerInventory()
        {
            // Avoid double-binding if already bound.
            if (currentInventoryNet != null)
                return;

            // MVP approach: find all PlayerInventoryNet and choose the one owned by us.
            // This works because PlayerInventoryNet is a NetworkBehaviour living on the player object.
            var all = FindObjectsOfType<PlayerInventoryNet>(true);

            for (int i = 0; i < all.Length; i++)
            {
                var inv = all[i];
                if (inv == null) continue;

                // We only want the local owner's inventory component.
                if (!inv.IsOwner) continue;

                currentInventoryNet = inv;
                break;
            }

            if (currentInventoryNet == null)
            {
                Debug.LogWarning("[PlayerInventoryWindowUI] Could not find local PlayerInventoryNet yet (player may not be spawned).");
                return;
            }

            // Subscribe exactly once.
            currentInventoryNet.OnSnapshotChanged -= HandleSnapshotChanged;
            currentInventoryNet.OnSnapshotChanged += HandleSnapshotChanged;

            Debug.Log($"[PlayerInventoryWindowUI] Bound to local inventory. netId={currentInventoryNet.NetworkObjectId} ownerClientId={currentInventoryNet.OwnerClientId}");
        }

        private void HandleSnapshotChanged(InventorySnapshot snapshot)
        {
            Render(snapshot);
        }

        private void Render(InventorySnapshot snapshot)
        {
            if (contentRoot == null || rowPrefab == null)
                return;

            ClearRows();

            if (snapshot.Slots == null)
                return;

            // Simple list: show only non-empty slots.
            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                var slot = snapshot.Slots[i];
                if (slot.IsEmpty)
                    continue;

                string itemId = slot.ItemId.ToString();
                int qty = slot.Quantity;

                // Resolve friendly display name if we can.
                // Reusing VendorChestNet.GetDisplayName() is fine for MVP.
                string displayName = itemNameResolver != null
                    ? itemNameResolver.GetDisplayName(itemId)
                    : itemId;

                var row = Instantiate(rowPrefab, contentRoot);
                rows.Add(row);

                row.Bind(displayName, qty);
            }
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
