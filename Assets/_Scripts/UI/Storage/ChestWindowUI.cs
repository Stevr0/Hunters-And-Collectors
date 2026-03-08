using System.Collections.Generic;
using HuntersAndCollectors.Input;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Inventory.UI;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Storage;
using HuntersAndCollectors.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI.Storage
{
    /// <summary>
    /// ChestWindowUI
    /// --------------------------------------------------------------------
    /// Container window that shows two grids in one place:
    /// - Top: Player inventory snapshot
    /// - Bottom: Chest inventory snapshot
    ///
    /// This keeps one active presenter for the player inventory while chest is open,
    /// preventing duplicated UI presenters from fighting each other.
    ///
    /// Drag/drop transfers are routed by InventoryDragController.
    /// </summary>
    public sealed class ChestWindowUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Header("Player Panel (Top)")]
        [SerializeField] private Transform playerGridRoot;
        [SerializeField] private InventoryGridSlotUI playerSlotPrefab;

        [Header("Chest Panel (Bottom)")]
        [SerializeField] private Transform chestGridRoot;
        [SerializeField] private InventoryGridSlotUI chestSlotPrefab;

        [Header("Drag")]
        [SerializeField] private InventoryDragController inventoryDragController;

        [Header("Single Presenter Ownership")]
        [Tooltip("Optional. If assigned, this character root is closed while chest UI is open so only one player inventory presenter is active.")]
        [SerializeField] private CharacterWindowRootUI characterWindowRoot;

        [Header("Data")]
        [SerializeField] private ItemDatabase itemDatabase;

        private ChestContainerNet activeChest;
        private PlayerInventoryNet localPlayerInventory;

        private readonly List<InventoryGridSlotUI> chestSlotUis = new();
        private readonly List<InventoryGridSlotUI> playerSlotUis = new();

        private InventorySnapshot latestChestSnapshot;
        private InventorySnapshot latestPlayerSnapshot;

        private bool gameplayLockHeld;
        private bool characterWindowWasOpenBeforeChest;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (inventoryDragController == null)
                inventoryDragController = FindFirstObjectByType<InventoryDragController>(FindObjectsInactive.Include);

            if (characterWindowRoot == null)
                characterWindowRoot = FindFirstObjectByType<CharacterWindowRootUI>(FindObjectsInactive.Include);
        }

        private void OnEnable()
        {
            if (!gameplayLockHeld)
            {
                InputState.LockGameplay();
                gameplayLockHeld = true;
            }

            BindLocalPlayerInventoryIfNeeded();
        }

        private void OnDisable()
        {
            UnbindActiveSources();
            ClearAllSlots();
            RestoreCharacterWindowOwnership();

            if (gameplayLockHeld)
            {
                InputState.UnlockGameplay();
                gameplayLockHeld = false;
            }
        }

        /// <summary>
        /// Opens chest UI and binds both player/chest snapshots.
        /// </summary>
        public void Open(ChestContainerNet chest)
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

            BindLocalPlayerInventoryIfNeeded();
            if (localPlayerInventory == null)
            {
                Debug.LogWarning("[ChestWindowUI] Local player inventory not found.");
                return;
            }

            // Clean unbind if window was previously bound to a different chest.
            UnbindActiveSources();

            activeChest = chest;
            activeChest.OnChestSnapshotChanged += HandleChestSnapshotChanged;

            localPlayerInventory.OnSnapshotChanged -= HandlePlayerSnapshotChanged;
            localPlayerInventory.OnSnapshotChanged += HandlePlayerSnapshotChanged;

            // Container drag controller needs the active chest reference.
            if (inventoryDragController != null)
                inventoryDragController.BindChest(activeChest);

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (titleText != null)
                titleText.text = "Container";

            TakeCharacterWindowOwnership();

            // Render cached snapshots immediately.
            latestChestSnapshot = activeChest.LastSnapshot;
            latestPlayerSnapshot = localPlayerInventory.LastSnapshot;

            RenderPlayerSnapshot(latestPlayerSnapshot);
            RenderChestSnapshot(latestChestSnapshot);

            // Ask server for latest chest data.
            activeChest.RequestOpenChestServerRpc();
        }

        /// <summary>
        /// Closes chest window and unbinds all listeners.
        /// </summary>
        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void BindLocalPlayerInventoryIfNeeded()
        {
            if (localPlayerInventory == null)
            {
                PlayerInventoryNet[] found = FindObjectsByType<PlayerInventoryNet>(FindObjectsSortMode.None);
                for (int i = 0; i < found.Length; i++)
                {
                    PlayerInventoryNet candidate = found[i];
                    if (candidate != null && candidate.IsOwner)
                    {
                        localPlayerInventory = candidate;
                        break;
                    }
                }
            }
        }

        private void UnbindActiveSources()
        {
            if (activeChest != null)
                activeChest.OnChestSnapshotChanged -= HandleChestSnapshotChanged;

            if (localPlayerInventory != null)
                localPlayerInventory.OnSnapshotChanged -= HandlePlayerSnapshotChanged;

            if (inventoryDragController != null)
                inventoryDragController.ClearChest();

            activeChest = null;
            latestChestSnapshot = default;
            latestPlayerSnapshot = default;
        }

        private void HandleChestSnapshotChanged(InventorySnapshot snapshot)
        {
            latestChestSnapshot = snapshot;
            RenderChestSnapshot(snapshot);
        }

        private void HandlePlayerSnapshotChanged(InventorySnapshot snapshot)
        {
            latestPlayerSnapshot = snapshot;
            RenderPlayerSnapshot(snapshot);
        }

        private void RenderPlayerSnapshot(InventorySnapshot snapshot)
        {
            if (playerGridRoot == null || playerSlotPrefab == null)
                return;

            int slotCount = snapshot.Slots == null ? 0 : snapshot.Slots.Length;
            EnsureSlotCount(
                playerSlotUis,
                playerSlotPrefab,
                playerGridRoot,
                slotCount,
                InventoryContainerType.Player);

            for (int i = 0; i < slotCount; i++)
                BindSlotUiFromSnapshot(playerSlotUis[i], InventoryContainerType.Player, i, snapshot.Slots[i]);
        }

        private void RenderChestSnapshot(InventorySnapshot snapshot)
        {
            if (chestGridRoot == null || chestSlotPrefab == null)
                return;

            int slotCount = snapshot.Slots == null ? 0 : snapshot.Slots.Length;
            EnsureSlotCount(
                chestSlotUis,
                chestSlotPrefab,
                chestGridRoot,
                slotCount,
                InventoryContainerType.Chest);

            for (int i = 0; i < slotCount; i++)
                BindSlotUiFromSnapshot(chestSlotUis[i], InventoryContainerType.Chest, i, snapshot.Slots[i]);
        }

        private void BindSlotUiFromSnapshot(
            InventoryGridSlotUI slotUi,
            InventoryContainerType containerType,
            int slotIndex,
            InventorySnapshot.SlotDto slot)
        {
            if (slotUi == null)
                return;

            slotUi.SetContainerContext(containerType, slotIndex);
            slotUi.SetContainerDragController(inventoryDragController);

            // Chest window transfer uses drag/drop only in this pass.
            slotUi.BindClick(null);

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

        private void EnsureSlotCount(
            List<InventoryGridSlotUI> slotList,
            InventoryGridSlotUI slotPrefab,
            Transform root,
            int desiredCount,
            InventoryContainerType containerType)
        {
            while (slotList.Count < desiredCount)
            {
                InventoryGridSlotUI slotUi = Instantiate(slotPrefab, root);
                slotUi.SetContainerContext(containerType, slotList.Count);
                slotUi.SetContainerDragController(inventoryDragController);
                slotUi.BindClick(null);
                slotUi.SetEmpty();
                slotList.Add(slotUi);
            }

            while (slotList.Count > desiredCount)
            {
                int last = slotList.Count - 1;
                if (slotList[last] != null)
                    Destroy(slotList[last].gameObject);

                slotList.RemoveAt(last);
            }
        }

        private void ClearAllSlots()
        {
            ClearSlotList(chestSlotUis);
            ClearSlotList(playerSlotUis);
        }

        private static void ClearSlotList(List<InventoryGridSlotUI> slots)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                    Destroy(slots[i].gameObject);
            }

            slots.Clear();
        }

        private void TakeCharacterWindowOwnership()
        {
            if (characterWindowRoot == null)
                return;

            characterWindowWasOpenBeforeChest = characterWindowRoot.IsOpen;
            if (characterWindowWasOpenBeforeChest)
                characterWindowRoot.Close();
        }

        private void RestoreCharacterWindowOwnership()
        {
            if (characterWindowRoot == null)
                return;

            if (characterWindowWasOpenBeforeChest)
                characterWindowRoot.Open();

            characterWindowWasOpenBeforeChest = false;
        }
    }
}
