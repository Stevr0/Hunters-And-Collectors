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
    public sealed class EquipmentWindowUI : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("UI")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private List<EquipmentSlotUI> slots = new();

        [Header("Multiplayer")]
        [Tooltip("If true: non-owner can view but cannot click.")]
        [SerializeField] private bool viewOnlyForNonOwner = true;

        [Header("Debug")]
        [SerializeField] private bool debugDurabilityRefresh = true;

        private PlayerEquipmentNet equipmentNet;
        private PlayerInventoryNet inventoryNet;

        private Action equipmentChangedHandler;
        private readonly Dictionary<EquipSlot, string> lastRenderedSlotIds = new();
        private readonly Dictionary<EquipSlot, int> lastRenderedSlotDurability = new();

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
                    Debug.Log($"[EquipmentWindowUI] Bound to local PlayerEquipmentNet. netId={e.NetworkObjectId}");
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
                // Keep existing aggregate callback.
                equipmentChangedHandler = RefreshAll;
                equipmentNet.OnEquipmentChanged += equipmentChangedHandler;

                // Also subscribe to per-slot item and durability netvars so UI updates immediately
                // when server replication changes either field.
                SubscribeToSlotNetVars();
            }

            RefreshAll();
        }

        public void Unbind()
        {
            if (equipmentNet != null)
            {
                if (equipmentChangedHandler != null)
                    equipmentNet.OnEquipmentChanged -= equipmentChangedHandler;

                UnsubscribeFromSlotNetVars();
            }

            equipmentChangedHandler = null;
            equipmentNet = null;
            inventoryNet = null;
            lastRenderedSlotIds.Clear();
            lastRenderedSlotDurability.Clear();
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

            // Legacy fallback path.
            equipmentNet.RequestEquipByItemIdServerRpc(new FixedString64Bytes(itemId));
        }

        public void RequestEquipFromInventorySlot(int slotIndex)
        {
            if (equipmentNet == null || !equipmentNet.IsOwner)
                return;

            equipmentNet.RequestEquipFromInventorySlotServerRpc(slotIndex);
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
                Sprite icon = ResolveIcon(itemId);

                // Set cache first, icon second, durability last.
                slotUI.SetEquippedItemCache(itemId, icon);
                slotUI.SetIcon(icon);

                int maxDurability = 0;
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    if (itemDatabase != null && itemDatabase.TryGet(itemId, out var defFromDb) && defFromDb != null)
                    {
                        maxDurability = Mathf.Max(0, defFromDb.MaxDurability);
                    }
                    else if (equipmentNet.TryGetItemDef(itemId, out var defFromEquip) && defFromEquip != null)
                    {
                        // Fallback when ItemDatabase isn't bound on this UI.
                        maxDurability = Mathf.Max(0, defFromEquip.MaxDurability);
                    }
                }

                int durability = equipmentNet.GetEquippedDurability(slotUI.Slot);
                if (maxDurability > 0 && durability <= 0)
                    durability = maxDurability;

                bool showDurability = !string.IsNullOrWhiteSpace(itemId) && maxDurability > 0;
                slotUI.SetDurability(durability, showDurability ? maxDurability : 0);

                if (debugDurabilityRefresh)
                {
                    string itemLabel = string.IsNullOrWhiteSpace(itemId) ? "<empty>" : itemId;
                    Debug.Log($"[DurUI] Slot={slotUI.Slot} item={itemLabel} dur={durability}/{maxDurability} show={showDurability}");
                }

                lastRenderedSlotIds[slotUI.Slot] = itemId ?? string.Empty;
                lastRenderedSlotDurability[slotUI.Slot] = durability;
            }
        }

        private Sprite ResolveIcon(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out ItemDef def) && def != null)
                return def.Icon;

            if (equipmentNet != null && equipmentNet.TryGetItemDef(itemId, out ItemDef fallbackDef) && fallbackDef != null)
                return fallbackDef.Icon;

            return null;
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
                if (!lastRenderedSlotIds.TryGetValue(slotUI.Slot, out var lastId) || !string.Equals(latestId, lastId, StringComparison.Ordinal))
                    return true;

                int latestDurability = equipmentNet.GetEquippedDurability(slotUI.Slot);
                if (!lastRenderedSlotDurability.TryGetValue(slotUI.Slot, out var lastDurability) || latestDurability != lastDurability)
                    return true;
            }

            return false;
        }

        private void SubscribeToSlotNetVars()
        {
            if (equipmentNet == null)
                return;

            equipmentNet.MainHandNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.OffHandNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.HeadNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.ChestNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.LegsNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.FeetNetVar.OnValueChanged += OnAnyEquipItemChanged;

            equipmentNet.MainHandDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.OffHandDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.HeadDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.ChestDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.LegsDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.FeetDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
        }

        private void UnsubscribeFromSlotNetVars()
        {
            if (equipmentNet == null)
                return;

            equipmentNet.MainHandNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.OffHandNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.HeadNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.ChestNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.LegsNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.FeetNetVar.OnValueChanged -= OnAnyEquipItemChanged;

            equipmentNet.MainHandDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.OffHandDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.HeadDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.ChestDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.LegsDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.FeetDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
        }

        private void OnAnyEquipItemChanged(FixedString64Bytes previous, FixedString64Bytes next)
        {
            RefreshAll();
        }

        private void OnAnyEquipDurabilityChanged(int previous, int next)
        {
            RefreshAll();
        }
    }
}





