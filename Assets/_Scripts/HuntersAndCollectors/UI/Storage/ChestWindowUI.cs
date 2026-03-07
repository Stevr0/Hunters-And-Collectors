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
    /// First-pass chest interaction window.
    ///
    /// Responsibilities:
    /// - Bind to one active ChestContainerNet.
    /// - Render chest slots from chest snapshots.
    /// - Render player slots from player inventory snapshots.
    /// - Route click transfers through server RPC requests.
    ///
    /// First-pass interaction:
    /// - Click chest slot => take full stack to player.
    /// - Click player slot => store full stack to chest.
    /// </summary>
    public sealed class ChestWindowUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Header("Chest Grid")]
        [SerializeField] private Transform chestGridRoot;
        [SerializeField] private InventoryGridSlotUI chestSlotPrefab;

        [Header("Player Grid")]
        [SerializeField] private Transform playerGridRoot;
        [SerializeField] private InventoryGridSlotUI playerSlotPrefab;

        [Header("Data")]
        [SerializeField] private ItemDatabase itemDatabase;

        private ChestContainerNet activeChest;
        private PlayerInventoryNet localPlayerInventory;

        private readonly List<InventoryGridSlotUI> chestSlotUis = new();
        private readonly List<InventoryGridSlotUI> playerSlotUis = new();

        private InventorySnapshot latestChestSnapshot;
        private InventorySnapshot latestPlayerSnapshot;

        private bool gameplayLockHeld;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
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

            BindLocalPlayerInventoryIfNeeded();
        }

        private void OnDisable()
        {
            if (activeChest != null)
                activeChest.OnChestSnapshotChanged -= HandleChestSnapshotChanged;

            if (localPlayerInventory != null)
                localPlayerInventory.OnSnapshotChanged -= HandlePlayerSnapshotChanged;

            activeChest = null;

            if (gameplayLockHeld)
            {
                InputState.UnlockGameplay();
                gameplayLockHeld = false;
            }
        }

        /// <summary>
        /// Opens chest UI for a specific chest network object.
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

            if (activeChest != null)
                activeChest.OnChestSnapshotChanged -= HandleChestSnapshotChanged;

            activeChest = chest;
            activeChest.OnChestSnapshotChanged += HandleChestSnapshotChanged;

            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (titleText != null)
                titleText.text = "Chest";

            // Render cached snapshots immediately if available.
            latestChestSnapshot = activeChest.LastSnapshot;
            latestPlayerSnapshot = localPlayerInventory.LastSnapshot;

            RenderChestSnapshot(latestChestSnapshot);
            RenderPlayerSnapshot(latestPlayerSnapshot);

            // Request fresh chest snapshot from server.
            activeChest.RequestOpenChestServerRpc();
        }

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

            if (localPlayerInventory != null)
            {
                localPlayerInventory.OnSnapshotChanged -= HandlePlayerSnapshotChanged;
                localPlayerInventory.OnSnapshotChanged += HandlePlayerSnapshotChanged;
            }
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

        private void RenderChestSnapshot(InventorySnapshot snapshot)
        {
            if (chestGridRoot == null || chestSlotPrefab == null)
                return;

            int slotCount = snapshot.Slots == null ? 0 : snapshot.Slots.Length;
            EnsureSlotCount(chestSlotUis, chestSlotPrefab, chestGridRoot, slotCount, OnChestSlotClicked);

            for (int i = 0; i < slotCount; i++)
                BindSlotUiFromSnapshot(chestSlotUis[i], i, snapshot.Slots[i]);
        }

        private void RenderPlayerSnapshot(InventorySnapshot snapshot)
        {
            if (playerGridRoot == null || playerSlotPrefab == null)
                return;

            int slotCount = snapshot.Slots == null ? 0 : snapshot.Slots.Length;
            EnsureSlotCount(playerSlotUis, playerSlotPrefab, playerGridRoot, slotCount, OnPlayerSlotClicked);

            for (int i = 0; i < slotCount; i++)
                BindSlotUiFromSnapshot(playerSlotUis[i], i, snapshot.Slots[i]);
        }

        private void OnChestSlotClicked(int chestSlotIndex, string itemId, int clickCount)
        {
            if (activeChest == null)
                return;

            if (latestChestSnapshot.Slots == null || chestSlotIndex < 0 || chestSlotIndex >= latestChestSnapshot.Slots.Length)
                return;

            InventorySnapshot.SlotDto slot = latestChestSnapshot.Slots[chestSlotIndex];
            if (slot.IsEmpty || slot.Quantity <= 0)
                return;

            // First-pass behavior: transfer full slot stack.
            activeChest.RequestTakeToPlayerServerRpc(chestSlotIndex, slot.Quantity);
        }

        private void OnPlayerSlotClicked(int playerSlotIndex, string itemId, int clickCount)
        {
            if (activeChest == null)
                return;

            if (latestPlayerSnapshot.Slots == null || playerSlotIndex < 0 || playerSlotIndex >= latestPlayerSnapshot.Slots.Length)
                return;

            InventorySnapshot.SlotDto slot = latestPlayerSnapshot.Slots[playerSlotIndex];
            if (slot.IsEmpty || slot.Quantity <= 0)
                return;

            // First-pass behavior: transfer full slot stack.
            activeChest.RequestStoreFromPlayerServerRpc(playerSlotIndex, slot.Quantity);
        }

        private void BindSlotUiFromSnapshot(InventoryGridSlotUI slotUi, int slotIndex, InventorySnapshot.SlotDto slot)
        {
            if (slotUi == null)
                return;

            slotUi.SetSlotIndex(slotIndex);

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

        private static void EnsureSlotCount(
            List<InventoryGridSlotUI> slotList,
            InventoryGridSlotUI slotPrefab,
            Transform root,
            int desiredCount,
            System.Action<int, string, int> onClicked)
        {
            while (slotList.Count < desiredCount)
            {
                InventoryGridSlotUI slotUi = Instantiate(slotPrefab, root);
                slotUi.BindClick(onClicked);
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
    }
}
