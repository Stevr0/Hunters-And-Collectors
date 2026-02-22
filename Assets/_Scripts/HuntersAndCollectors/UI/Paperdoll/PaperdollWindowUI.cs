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
    /// <summary>
    /// PaperdollWindowUI (Option A friendly)
    /// ---------------------------------------------------------
    /// IMPORTANT: Visibility is controlled by CharacterWindowRoot.
    /// This window does NOT SetActive(false) on itself.
    ///
    /// Responsibilities:
    /// - Bind to local owner PlayerEquipmentNet (+ optional inventory net)
    /// - Subscribe to OnEquipmentChanged
    /// - Refresh slot icons from replicated state
    /// - Send ServerRpc requests when owner clicks
    /// </summary>
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

        private void OnEnable()
        {
            // Because CharacterWindowRoot enables/disables us,
            // OnEnable is a good time to ensure we are bound.
            TryBindToLocalPlayer();

            // Even if we were already bound, refresh the UI when shown.
            RefreshAll();
        }

        private void OnDisable()
        {
            // We DO NOT unbind here.
            // Reason: if you hide/show frequently, re-subscribing every time is error-prone.
            // We unsubscribe only when destroyed.
        }

        private void OnDestroy()
        {
            Unbind();
        }

        /// <summary>
        /// Try to find the local owner's PlayerEquipmentNet (+ PlayerInventoryNet) in the scene and bind.
        /// This matches your inventory window's MVP approach.
        /// </summary>
        private void TryBindToLocalPlayer()
        {
            if (equipmentNet != null)
                return;

            // Find owner equipment net
            var equipments = FindObjectsOfType<PlayerEquipmentNet>(true);
            for (int i = 0; i < equipments.Length; i++)
            {
                var e = equipments[i];
                if (e != null && e.IsOwner)
                {
                    // Find owner inventory net (optional, but nice to have for future sync)
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

        /// <summary>
        /// Bind this window to a specific player's network components.
        /// </summary>
        public void Bind(PlayerEquipmentNet equipment, PlayerInventoryNet inventory, ItemDatabase dbOverride = null)
        {
            Unbind();

            equipmentNet = equipment;
            inventoryNet = inventory;

            if (dbOverride != null)
                itemDatabase = dbOverride;

            // Bind slot click callbacks.
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                    slots[i].Bind(this);
            }

            // Owner-only interaction.
            bool isOwner = equipmentNet != null && equipmentNet.IsOwner;
            bool canInteract = isOwner;

            if (!viewOnlyForNonOwner)
                canInteract = equipmentNet != null;

            SetSlotsInteractable(canInteract);

            // Subscribe to equipment changes (replication-driven).
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
                titleText.text = equipmentNet.IsOwner ? "Equipment" : "Equipment";

            for (int i = 0; i < slots.Count; i++)
            {
                var slotUI = slots[i];
                if (slotUI == null) continue;

                string itemId = equipmentNet.GetEquippedItemId(slotUI.Slot);
                slotUI.SetIcon(ResolveIcon(itemId));
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
    }
}
