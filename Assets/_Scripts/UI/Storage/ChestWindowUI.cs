using System.Collections.Generic;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Inventory.UI;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Storage;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI.Storage
{
    /// <summary>
    /// Storage-only presenter for chest inventories.
    ///
    /// Root-cause fix:
    /// - The old implementation rendered an extra "player panel" inside this window.
    /// - In scene wiring, that player panel pointed at the same Transform used by
    ///   PlayerInventoryWindowUI's hotbar row.
    /// - Opening chest therefore instantiated a second set of player slot views into the
    ///   player row hierarchy, causing duplicated visuals and drag interactions on the
    ///   duplicate instances instead of the canonical player presenter.
    ///
    /// New model:
    /// - This class renders chest slots only.
    /// - PlayerInventoryWindowUI remains the only owner of player slot views.
    /// - While storage is open, we temporarily route player slot drag/drop through
    ///   InventoryDragController so cross-container transfers use server RPCs.
    /// </summary>
    public sealed class ChestWindowUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Header("Chest Panel")]
        [SerializeField] private Transform chestGridRoot;
        [SerializeField] private InventoryGridSlotUI chestSlotPrefab;

        [Header("Player Presenter (External)")]
        [Tooltip("Canonical player inventory presenter. It owns player slots and is never duplicated by storage UI.")]
        [SerializeField] private PlayerInventoryWindowUI playerInventoryWindow;

        [Header("Drag")]
        [SerializeField] private InventoryDragController inventoryDragController;

        [Header("Data")]
        [SerializeField] private ItemDatabase itemDatabase;

        private StorageNet activeChest;
        private readonly List<InventoryGridSlotUI> chestSlotUis = new();
        private InventorySnapshot latestChestSnapshot;
        private bool gameplayLockHeld;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (inventoryDragController == null)
                inventoryDragController = FindFirstObjectByType<InventoryDragController>(FindObjectsInactive.Include);

            if (playerInventoryWindow == null)
                playerInventoryWindow = FindFirstObjectByType<PlayerInventoryWindowUI>(FindObjectsInactive.Include);
        }

        private void OnEnable()
        {
            if (!gameplayLockHeld)
            {
                InputState.LockGameplay();
                gameplayLockHeld = true;
            }
        }

        private void OnDisable()
        {
            UnbindActiveChestSession();
            ClearChestSlots();

            if (gameplayLockHeld)
            {
                InputState.UnlockGameplay();
                gameplayLockHeld = false;
            }
        }

        /// <summary>
        /// Opens storage UI and binds only to chest snapshots.
        /// Player slot visuals stay owned by PlayerInventoryWindowUI.
        /// </summary>
        public void Open(StorageNet chest)
        {
            if (chest == null)
            {
                Debug.LogWarning("[ChestWindowUI] Open called with null chest.");
                return;
            }

            if (!chest.IsSpawned)
            {
                Debug.LogWarning("[ChestWindowUI] Chest not spawned.", chest);
                return;
            }

            UnbindActiveChestSession();

            activeChest = chest;
            activeChest.OnChestSnapshotChanged += HandleChestSnapshotChanged;

            if (inventoryDragController != null)
                inventoryDragController.BindChest(activeChest);

            if (playerInventoryWindow != null)
                playerInventoryWindow.SetContainerDragController(inventoryDragController);

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (titleText != null)
                titleText.text = "Container";

            // Render current cached chest snapshot first, then request fresh data from server.
            latestChestSnapshot = activeChest.LastSnapshot;
            RenderChestSnapshot(latestChestSnapshot);
            activeChest.RequestOpenChestServerRpc();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void UnbindActiveChestSession()
        {
            if (activeChest != null)
                activeChest.OnChestSnapshotChanged -= HandleChestSnapshotChanged;

            if (inventoryDragController != null)
                inventoryDragController.ClearChest();

            if (playerInventoryWindow != null)
                playerInventoryWindow.ClearContainerDragController();

            activeChest = null;
            latestChestSnapshot = default;
        }

        private void HandleChestSnapshotChanged(InventorySnapshot snapshot)
        {
            latestChestSnapshot = snapshot;
            RenderChestSnapshot(snapshot);
        }

        private void RenderChestSnapshot(InventorySnapshot snapshot)
        {
            if (chestGridRoot == null || chestSlotPrefab == null)
                return;

            int slotCount = snapshot.Slots == null ? 0 : snapshot.Slots.Length;
            EnsureSlotCount(slotCount);

            for (int i = 0; i < slotCount; i++)
                BindSlotUiFromSnapshot(chestSlotUis[i], i, snapshot.Slots[i]);
        }

        private void BindSlotUiFromSnapshot(InventoryGridSlotUI slotUi, int slotIndex, InventorySnapshot.SlotDto slot)
        {
            if (slotUi == null)
                return;

            slotUi.SetContainerContext(InventoryContainerType.StorageInventory, slotIndex);
            slotUi.SetContainerDragController(inventoryDragController);
            slotUi.BindCanStartDrag(null);
            slotUi.BindClick(null);
            slotUi.BindRightClick(null);

            if (slot.IsEmpty)
            {
                slotUi.SetEmpty();
                return;
            }

            string itemId = slot.ItemId.ToString();
            Sprite icon = ResolveIcon(itemId);
            ItemTooltipData tooltipData = BuildTooltipData(itemId, slot);
            slotUi.SetItem(itemId, icon, slot.Quantity, slot.Durability, slot.MaxDurability, tooltipData);
        }

        private Sprite ResolveIcon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) || itemDatabase == null)
                return null;

            if (!itemDatabase.TryGet(itemId, out ItemDef def) || def == null)
                return null;

            return def.Icon;
        }

        private ItemTooltipData BuildTooltipData(string itemId, InventorySnapshot.SlotDto slot)
        {
            ItemTooltipData data = new ItemTooltipData
            {
                ItemId = itemId,
                BonusStrength = slot.BonusStrength,
                BonusDexterity = slot.BonusDexterity,
                BonusIntelligence = slot.BonusIntelligence,
                Durability = slot.Durability,
                CraftedBy = slot.CraftedBy.ToString(),
                DisplayName = itemId
            };

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out ItemDef def) && def != null)
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

        private void EnsureSlotCount(int desiredCount)
        {
            while (chestSlotUis.Count < desiredCount)
            {
                int slotIndex = chestSlotUis.Count;
                InventoryGridSlotUI slotUi = Instantiate(chestSlotPrefab, chestGridRoot);
                slotUi.SetContainerContext(InventoryContainerType.StorageInventory, slotIndex);
                slotUi.SetContainerDragController(inventoryDragController);
                slotUi.BindCanStartDrag(null);
                slotUi.BindClick(null);
                slotUi.BindRightClick(null);
                slotUi.SetEmpty();
                chestSlotUis.Add(slotUi);
            }

            while (chestSlotUis.Count > desiredCount)
            {
                int last = chestSlotUis.Count - 1;
                if (chestSlotUis[last] != null)
                    Destroy(chestSlotUis[last].gameObject);

                chestSlotUis.RemoveAt(last);
            }
        }

        private void ClearChestSlots()
        {
            for (int i = 0; i < chestSlotUis.Count; i++)
            {
                if (chestSlotUis[i] != null)
                    Destroy(chestSlotUis[i].gameObject);
            }

            chestSlotUis.Clear();
        }
    }
}

