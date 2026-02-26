using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using System;
using System.Collections.Generic;
using System.Text;
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
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private List<PaperdollSlotUI> slots = new();

        [Header("Multiplayer")]
        [Tooltip("If true: non-owner can view but cannot click.")]
        [SerializeField] private bool viewOnlyForNonOwner = true;

        private PlayerEquipmentNet equipmentNet;
        private PlayerInventoryNet inventoryNet;

        private Action equipmentChangedHandler;
        private readonly Dictionary<EquipSlot, string> lastRenderedSlotIds = new();
        private bool hoverSubscribed;

        private void OnEnable()
        {
            TryBindToLocalPlayer();
            RefreshAll();
            SubscribeHoverBus();
        }

        private void OnDisable()
        {
            UnsubscribeHoverBus();
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

            UnsubscribeHoverBus();
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
                titleText.text = equipmentNet.IsOwner ? "Equipment" : "Equipment";

            for (int i = 0; i < slots.Count; i++)
            {
                var slotUI = slots[i];
                if (slotUI == null) continue;

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

        private void HandleHoveredItemChanged(string itemId)
        {
            if (infoText == null)
                return;

            if (itemDatabase == null)
            {
                infoText.text = itemId ?? string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                infoText.text = string.Empty;
                return;
            }

            if (!itemDatabase.TryGet(itemId, out ItemDef def) || def == null)
            {
                infoText.text = itemId;
                return;
            }

            infoText.text = BuildInfoText(def);
        }

        private void HandleHoverCleared()
        {
            if (infoText != null)
                infoText.text = string.Empty;
        }

        private void SubscribeHoverBus()
        {
            if (hoverSubscribed)
                return;

            HuntersAndCollectors.UI.ItemHoverBus.HoveredItemChanged += HandleHoveredItemChanged;
            HuntersAndCollectors.UI.ItemHoverBus.HoverCleared += HandleHoverCleared;
            hoverSubscribed = true;
        }

        private void UnsubscribeHoverBus()
        {
            if (!hoverSubscribed)
                return;

            HuntersAndCollectors.UI.ItemHoverBus.HoveredItemChanged -= HandleHoveredItemChanged;
            HuntersAndCollectors.UI.ItemHoverBus.HoverCleared -= HandleHoverCleared;
            hoverSubscribed = false;
        }

        private bool IsEquipmentVisualOutOfDate()
        {
            if (equipmentNet == null) return false;

            for (int i = 0; i < slots.Count; i++)
            {
                var slotUI = slots[i];
                if (slotUI == null) continue;

                var latestId = equipmentNet.GetEquippedItemId(slotUI.Slot) ?? string.Empty;
                if (!lastRenderedSlotIds.TryGetValue(slotUI.Slot, out var lastId))
                    return true;

                if (!string.Equals(latestId, lastId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private string BuildInfoText(ItemDef def)
        {
            var lines = new StringBuilder(256);

            lines.AppendLine(def.DisplayName);
            lines.Append("Category: ").Append(def.Category).AppendLine();
            lines.Append("Max Stack: ").Append(def.MaxStack).AppendLine();

            if (def.IsEquippable)
            {
                lines.Append("Equip Slot: ").Append(def.EquipSlot).AppendLine();
                lines.Append("Handedness: ").Append(def.Handedness).AppendLine();

                if (def.ToolTags != null && def.ToolTags.Length > 0)
                {
                    lines.Append("Tool Tags: ");
                    for (int i = 0; i < def.ToolTags.Length; i++)
                    {
                        lines.Append(def.ToolTags[i]);
                        if (i < def.ToolTags.Length - 1)
                            lines.Append(", ");
                    }
                    lines.AppendLine();
                }
            }

            if (!string.IsNullOrWhiteSpace(def.Description))
            {
                lines.AppendLine();
                lines.AppendLine(def.Description.Trim());
            }

            if (!string.IsNullOrWhiteSpace(def.PropertiesText))
            {
                lines.AppendLine();
                lines.AppendLine("Properties:");
                lines.AppendLine(def.PropertiesText.Trim());
            }

            return lines.ToString();
        }
    }
}
