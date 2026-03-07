using System.Collections.Generic;
using HuntersAndCollectors.Building;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.UI;
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

        [Header("Placement UX")]
        [Tooltip("Max time between two clicks on the same slot/item to count as a double-click.")]
        [SerializeField] private float doubleClickThresholdSeconds = 0.3f;

        [Tooltip("Inventory window alpha while placement mode is active.")]
        [Range(0f, 1f)]
        [SerializeField] private float placementModeWindowAlpha = 0.12f;

        private Inventory.PlayerInventoryNet currentInventoryNet;
        private PlayerVitalsNet currentVitals;
        private BuildPlacementController currentPlacementController;

        private readonly List<InventoryGridSlotUI> slotUIs = new();

        private bool gameplayLockHeld;
        private bool subscribedToSnapshots;
        private bool subscribedToPlacementMode;

        private int lastRenderedSignature = int.MinValue;

        private CanvasGroup windowCanvasGroup;
        private bool placementUiDimApplied;
        private float cachedWindowAlpha = 1f;
        private bool cachedWindowInteractable = true;
        private bool cachedWindowBlocksRaycasts = true;

        private int lastClickedSlotIndex = -1;
        private string lastClickedItemId = string.Empty;
        private float lastClickTimeUnscaled = -999f;

        private void Awake()
        {
            if (titleText)
                titleText.text = "Inventory";

            windowCanvasGroup = GetComponent<CanvasGroup>();
            if (windowCanvasGroup == null)
                windowCanvasGroup = gameObject.AddComponent<CanvasGroup>();
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
            TryBindToLocalPlacementController();

            EnsureSlotUICount(uiSlotCount);
            ForceNextRender();
            TryRenderIfChanged();
        }

        private void OnDisable()
        {
            UnsubscribeFromInventorySnapshots();
            UnsubscribeFromPlacementMode();

            currentInventoryNet = null;
            currentVitals = null;
            currentPlacementController = null;

            RestoreWindowFromPlacementMode();
            ClearDoubleClickState();

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
            UnsubscribeFromPlacementMode();
        }

        private void Update()
        {
            TryRenderIfChanged();
            TryBindToLocalPlacementController();
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
                    currentVitals = inv.GetComponent<PlayerVitalsNet>();
                    SubscribeToInventorySnapshots();
                    Debug.Log($"[PlayerInventoryWindowUI] Bound to local PlayerInventoryNet. netId={inv.NetworkObjectId}");
                    ForceNextRender();
                    return;
                }
            }
        }

        private void TryBindToLocalPlacementController()
        {
            if (currentPlacementController != null && currentPlacementController.IsLocalOwner)
                return;

            BuildPlacementController[] found = FindObjectsOfType<BuildPlacementController>(true);
            for (int i = 0; i < found.Length; i++)
            {
                BuildPlacementController candidate = found[i];
                if (candidate == null || !candidate.IsLocalOwner)
                    continue;

                UnsubscribeFromPlacementMode();
                currentPlacementController = candidate;
                SubscribeToPlacementMode();
                return;
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
                    hash = hash * 31 + s.BonusStrength;
                    hash = hash * 31 + s.BonusDexterity;
                    hash = hash * 31 + s.BonusIntelligence;
                    hash = hash * 31 + s.CraftedBy.GetHashCode();
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

                if (maxDurability <= 0 && itemDatabase != null && itemDatabase.TryGet(itemId, out var defFromDb) && defFromDb != null)
                {
                    maxDurability = Mathf.Max(0, defFromDb.MaxDurability);
                    if (maxDurability > 0 && durability <= 0)
                        durability = maxDurability;
                }

                Sprite icon = ResolveIcon(itemId);
                ItemTooltipData tooltip = BuildTooltipData(itemId, netSlot, durability);
                uiSlot.SetItem(itemId, icon, qty, durability, maxDurability, tooltip);
            }

            for (int i = renderCount; i < uiSlotCount; i++)
                slotUIs[i].SetEmpty();
        }

        private ItemTooltipData BuildTooltipData(string itemId, InventorySnapshot.SlotDto slot, int durability)
        {
            ItemTooltipData data = new ItemTooltipData
            {
                ItemId = itemId,
                BonusStrength = slot.BonusStrength,
                BonusDexterity = slot.BonusDexterity,
                BonusIntelligence = slot.BonusIntelligence,
                Durability = durability,
                CraftedBy = slot.CraftedBy.ToString()
            };

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out var def) && def != null)
            {
                data.DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.ItemId : def.DisplayName;
                data.Description = def.Description;
                data.Damage = def.Damage;
                data.Defence = def.Defence;
                data.AttackBonus = def.AttackBonus;
                data.SwingSpeed = def.SwingSpeed;
                data.MoveSpeed = def.MovementSpeed;

                data.Strength = Mathf.Max(0, def.Strength) + slot.BonusStrength;
                data.Dexterity = Mathf.Max(0, def.Dexterity) + slot.BonusDexterity;
                data.Intelligence = Mathf.Max(0, def.Intelligence) + slot.BonusIntelligence;
            }

            return data;
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

        private void OnSlotClicked(int slotIndex, string itemId, int clickCount)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            bool isDoubleClick = IsSameSlotItemDoubleClick(slotIndex, itemId);
            if (!isDoubleClick)
            {
                Debug.Log($"[Inventory] Clicked slot={slotIndex} itemId={itemId}");
                return;
            }

            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (itemDatabase == null || !itemDatabase.TryGet(itemId, out var def) || def == null)
                return;

            if (def.IsPlaceable)
            {
                TryBindToLocalPlacementController();
                if (currentPlacementController == null)
                {
                    Debug.LogWarning("[Inventory] Double-click placeable detected but no local BuildPlacementController found.");
                    return;
                }

                Debug.Log($"[Inventory] Double-click detected on placeable itemId={itemId}. Entering placement mode.");
                currentPlacementController.BeginPlacement(def);
                return;
            }

            // Existing behavior preserved for non-placeable food items.
            if (currentVitals != null && def.IsFood)
                currentVitals.TryConsumeFoodFromInventorySlot(slotIndex);
        }

        private bool IsSameSlotItemDoubleClick(int slotIndex, string itemId)
        {
            float now = Time.unscaledTime;

            bool sameTarget = lastClickedSlotIndex == slotIndex &&
                              string.Equals(lastClickedItemId, itemId, System.StringComparison.Ordinal);

            bool withinThreshold = sameTarget && (now - lastClickTimeUnscaled) <= Mathf.Max(0.1f, doubleClickThresholdSeconds);

            lastClickedSlotIndex = slotIndex;
            lastClickedItemId = itemId;
            lastClickTimeUnscaled = now;

            return withinThreshold;
        }

        private void ClearDoubleClickState()
        {
            lastClickedSlotIndex = -1;
            lastClickedItemId = string.Empty;
            lastClickTimeUnscaled = -999f;
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

        private void SubscribeToPlacementMode()
        {
            if (currentPlacementController == null || subscribedToPlacementMode)
                return;

            currentPlacementController.PlacementModeChanged += HandlePlacementModeChanged;
            subscribedToPlacementMode = true;
        }

        private void UnsubscribeFromPlacementMode()
        {
            if (currentPlacementController == null || !subscribedToPlacementMode)
                return;

            currentPlacementController.PlacementModeChanged -= HandlePlacementModeChanged;
            subscribedToPlacementMode = false;
        }

        private void HandlePlacementModeChanged(bool isActive)
        {
            if (isActive)
                ApplyWindowPlacementMode();
            else
                RestoreWindowFromPlacementMode();
        }

        private void ApplyWindowPlacementMode()
        {
            if (windowCanvasGroup == null || placementUiDimApplied)
                return;

            cachedWindowAlpha = windowCanvasGroup.alpha;
            cachedWindowInteractable = windowCanvasGroup.interactable;
            cachedWindowBlocksRaycasts = windowCanvasGroup.blocksRaycasts;

            windowCanvasGroup.alpha = Mathf.Clamp01(placementModeWindowAlpha);
            windowCanvasGroup.interactable = false;
            windowCanvasGroup.blocksRaycasts = false;

            placementUiDimApplied = true;
        }

        private void RestoreWindowFromPlacementMode()
        {
            if (windowCanvasGroup == null || !placementUiDimApplied)
                return;

            windowCanvasGroup.alpha = cachedWindowAlpha;
            windowCanvasGroup.interactable = cachedWindowInteractable;
            windowCanvasGroup.blocksRaycasts = cachedWindowBlocksRaycasts;

            placementUiDimApplied = false;
            Debug.Log("[Inventory] Placement mode exited. Inventory visibility/interactions restored.");
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
