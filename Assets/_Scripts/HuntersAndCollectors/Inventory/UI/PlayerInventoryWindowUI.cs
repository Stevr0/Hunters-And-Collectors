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
        [SerializeField] private Transform gridRoot;                 // Parent with GridLayoutGroup
        [SerializeField] private InventoryGridSlotUI slotPrefab;     // PF_InventoryGridSlot

        [Header("Item Resolver")]
        [SerializeField] private ItemDatabase itemDatabase;

        // Optional: If you want click-to-equip later, assign this and use it in OnSlotClicked.
        // [SerializeField] private HuntersAndCollectors.UI.PaperdollWindowUI paperdollWindow;

        private Inventory.PlayerInventoryNet currentInventoryNet;
        private readonly List<InventoryGridSlotUI> slotUIs = new();

        private bool gameplayLockHeld;

        // Simple polling signature (same as you had, but now we re-render visually).
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
            TryRenderIfChanged();
        }

        private void OnDisable()
        {
            currentInventoryNet = null;

            // Keep the UI objects (optional) or clear them.
            // For MVP, I recommend keeping them so reopening is instant.
            // ClearGrid();

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
            // Polling MVP. Later you can replace this with an event.
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
            if (gridRoot == null || slotPrefab == null)
                return;

            EnsureSlotUICount(snapshot.Slots.Length);

            for (int i = 0; i < snapshot.Slots.Length; i++)
            {
                var netSlot = snapshot.Slots[i];
                var uiSlot = slotUIs[i];

                if (netSlot.IsEmpty)
                {
                    uiSlot.SetEmpty();
                    continue;
                }

                string itemId = netSlot.ItemId.ToString();
                int qty = netSlot.Quantity;

                // Resolve icon from ItemDatabase
                Sprite icon = ResolveIcon(itemId);

                uiSlot.SetItem(itemId, icon, qty);
            }
        }

        /// <summary>
        /// Make sure we have exactly N slot UI objects created.
        /// We reuse them between renders for performance.
        /// </summary>
        private void EnsureSlotUICount(int desiredCount)
        {
            // Create missing ones
            while (slotUIs.Count < desiredCount)
            {
                var ui = Instantiate(slotPrefab, gridRoot);
                slotUIs.Add(ui);

                // Bind click handler (for future equip/use)
                ui.BindClick(OnSlotClicked);
            }

            // If we have too many (inventory size changed), destroy extras.
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

            // Uses your existing API.
            if (itemDatabase.TryGet(itemId, out var def) && def != null)
                return def.Icon;

            return null;
        }

        /// <summary>
        /// Slot click handler.
        /// MVP: do nothing, or later call PaperdollWindowUI.RequestEquipFromInventory(itemId)
        /// if the item is equippable.
        /// </summary>
        private void OnSlotClicked(string itemId)
        {
            // For now, just log so you know clicking works.
            Debug.Log($"[Inventory] Clicked itemId={itemId}");

            // Later (when you want click-to-equip):
            // if (paperdollWindow != null)
            //     paperdollWindow.RequestEquipFromInventory(itemId);
        }

        // Optional if you want to clear on disable.
        private void ClearGrid()
        {
            for (int i = 0; i < slotUIs.Count; i++)
            {
                if (slotUIs[i] != null)
                    Destroy(slotUIs[i].gameObject);
            }
            slotUIs.Clear();
        }
    }
}
