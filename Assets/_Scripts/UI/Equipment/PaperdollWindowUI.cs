using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    [DisallowMultipleComponent]
    public sealed class PaperdollWindowUI : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("UI")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private List<PaperdollSlotUI> slots = new();

        [Header("Multiplayer")]
        [Tooltip("If true: non-owner can view but cannot click.")]
        [SerializeField] private bool viewOnlyForNonOwner = true;

        private PlayerEquipmentNet equipmentNet;
        private PlayerInventoryNet inventoryNet;

        private Action equipmentChangedHandler;
        private readonly Dictionary<EquipSlot, string> lastRenderedSlotIds = new();

        private void OnEnable()
        {
            TryBindToLocalPlayer();
            RefreshAll();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Update()
        {
            if (equipmentNet == null)
                TryBindToLocalPlayer();

            if (equipmentNet != null && IsEquipmentVisualOutOfDate())
                RefreshAll();
        }

        private void TryBindToLocalPlayer()
        {
            if (equipmentNet != null)
                return;

            var equipments = FindObjectsOfType<PlayerEquipmentNet>(true);
            for (int i = 0; i < equipments.Length; i++)
            {
                var e = equipments[i];
                if (e != null && e.IsOwner)
                {
                    var inventories = FindObjectsOfType<PlayerInventoryNet>(true);
                    PlayerInventoryNet invOwner = null;
                    for (int j = 0; j < inventories.Length; j++)
                    {
                        if (inventories[j] != null && inventories[j].IsOwner)
                        {
                            invOwner = inventories[j];
                            break;
                        }
                    }

                    Bind(e, invOwner);
                    Debug.Log($"[PaperdollWindowUI] Bound to local PlayerEquipmentNet. netId={e.NetworkObjectId}");
                    return;
                }
            }
        }

        public void Bind(PlayerEquipmentNet equipment, PlayerInventoryNet inventory, ItemDatabase dbOverride = null)
        {
            Unbind();

            equipmentNet = equipment;
            inventoryNet = inventory;

            if (dbOverride != null)
                itemDatabase = dbOverride;

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                    slots[i].Bind(this);
            }

            bool isOwner = equipmentNet != null && equipmentNet.IsOwner;
            bool canInteract = viewOnlyForNonOwner ? isOwner : equipmentNet != null;
            SetSlotsInteractable(canInteract);

            if (equipmentNet != null)
            {
                equipmentChangedHandler = RefreshAll;
                equipmentNet.OnEquipmentChanged += equipmentChangedHandler;
            }

            RefreshAll();
        }

        public void Unbind()
        {
            if (equipmentNet != null && equipmentChangedHandler != null)
                equipmentNet.OnEquipmentChanged -= equipmentChangedHandler;

            equipmentChangedHandler = null;
            equipmentNet = null;
            inventoryNet = null;
        }

        public void OnSlotClicked(EquipSlot slot)
        {
            if (equipmentNet == null || !equipmentNet.IsOwner)
                return;

            string equippedId = equipmentNet.GetEquippedItemId(slot);
            if (!string.IsNullOrWhiteSpace(equippedId))
                equipmentNet.RequestUnequipSlotServerRpc(slot);
        }

        public void RequestEquipFromInventory(string itemId)
        {
            if (equipmentNet == null || !equipmentNet.IsOwner)
                return;

            if (string.IsNullOrWhiteSpace(itemId))
                return;

            equipmentNet.RequestEquipByItemIdServerRpc(new FixedString64Bytes(itemId));
        }

        public void ForceRefresh()
        {
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (equipmentNet == null)
                return;

            if (titleText != null)
                titleText.text = "Equipment";

            for (int i = 0; i < slots.Count; i++)
            {
                var slotUI = slots[i];
                if (slotUI == null)
                    continue;

                string itemId = equipmentNet.GetEquippedItemId(slotUI.Slot);
                var icon = ResolveIcon(itemId);
                slotUI.SetIcon(icon);
                slotUI.SetEquippedItemCache(itemId, icon);
                lastRenderedSlotIds[slotUI.Slot] = itemId ?? string.Empty;
            }
        }

        private Sprite ResolveIcon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            if (itemDatabase == null)
                return null;

            return itemDatabase.TryGet(itemId, out ItemDef def) ? def.Icon : null;
        }

        private void SetSlotsInteractable(bool canInteract)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                    slots[i].SetInteractable(canInteract);
            }
        }

        private bool IsEquipmentVisualOutOfDate()
        {
            if (equipmentNet == null)
                return false;

            for (int i = 0; i < slots.Count; i++)
            {
                var slotUI = slots[i];
                if (slotUI == null)
                    continue;

                var latestId = equipmentNet.GetEquippedItemId(slotUI.Slot) ?? string.Empty;
                if (!lastRenderedSlotIds.TryGetValue(slotUI.Slot, out var lastId))
                    return true;

                if (!string.Equals(latestId, lastId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
