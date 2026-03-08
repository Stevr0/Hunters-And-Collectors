using System;
using System.Collections.Generic;
using HuntersAndCollectors.Building;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// Snapshot-driven unified player inventory presenter.
    ///
    /// Authoritative model:
    /// - Server owns inventory state.
    /// - Clients render snapshots and send action requests only.
    ///
    /// Valheim-style presentation:
    /// - Row 0 (slots 0..7) is always visible (hotbar).
    /// - Rows 1..3 (slots 8..31) are grouped under ExpandedRowsRoot and toggled.
    /// </summary>
    public sealed class PlayerInventoryWindowUI : MonoBehaviour
    {
        private const int HotbarSlotCount = 8;
        private const int InventoryColumns = 8;
        private const int InventoryRows = 4;
        private const int TotalInventorySlots = InventoryColumns * InventoryRows;

        [Header("UI References")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Transform gridRoot;
        [SerializeField] private InventoryGridSlotUI slotPrefab;

        [Header("Row Roots (Optional but recommended)")]
        [Tooltip("Persistent hotbar row root (slots 0..7). This should stay visible at all times.")]
        [SerializeField] private Transform row0Root;

        [Tooltip("Container for rows 1..3 (slots 8..31). This gets toggled by expanded state.")]
        [SerializeField] private Transform expandedRowsRoot;

        [Tooltip("Optional explicit row root for slots 8..15 (row 1).")]
        [SerializeField] private Transform inventoryGridRow1Root;

        [Tooltip("Optional explicit row root for slots 16..23 (row 2).")]
        [SerializeField] private Transform inventoryGridRow2Root;

        [Tooltip("Optional explicit row root for slots 24..31 (row 3).")]
        [SerializeField] private Transform inventoryGridRow3Root;

        [Header("Item Resolver")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Grid")]
        [Tooltip("How many UI slots to instantiate at minimum. Default should match authoritative size (32).")]
        [SerializeField] private int uiSlotCount = TotalInventorySlots;

        [Header("Presentation")]
        [Tooltip("If true, rows 1..3 are shown on enable. Row 0 is always visible.")]
        [SerializeField] private bool startExpanded;

        [Tooltip("Allow right-click activation on row 0 while collapsed.")]
        [SerializeField] private bool allowRightClickHotbarWhenCollapsed = true;

        [Header("Placement UX")]
        [Tooltip("Inventory window alpha while placement mode is active.")]
        [Range(0f, 1f)]
        [SerializeField] private float placementModeWindowAlpha = 0.12f;

        private PlayerInventoryNet currentInventoryNet;
        private PlayerEquipmentNet currentEquipmentNet;
        private PlayerVitalsNet currentVitals;
        private BuildPlacementController currentPlacementController;

        private readonly List<InventoryGridSlotUI> slotUIs = new();

        private bool gameplayLockHeld;
        private bool subscribedToSnapshots;
        private bool subscribedToPlacementMode;
        private bool isExpanded;

        private int lastRenderedSignature = int.MinValue;

        private CanvasGroup windowCanvasGroup;
        private bool placementUiDimApplied;
        private float cachedWindowAlpha = 1f;
        private bool cachedWindowInteractable = true;
        private bool cachedWindowBlocksRaycasts = true;

        public event Action<bool> ExpandedChanged;

        private void Awake()
        {
            if (titleText)
                titleText.text = "Inventory";

            if (gridRoot == null)
                gridRoot = transform;

            windowCanvasGroup = GetComponent<CanvasGroup>();
            if (windowCanvasGroup == null)
                windowCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            TryBindToLocalPlayerInventory();
            TryBindToLocalPlacementController();

            EnsureSlotUICount(GetClampedUiSlotCount());
            SetExpanded(startExpanded);
            ForceNextRender();
            TryRenderIfChanged();
        }

        private void OnDisable()
        {
            UnsubscribeFromInventorySnapshots();
            UnsubscribeFromPlacementMode();

            currentInventoryNet = null;
            currentEquipmentNet = null;
            currentVitals = null;
            currentPlacementController = null;

            RestoreWindowFromPlacementMode();
            ReleaseGameplayLockIfHeld();

            lastRenderedSignature = int.MinValue;
        }

        private void OnDestroy()
        {
            UnsubscribeFromInventorySnapshots();
            UnsubscribeFromPlacementMode();
            ReleaseGameplayLockIfHeld();
        }

        private void Update()
        {
            TryBindToLocalPlayerInventory();
            TryBindToLocalPlacementController();
            TryRenderIfChanged();

            if (currentInventoryNet == null || !currentInventoryNet.IsOwner)
                return;

            TryHandleHotbarActivationInput();
        }

        /// <summary>
        /// True when rows 1..3 are currently visible.
        /// Row 0 remains visible regardless of this state.
        /// </summary>
        public bool IsInventoryExpanded() => isExpanded;

        /// <summary>
        /// Alias helper requested for simpler external use.
        /// </summary>
        public bool IsExpanded() => IsInventoryExpanded();

        public void SetExpanded(bool expanded)
        {
            if (isExpanded == expanded)
            {
                ApplyExpandedInteractionLock();
                ApplySlotPresentationForCurrentMode();
                return;
            }

            isExpanded = expanded;
            ApplyExpandedInteractionLock();
            ApplySlotPresentationForCurrentMode();
            ExpandedChanged?.Invoke(isExpanded);
        }

        public void ToggleExpanded()
        {
            SetExpanded(!isExpanded);
        }

        public bool IsHotbarSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < HotbarSlotCount;
        }

        private bool IsLowerInventorySlot(int slotIndex)
        {
            return slotIndex >= HotbarSlotCount && slotIndex < TotalInventorySlots;
        }

        public bool CanStartDragCurrentMode()
        {
            // Drag is disabled while collapsed.
            return IsInventoryExpanded();
        }

        /// <summary>
        /// Alias helper requested for simpler external use.
        /// </summary>
        public bool CanDragInCurrentState() => CanStartDragCurrentMode();

        public bool GetHotbarSlotIndexFromKey(Key key, out int slotIndex)
        {
            slotIndex = -1;
            switch (key)
            {
                case Key.Digit1:
                case Key.Numpad1: slotIndex = 0; return true;
                case Key.Digit2:
                case Key.Numpad2: slotIndex = 1; return true;
                case Key.Digit3:
                case Key.Numpad3: slotIndex = 2; return true;
                case Key.Digit4:
                case Key.Numpad4: slotIndex = 3; return true;
                case Key.Digit5:
                case Key.Numpad5: slotIndex = 4; return true;
                case Key.Digit6:
                case Key.Numpad6: slotIndex = 5; return true;
                case Key.Digit7:
                case Key.Numpad7: slotIndex = 6; return true;
                case Key.Digit8:
                case Key.Numpad8: slotIndex = 7; return true;
                default: return false;
            }
        }

        /// <summary>
        /// Alias helper requested in task wording.
        /// Returns -1 when key is not a hotbar number key.
        /// </summary>
        public int GetHotbarSlotIndexFromNumberKey(Key key)
        {
            return GetHotbarSlotIndexFromKey(key, out int slotIndex) ? slotIndex : -1;
        }

        /// <summary>
        /// Attempts to activate slot using latest replicated snapshot.
        /// No client-side authoritative mutation is performed.
        /// </summary>
        public bool TryActivateSlot(int slotIndex)
        {
            if (currentInventoryNet == null || !currentInventoryNet.IsOwner)
                return false;

            InventorySnapshot snapshot = currentInventoryNet.LastSnapshot;
            if (snapshot.Slots == null)
                return false;

            if (slotIndex < 0 || slotIndex >= snapshot.Slots.Length)
                return false;

            InventorySnapshot.SlotDto slot = snapshot.Slots[slotIndex];
            if (slot.IsEmpty || slot.Quantity <= 0)
                return false;

            string itemId = slot.ItemId.ToString();
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            return TryActivateItemInternal(itemId, slotIndex);
        }

        public bool TryActivateItem(string itemId)
        {
            if (currentInventoryNet == null || !currentInventoryNet.IsOwner)
                return false;

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            InventorySnapshot snapshot = currentInventoryNet.LastSnapshot;
            if (snapshot.Slots == null)
                return false;

            string canonical = itemId.Trim();
            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                InventorySnapshot.SlotDto slot = snapshot.Slots[i];
                if (slot.IsEmpty || slot.Quantity <= 0)
                    continue;

                if (!string.Equals(slot.ItemId.ToString(), canonical, StringComparison.Ordinal))
                    continue;

                return TryActivateItemInternal(canonical, i);
            }

            return false;
        }

        private bool TryActivateItemInternal(string itemId, int slotIndex)
        {
            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (itemDatabase == null || !itemDatabase.TryGet(itemId, out ItemDef def) || def == null)
                return false;

            // Equippable route: existing authoritative equipment RPC.
            if (def.IsEquippable)
            {
                if (currentEquipmentNet == null || !currentEquipmentNet.IsOwner)
                    return false;

                currentEquipmentNet.RequestEquipFromInventorySlotServerRpc(slotIndex);
                return true;
            }

            // Consumable route: existing authoritative consume path.
            if (def.IsFood)
            {
                if (currentVitals == null)
                    return false;

                currentVitals.TryConsumeFoodFromInventorySlot(slotIndex);
                return true;
            }

            // Placeable route: client enters placement UX, server still validates consume/spawn.
            if (def.IsPlaceable)
            {
                TryBindToLocalPlacementController();
                if (currentPlacementController == null)
                    return false;

                return currentPlacementController.BeginPlacement(def);
            }

            return false;
        }

        private void TryHandleHotbarActivationInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            TryActivateFromKeyboardKey(keyboard, Key.Digit1);
            TryActivateFromKeyboardKey(keyboard, Key.Digit2);
            TryActivateFromKeyboardKey(keyboard, Key.Digit3);
            TryActivateFromKeyboardKey(keyboard, Key.Digit4);
            TryActivateFromKeyboardKey(keyboard, Key.Digit5);
            TryActivateFromKeyboardKey(keyboard, Key.Digit6);
            TryActivateFromKeyboardKey(keyboard, Key.Digit7);
            TryActivateFromKeyboardKey(keyboard, Key.Digit8);

            // Numpad support mirrors top-row behavior.
            TryActivateFromKeyboardKey(keyboard, Key.Numpad1);
            TryActivateFromKeyboardKey(keyboard, Key.Numpad2);
            TryActivateFromKeyboardKey(keyboard, Key.Numpad3);
            TryActivateFromKeyboardKey(keyboard, Key.Numpad4);
            TryActivateFromKeyboardKey(keyboard, Key.Numpad5);
            TryActivateFromKeyboardKey(keyboard, Key.Numpad6);
            TryActivateFromKeyboardKey(keyboard, Key.Numpad7);
            TryActivateFromKeyboardKey(keyboard, Key.Numpad8);
        }

        private void TryActivateFromKeyboardKey(Keyboard keyboard, Key key)
        {
            var control = keyboard[key];
            if (control == null || !control.wasPressedThisFrame)
                return;

            int slotIndex = GetHotbarSlotIndexFromNumberKey(key);
            if (slotIndex < 0)
                return;

            TryActivateSlot(slotIndex);
        }

        private void TryBindToLocalPlayerInventory()
        {
            if (currentInventoryNet != null)
                return;

            PlayerInventoryNet[] allInventories = FindObjectsOfType<PlayerInventoryNet>(true);
            for (int i = 0; i < allInventories.Length; i++)
            {
                PlayerInventoryNet inv = allInventories[i];
                if (inv == null || !inv.IsOwner)
                    continue;

                currentInventoryNet = inv;
                currentVitals = inv.GetComponent<PlayerVitalsNet>();
                currentEquipmentNet = inv.GetComponent<PlayerEquipmentNet>();
                SubscribeToInventorySnapshots();
                ForceNextRender();
                return;
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

            InventorySnapshot snapshot = currentInventoryNet.LastSnapshot;
            if (snapshot.Slots == null)
                return;

            int signature = ComputeSnapshotSignature(snapshot, isExpanded);
            if (signature == lastRenderedSignature)
                return;

            lastRenderedSignature = signature;
            Render(snapshot);
        }

        private static int ComputeSnapshotSignature(InventorySnapshot snapshot, bool expanded)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + snapshot.W;
                hash = hash * 31 + snapshot.H;
                hash = hash * 31 + (expanded ? 1 : 0);
                hash = hash * 31 + (snapshot.Slots == null ? 0 : snapshot.Slots.Length);

                if (snapshot.Slots == null)
                    return hash;

                for (int i = 0; i < snapshot.Slots.Length; i++)
                {
                    InventorySnapshot.SlotDto s = snapshot.Slots[i];
                    hash = hash * 31 + (s.IsEmpty ? 1 : 0);
                    hash = hash * 31 + s.Quantity;
                    hash = hash * 31 + (int)s.ContentType;
                    hash = hash * 31 + s.ItemId.GetHashCode();
                    hash = hash * 31 + s.Durability;
                    hash = hash * 31 + s.MaxDurability;
                    hash = hash * 31 + s.BonusStrength;
                    hash = hash * 31 + s.BonusDexterity;
                    hash = hash * 31 + s.BonusIntelligence;
                    hash = hash * 31 + s.CraftedBy.GetHashCode();
                    hash = hash * 31 + s.InstanceId.GetHashCode();
                    hash = hash * 31 + s.RolledDamage.GetHashCode();
                    hash = hash * 31 + s.RolledDefence.GetHashCode();
                    hash = hash * 31 + s.RolledSwingSpeed.GetHashCode();
                    hash = hash * 31 + s.RolledMovementSpeed.GetHashCode();
                }

                return hash;
            }
        }

        private void Render(InventorySnapshot snapshot)
        {
            if (slotPrefab == null)
                return;

            int snapshotCount = snapshot.Slots == null ? 0 : snapshot.Slots.Length;
            int desiredSlots = Mathf.Max(HotbarSlotCount, Mathf.Min(TotalInventorySlots, Mathf.Max(GetClampedUiSlotCount(), snapshotCount)));
            EnsureSlotUICount(desiredSlots);

            int renderCount = Mathf.Min(slotUIs.Count, snapshotCount);

            for (int i = 0; i < renderCount; i++)
            {
                InventorySnapshot.SlotDto netSlot = snapshot.Slots[i];
                InventoryGridSlotUI uiSlot = slotUIs[i];

                EnsureSlotParentForIndex(i, uiSlot);
                uiSlot.SetContainerContext(InventoryContainerType.Player, i);
                uiSlot.SetContainerDragController(null);
                uiSlot.gameObject.SetActive(IsSlotVisibleInCurrentPresentation(i));

                if (netSlot.IsEmpty)
                {
                    uiSlot.SetEmpty();
                    continue;
                }

                string itemId = netSlot.ItemId.ToString();
                int qty = netSlot.Quantity;

                int maxDurability = netSlot.MaxDurability;
                int durability = netSlot.Durability;

                if (maxDurability <= 0 && itemDatabase != null && itemDatabase.TryGet(itemId, out ItemDef defFromDb) && defFromDb != null)
                {
                    maxDurability = Mathf.Max(0, defFromDb.MaxDurability);
                    if (maxDurability > 0 && durability <= 0)
                        durability = maxDurability;
                }

                Sprite icon = ResolveIcon(itemId);
                ItemTooltipData tooltip = BuildTooltipData(itemId, netSlot, durability);
                uiSlot.SetItem(itemId, icon, qty, durability, maxDurability, tooltip);
            }

            // Any extra instantiated slot UIs beyond snapshot count are hidden and emptied.
            for (int i = renderCount; i < slotUIs.Count; i++)
            {
                InventoryGridSlotUI uiSlot = slotUIs[i];
                EnsureSlotParentForIndex(i, uiSlot);
                uiSlot.SetEmpty();
                uiSlot.gameObject.SetActive(IsSlotVisibleInCurrentPresentation(i));
            }
        }

        private bool IsSlotVisibleInCurrentPresentation(int slotIndex)
        {
            if (IsHotbarSlot(slotIndex))
                return true;

            if (IsLowerInventorySlot(slotIndex))
                return IsInventoryExpanded();

            // Do not surface indices beyond the authoritative fixed-size player inventory.
            return false;
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
                MaxDurability = slot.MaxDurability,
                CraftedBy = slot.CraftedBy.ToString(),
                InstanceId = slot.InstanceId,
                RolledDamage = slot.RolledDamage,
                RolledDefence = slot.RolledDefence,
                RolledSwingSpeed = slot.RolledSwingSpeed,
                RolledMovementSpeed = slot.RolledMovementSpeed
            };

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out ItemDef def) && def != null)
            {
                data.DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.ItemId : def.DisplayName;
                data.Description = def.Description;
                // Prefer rolled values when this slot represents an instance item.
                data.Damage = slot.ContentType == InventorySlotContentType.Instance && slot.RolledDamage > 0f ? slot.RolledDamage : def.Damage;
                data.Defence = slot.ContentType == InventorySlotContentType.Instance && slot.RolledDefence > 0f ? slot.RolledDefence : def.Defence;
                data.AttackBonus = def.AttackBonus;
                data.SwingSpeed = slot.ContentType == InventorySlotContentType.Instance && slot.RolledSwingSpeed > 0f ? slot.RolledSwingSpeed : def.SwingSpeed;
                data.MoveSpeed = slot.ContentType == InventorySlotContentType.Instance && slot.RolledMovementSpeed > 0f ? slot.RolledMovementSpeed : def.MovementSpeed;

                data.Strength = Mathf.Max(0, def.Strength) + slot.BonusStrength;
                data.Dexterity = Mathf.Max(0, def.Dexterity) + slot.BonusDexterity;
                data.Intelligence = Mathf.Max(0, def.Intelligence) + slot.BonusIntelligence;
            }

            return data;
        }

        private int GetClampedUiSlotCount()
        {
            // Defensive clamp: player inventory is authoritative fixed-size 8x4 (32).
            // This prevents stale scene values (for example old 48-slot setups) from creating
            // extra UI slots that can steal raycasts and produce invalid drop target indices.
            return Mathf.Clamp(uiSlotCount, HotbarSlotCount, TotalInventorySlots);
        }
        private void EnsureSlotUICount(int desiredCount)
        {
            while (slotUIs.Count < desiredCount)
            {
                int slotIndex = slotUIs.Count;
                Transform parent = ResolveParentForSlot(slotIndex);
                InventoryGridSlotUI ui = Instantiate(slotPrefab, parent != null ? parent : transform);

                ui.SetContainerContext(InventoryContainerType.Player, slotIndex);
                ui.SetContainerDragController(null);
                ui.BindCanStartDrag(CanStartDragCurrentMode);
                ui.BindClick(OnSlotClicked);
                ui.BindRightClick(OnSlotRightClicked);
                ui.SetEmpty();

                slotUIs.Add(ui);
            }

            while (slotUIs.Count > desiredCount)
            {
                int last = slotUIs.Count - 1;
                if (slotUIs[last] != null)
                    Destroy(slotUIs[last].gameObject);

                slotUIs.RemoveAt(last);
            }
        }

        private Transform ResolveParentForSlot(int slotIndex)
        {
            // Preferred hierarchy:
            // - row0Root contains slots 0..7
            // - expandedRowsRoot contains InventoryGridRow_1/2/3 for slots 8..31
            if (row0Root != null && expandedRowsRoot != null)
            {
                if (IsHotbarSlot(slotIndex))
                    return row0Root;

                Transform perRowRoot = ResolveExpandedPerRowRoot(slotIndex);
                if (perRowRoot != null)
                    return perRowRoot;
            }

            // Fallback: use single grid root.
            if (gridRoot != null)
                return gridRoot;

            return transform;
        }

        private Transform ResolveExpandedPerRowRoot(int slotIndex)
        {
            if (expandedRowsRoot == null || !IsLowerInventorySlot(slotIndex))
                return null;

            int row = slotIndex / InventoryColumns; // 1..3 for lower rows

            Transform explicitRow = GetExplicitExpandedRowRoot(row);
            if (explicitRow != null)
                return explicitRow;

            // Preferred names for current hierarchy model.
            Transform named = expandedRowsRoot.Find($"InventoryGridRow_{row}");
            if (named != null)
                return named;

            // Legacy fallback for older prefab naming.
            Transform legacyNamed = expandedRowsRoot.Find($"Row{row}");
            if (legacyNamed != null)
                return legacyNamed;

            int childIndex = row - 1;
            if (childIndex >= 0 && childIndex < expandedRowsRoot.childCount)
                return expandedRowsRoot.GetChild(childIndex);

            return null;
        }

        private Transform GetExplicitExpandedRowRoot(int row)
        {
            switch (row)
            {
                case 1: return inventoryGridRow1Root;
                case 2: return inventoryGridRow2Root;
                case 3: return inventoryGridRow3Root;
                default: return null;
            }
        }

        private void EnsureSlotParentForIndex(int slotIndex, InventoryGridSlotUI slotUi)
        {
            if (slotUi == null)
                return;

            Transform desiredParent = ResolveParentForSlot(slotIndex);
            if (desiredParent == null)
                return;

            if (slotUi.transform.parent != desiredParent)
                slotUi.transform.SetParent(desiredParent, false);
        }

        private Sprite ResolveIcon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out ItemDef def) && def != null)
                return def.Icon;

            return null;
        }

        private void OnSlotClicked(int slotIndex, string itemId, int clickCount)
        {
            // Single left-click remains non-mutating; drag and right-click/hotkeys handle action intents.
            if (string.IsNullOrWhiteSpace(itemId))
                return;
        }

        private void OnSlotRightClicked(int slotIndex, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return;

            if (!IsInventoryExpanded() && !(allowRightClickHotbarWhenCollapsed && IsHotbarSlot(slotIndex)))
                return;

            TryActivateSlot(slotIndex);
        }

        private void ApplySlotPresentationForCurrentMode()
        {
            ApplyRowRootVisibility();

            for (int i = 0; i < slotUIs.Count; i++)
            {
                if (slotUIs[i] == null)
                    continue;

                slotUIs[i].gameObject.SetActive(IsSlotVisibleInCurrentPresentation(i));
            }

            ForceNextRender();
            TryRenderIfChanged();
        }

        private void ApplyRowRootVisibility()
        {
            if (row0Root != null)
                row0Root.gameObject.SetActive(true);

            if (expandedRowsRoot == null)
                return;

            bool expanded = IsInventoryExpanded();
            expandedRowsRoot.gameObject.SetActive(expanded);

            // Ensure per-row containers are enabled when expanded.
            if (expanded)
            {
                SetExpandedRowActive(1, true);
                SetExpandedRowActive(2, true);
                SetExpandedRowActive(3, true);
            }
        }

        private void SetExpandedRowActive(int rowNumber, bool active)
        {
            if (expandedRowsRoot == null)
                return;

            Transform row = GetExplicitExpandedRowRoot(rowNumber);
            if (row != null)
            {
                row.gameObject.SetActive(active);
                return;
            }

            row = expandedRowsRoot.Find($"InventoryGridRow_{rowNumber}");
            if (row != null)
            {
                row.gameObject.SetActive(active);
                return;
            }

            row = expandedRowsRoot.Find($"Row{rowNumber}");
            if (row != null)
            {
                row.gameObject.SetActive(active);
                return;
            }

            int childIndex = rowNumber - 1;
            if (childIndex >= 0 && childIndex < expandedRowsRoot.childCount)
                expandedRowsRoot.GetChild(childIndex).gameObject.SetActive(active);
        }

        private void ApplyExpandedInteractionLock()
        {
            if (IsInventoryExpanded())
            {
                // Expanded inventory keeps existing behavior: gameplay input is locked so cursor interaction is stable.
                if (!gameplayLockHeld)
                {
                    InputState.LockGameplay();
                    gameplayLockHeld = true;
                }

                return;
            }

            // Collapsed mode keeps row 0 visible while restoring gameplay input.
            ReleaseGameplayLockIfHeld();
        }

        private void ReleaseGameplayLockIfHeld()
        {
            if (!gameplayLockHeld)
                return;

            InputState.UnlockGameplay();
            gameplayLockHeld = false;
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










