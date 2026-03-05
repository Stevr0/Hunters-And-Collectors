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

        private int lastRenderedSignature = int.MinValue;

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

            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            TryBindToLocalPlayerInventory();
            EnsureSlotUICount(uiSlotCount);
            ForceNextRender();
            TryRenderIfChanged();
        }

        private void OnDisable()
        {
            UnsubscribeFromInventorySnapshots();
            currentInventoryNet = null;

            lastRenderedSignature = int.MinValue;

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

            int signature = ComputeSnapshotSignature(snapshot);
            if (signature == lastRenderedSignature)
                return;

            lastRenderedSignature = signature;
            Render(snapshot);
        }

        private static int ComputeSnapshotSignature(InventorySnapshot snapshot)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + snapshot.W;
                hash = hash * 31 + snapshot.H;
                hash = hash * 31 + (snapshot.Slots == null ? 0 : snapshot.Slots.Length);

                if (snapshot.Slots == null)
                    return hash;

                for (int i = 0; i < snapshot.Slots.Length; i++)
                {
                    var s = snapshot.Slots[i];
                    hash = hash * 31 + (s.IsEmpty ? 1 : 0);
                    hash = hash * 31 + s.Quantity;
                    hash = hash * 31 + s.ItemId.GetHashCode();
                    hash = hash * 31 + s.Durability;
                    hash = hash * 31 + s.MaxDurability;
                }

                return hash;
            }
        }

        private void Render(InventorySnapshot snapshot)
        {
            if (gridRoot == null || slotPrefab == null)
                return;

            EnsureSlotUICount(uiSlotCount);

            int snapshotCount = snapshot.Slots.Length;
            int renderCount = Mathf.Min(uiSlotCount, snapshotCount);

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

                int maxDurability = netSlot.MaxDurability;
                int durability = netSlot.Durability;

                // Backward-safe fallback if an older snapshot source omitted max durability.
                if (maxDurability <= 0 && itemDatabase != null && itemDatabase.TryGet(itemId, out var def) && def != null)
                {
                    maxDurability = Mathf.Max(0, def.MaxDurability);
                    if (maxDurability > 0 && durability <= 0)
                        durability = maxDurability;
                }

                Sprite icon = ResolveIcon(itemId);
                uiSlot.SetItem(itemId, icon, qty, durability, maxDurability);
            }

            for (int i = renderCount; i < uiSlotCount; i++)
                slotUIs[i].SetEmpty();
        }

        private void EnsureSlotUICount(int desiredCount)
        {
            while (slotUIs.Count < desiredCount)
            {
                var ui = Instantiate(slotPrefab, gridRoot);
                slotUIs.Add(ui);
                ui.SetSlotIndex(slotUIs.Count - 1);
                ui.BindClick(OnSlotClicked);
                ui.SetEmpty();
            }

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
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out var def) && def != null)
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
            currentInventoryNet.OnSnapshotChanged += HandleInventorySnapshotReceived;
            subscribedToSnapshots = true;
        }

        private void UnsubscribeFromInventorySnapshots()
        {
            if (currentInventoryNet == null || !subscribedToSnapshots)
                return;

            currentInventoryNet.OnSnapshotReceived -= HandleInventorySnapshotReceived;
            currentInventoryNet.OnSnapshotChanged -= HandleInventorySnapshotReceived;
            subscribedToSnapshots = false;
        }

        private void HandleInventorySnapshotReceived(InventorySnapshot snapshot)
        {
            ForceNextRender();
            Render(snapshot);
        }

        private void ForceNextRender()
        {
            lastRenderedSignature = int.MinValue;
        }
    }
}

