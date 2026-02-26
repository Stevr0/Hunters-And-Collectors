using System.Collections.Generic;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using TMPro;
using UnityEngine;

namespace HuntersAndCollectors.Inventory.UI
{
    public sealed class PlayerInventoryWindowUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Transform gridRoot;
        [SerializeField] private InventoryGridSlotUI slotPrefab;

        [Header("Item Resolver")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Grid")]
        [Tooltip("How many UI slots to show, regardless of snapshot size.")]
        [SerializeField] private int uiSlotCount = 48;

        private Inventory.PlayerInventoryNet currentInventoryNet;
        private readonly List<InventoryGridSlotUI> slotUIs = new();

        private bool gameplayLockHeld;
        private bool subscribedToSnapshots;

        // Polling signature (optional)
        private int lastRenderedNonEmptyCount = -1;
        private int lastRenderedSlotsLength = -1;

        private void Awake()
        {
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

            // Ensure our UI always has 48 slots.
            EnsureSlotUICount(uiSlotCount);

            ForceNextRender();
            TryRenderIfChanged();
        }

        private void OnDisable()
        {
            UnsubscribeFromInventorySnapshots();
            currentInventoryNet = null;

            lastRenderedNonEmptyCount = -1;
            lastRenderedSlotsLength = -1;

            if (gameplayLockHeld)
            {
                InputState.UnlockGameplay();
                gameplayLockHeld = false;
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromInventorySnapshots();
        }

        private void Update()
        {
            TryRenderIfChanged();
        }

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
                    SubscribeToInventorySnapshots();
                    Debug.Log($"[PlayerInventoryWindowUI] Bound to local PlayerInventoryNet. netId={inv.NetworkObjectId}");
                    ForceNextRender();
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

            // This signature still works, but note: UI count is fixed, snapshot may vary.
            if (slotsLen == lastRenderedSlotsLength && nonEmpty == lastRenderedNonEmptyCount)
                return;

            lastRenderedSlotsLength = slotsLen;
            lastRenderedNonEmptyCount = nonEmpty;

            Render(snapshot);
        }

        private void Render(InventorySnapshot snapshot)
        {
            if (gridRoot == null || slotPrefab == null)
                return;

            // Always keep 48 UI slots.
            EnsureSlotUICount(uiSlotCount);

            int snapshotCount = snapshot.Slots.Length;
            int renderCount = Mathf.Min(uiSlotCount, snapshotCount);

            // 1) Fill slots that exist in snapshot
            for (int i = 0; i < renderCount; i++)
            {
                var netSlot = snapshot.Slots[i];
                var uiSlot = slotUIs[i];
                uiSlot.SetSlotIndex(i);

                if (netSlot.IsEmpty)
                {
                    uiSlot.SetEmpty();
                    continue;
                }

                string itemId = netSlot.ItemId.ToString();
                int qty = netSlot.Quantity;

                Sprite icon = ResolveIcon(itemId);
                uiSlot.SetItem(itemId, icon, qty);
            }

            // 2) Clear remaining UI slots (if snapshot smaller than 48)
            for (int i = renderCount; i < uiSlotCount; i++)
            {
                slotUIs[i].SetEmpty();
            }
        }

        private void EnsureSlotUICount(int desiredCount)
        {
            // Create missing
            while (slotUIs.Count < desiredCount)
            {
                var ui = Instantiate(slotPrefab, gridRoot);
                slotUIs.Add(ui);
                ui.SetSlotIndex(slotUIs.Count - 1);
                ui.BindClick(OnSlotClicked);
                ui.SetEmpty(); // start empty

            }

            // Destroy extra (shouldn't happen unless you change uiSlotCount in inspector)
            while (slotUIs.Count > desiredCount)
            {
                int last = slotUIs.Count - 1;
                if (slotUIs[last] != null)
                    Destroy(slotUIs[last].gameObject);
                slotUIs.RemoveAt(last);
            }
        }

        private Sprite ResolveIcon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            if (itemDatabase == null)
                return null;

            if (itemDatabase.TryGet(itemId, out var def) && def != null)
                return def.Icon;

            return null;
        }

        private void OnSlotClicked(string itemId)
        {
            Debug.Log($"[Inventory] Clicked itemId={itemId}");
        }

        private void SubscribeToInventorySnapshots()
        {
            if (currentInventoryNet == null || subscribedToSnapshots)
                return;

            currentInventoryNet.OnSnapshotReceived += HandleInventorySnapshotReceived;
            subscribedToSnapshots = true;
        }

        private void UnsubscribeFromInventorySnapshots()
        {
            if (currentInventoryNet == null || !subscribedToSnapshots)
                return;

            currentInventoryNet.OnSnapshotReceived -= HandleInventorySnapshotReceived;
            subscribedToSnapshots = false;
        }

        private void HandleInventorySnapshotReceived(InventorySnapshot snapshot)
        {
            ForceNextRender();
            Render(snapshot);
        }

        private void ForceNextRender()
        {
            lastRenderedNonEmptyCount = -1;
            lastRenderedSlotsLength = -1;
        }
    }
}