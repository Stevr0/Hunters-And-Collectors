using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
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
        private Action<InventorySnapshot> inventorySnapshotChangedHandler;
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

                if (inventoryNet != null)
                {
                    inventorySnapshotChangedHandler = _ => RefreshAll();
                    inventoryNet.OnSnapshotChanged += inventorySnapshotChangedHandler;
                }
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

                if (inventoryNet != null && inventorySnapshotChangedHandler != null)
                    inventoryNet.OnSnapshotChanged -= inventorySnapshotChangedHandler;
            }

            equipmentChangedHandler = null;
            inventorySnapshotChangedHandler = null;
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
                EquipmentSlotUI slotUI = slots[i];
                if (slotUI == null)
                    continue;

                bool isReferenceSlot = equipmentNet.IsReferenceEquipSlot(slotUI.Slot);

                string itemId = string.Empty;
                int durability = 0;
                int maxDurability = 0;
                ItemTooltipData tooltip = default;

                if (isReferenceSlot)
                {
                    // Reference slot: render ONLY from inventory snapshot + reference index.
                    // Never mix in moved-equipment payload values for hand slots.
                    TryResolveReferenceVisualFromInventory(slotUI.Slot, out itemId, out durability, out maxDurability, out tooltip);
                }
                else
                {
                    // Moved-equipment slot (armor): render from equipment net state.
                    itemId = equipmentNet.GetEquippedItemId(slotUI.Slot);
                    durability = equipmentNet.GetEquippedDurability(slotUI.Slot);
                    tooltip = BuildTooltipData(slotUI.Slot, itemId);
                    maxDurability = ResolveMaxDurabilityFromItem(itemId);
                }

                Sprite icon = ResolveIcon(itemId);

                // Always overwrite all visuals from one source path.
                slotUI.SetEquippedItemCache(itemId, icon);
                slotUI.SetTooltipData(tooltip);
                slotUI.SetIcon(icon);

                bool showDurability = !string.IsNullOrWhiteSpace(itemId) && maxDurability > 0 && durability > 0;
                slotUI.SetDurability(durability, showDurability ? maxDurability : 0);

                if (debugDurabilityRefresh)
                {
                    string itemLabel = string.IsNullOrWhiteSpace(itemId) ? "<empty>" : itemId;
                    string source = isReferenceSlot ? "ReferenceInventory" : "MovedEquipment";
                    Debug.Log($"[DurUI] Slot={slotUI.Slot} source={source} item={itemLabel} dur={durability}/{maxDurability} show={showDurability}");
                }

                lastRenderedSlotIds[slotUI.Slot] = itemId ?? string.Empty;
                lastRenderedSlotDurability[slotUI.Slot] = durability;
            }
        }
        private bool TryResolveReferenceVisualFromInventory(EquipSlot slot, out string itemId, out int durability, out int maxDurability, out ItemTooltipData tooltip)
        {
            itemId = string.Empty;
            durability = 0;
            maxDurability = 0;
            tooltip = default;

            if (equipmentNet == null || inventoryNet == null)
                return false;

            int inventoryIndex = equipmentNet.GetReferenceInventorySlotIndex(slot);
            if (inventoryIndex < 0)
                return false;

            InventorySnapshot snapshot = inventoryNet.LastSnapshot;
            if (snapshot.Slots == null || inventoryIndex >= snapshot.Slots.Length)
                return false;

            InventorySnapshot.SlotDto sourceSlot = snapshot.Slots[inventoryIndex];
            if (sourceSlot.IsEmpty || sourceSlot.Quantity <= 0)
                return false;

            itemId = sourceSlot.ItemId.ToString();
            durability = sourceSlot.Durability;
            maxDurability = sourceSlot.MaxDurability;

            tooltip = new ItemTooltipData
            {
                ItemId = itemId,
                Durability = durability,
                MaxDurability = maxDurability,
                BonusStrength = sourceSlot.BonusStrength,
                BonusDexterity = sourceSlot.BonusDexterity,
                BonusIntelligence = sourceSlot.BonusIntelligence,
                CraftedBy = sourceSlot.CraftedBy.ToString(),
                InstanceId = sourceSlot.InstanceId,
                RolledDamage = sourceSlot.RolledDamage,
                RolledDefence = sourceSlot.RolledDefence,
                RolledSwingSpeed = sourceSlot.RolledSwingSpeed,
                RolledMovementSpeed = sourceSlot.RolledMovementSpeed,
                RolledCastSpeed = sourceSlot.RolledCastSpeed,
                RolledBlockValue = sourceSlot.RolledBlockValue,
                DamageBonus = sourceSlot.DamageBonus,
                DefenceBonus = sourceSlot.DefenceBonus,
                AttackSpeedBonus = sourceSlot.AttackSpeedBonus,
                CastSpeedBonus = sourceSlot.CastSpeedBonus,
                CritChance = sourceSlot.CritChanceBonus,
                CritChanceBonus = sourceSlot.CritChanceBonus,
                BlockValueBonus = sourceSlot.BlockValueBonus,
                StatusPower = sourceSlot.StatusPowerBonus,
                StatusPowerBonus = sourceSlot.StatusPowerBonus,
                TrapPower = sourceSlot.TrapPowerBonus,
                TrapPowerBonus = sourceSlot.TrapPowerBonus,
                PhysicalResist = sourceSlot.PhysicalResist,
                FireResist = sourceSlot.FireResist,
                FrostResist = sourceSlot.FrostResist,
                PoisonResist = sourceSlot.PoisonResist,
                LightningResist = sourceSlot.LightningResist,
                AffixA = (ItemAffixId)sourceSlot.AffixA,
                AffixB = (ItemAffixId)sourceSlot.AffixB,
                AffixC = (ItemAffixId)sourceSlot.AffixC,
                ResistanceAffix = (ResistanceAffixId)sourceSlot.ResistanceAffix
            };

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out ItemDef def) && def != null)
            {
                tooltip.DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.ItemId : def.DisplayName;
                tooltip.Description = def.Description;
                tooltip.ItemTier = def.ItemTier;
                tooltip.CombatFamily = def.CombatFamily;
                tooltip.ItemStatBias = def.ItemStatBias;
                tooltip.Damage = sourceSlot.ContentType == InventorySlotContentType.Instance && sourceSlot.RolledDamage > 0f ? sourceSlot.RolledDamage + sourceSlot.DamageBonus : def.Damage + sourceSlot.DamageBonus;
                tooltip.Defence = sourceSlot.ContentType == InventorySlotContentType.Instance && sourceSlot.RolledDefence > 0f ? sourceSlot.RolledDefence + sourceSlot.DefenceBonus : def.Defence + sourceSlot.DefenceBonus;
                tooltip.AttackBonus = def.AttackBonus;
                tooltip.SwingSpeed = sourceSlot.ContentType == InventorySlotContentType.Instance && sourceSlot.RolledSwingSpeed > 0f ? sourceSlot.RolledSwingSpeed + sourceSlot.AttackSpeedBonus : def.SwingSpeed + sourceSlot.AttackSpeedBonus;
                tooltip.MoveSpeed = sourceSlot.ContentType == InventorySlotContentType.Instance && sourceSlot.RolledMovementSpeed > 0f ? sourceSlot.RolledMovementSpeed : def.MovementSpeed;
                tooltip.CastSpeed = sourceSlot.ContentType == InventorySlotContentType.Instance && sourceSlot.RolledCastSpeed > 0f ? sourceSlot.RolledCastSpeed + sourceSlot.CastSpeedBonus : def.CastSpeed + sourceSlot.CastSpeedBonus;
                tooltip.BlockValue = sourceSlot.ContentType == InventorySlotContentType.Instance && sourceSlot.RolledBlockValue > 0 ? sourceSlot.RolledBlockValue + sourceSlot.BlockValueBonus : def.BlockValue + sourceSlot.BlockValueBonus;
                tooltip.Strength = Mathf.Max(0, def.Strength) + sourceSlot.BonusStrength;
                tooltip.Dexterity = Mathf.Max(0, def.Dexterity) + sourceSlot.BonusDexterity;
                tooltip.Intelligence = Mathf.Max(0, def.Intelligence) + sourceSlot.BonusIntelligence;

                // Fallback only for missing snapshot max values.
                if (maxDurability <= 0)
                    maxDurability = Mathf.Max(0, def.MaxDurability);
            }

            if (debugDurabilityRefresh)
            {
                Debug.Log($"[DurRef] slot={slot} invIndex={inventoryIndex} item={itemId} dur={durability}/{maxDurability}");
            }

            return true;
        }
        private int ResolveMaxDurabilityFromItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return 0;

            if (itemDatabase != null && itemDatabase.TryGet(itemId, out var defFromDb) && defFromDb != null)
                return Mathf.Max(0, defFromDb.MaxDurability);

            if (equipmentNet != null && equipmentNet.TryGetItemDef(itemId, out var defFromEquip) && defFromEquip != null)
                return Mathf.Max(0, defFromEquip.MaxDurability);

            return 0;
        }
        private ItemTooltipData BuildTooltipData(EquipSlot slot, string itemId)
        {
            ItemInstanceData instanceData = equipmentNet != null ? equipmentNet.GetEquippedInstanceData(slot) : default;
            ItemTooltipData data = new ItemTooltipData
            {
                ItemId = itemId,
                Durability = equipmentNet != null ? equipmentNet.GetEquippedDurability(slot) : 0,
                MaxDurability = instanceData.MaxDurability,
                BonusStrength = instanceData.BonusStrength,
                BonusDexterity = instanceData.BonusDexterity,
                BonusIntelligence = instanceData.BonusIntelligence,
                CraftedBy = instanceData.CraftedBy.ToString(),
                InstanceId = instanceData.InstanceId,
                RolledDamage = instanceData.RolledDamage,
                RolledDefence = instanceData.RolledDefence,
                RolledSwingSpeed = instanceData.RolledSwingSpeed,
                RolledMovementSpeed = instanceData.RolledMovementSpeed,
                RolledCastSpeed = instanceData.RolledCastSpeed,
                RolledBlockValue = instanceData.RolledBlockValue,
                DamageBonus = instanceData.DamageBonus,
                DefenceBonus = instanceData.DefenceBonus,
                AttackSpeedBonus = instanceData.AttackSpeedBonus,
                CastSpeedBonus = instanceData.CastSpeedBonus,
                CritChance = instanceData.CritChanceBonus,
                CritChanceBonus = instanceData.CritChanceBonus,
                BlockValueBonus = instanceData.BlockValueBonus,
                StatusPower = instanceData.StatusPowerBonus,
                StatusPowerBonus = instanceData.StatusPowerBonus,
                TrapPower = instanceData.TrapPowerBonus,
                TrapPowerBonus = instanceData.TrapPowerBonus,
                PhysicalResist = instanceData.PhysicalResist,
                FireResist = instanceData.FireResist,
                FrostResist = instanceData.FrostResist,
                PoisonResist = instanceData.PoisonResist,
                LightningResist = instanceData.LightningResist,
                AffixA = instanceData.AffixA,
                AffixB = instanceData.AffixB,
                AffixC = instanceData.AffixC,
                ResistanceAffix = instanceData.ResistanceAffix
            };

            if (string.IsNullOrWhiteSpace(itemId))
                return data;

            ItemDef def = null;
            if (itemDatabase != null)
                itemDatabase.TryGet(itemId, out def);

            if (def == null && equipmentNet != null)
                equipmentNet.TryGetItemDef(itemId, out def);

            if (def == null)
                return data;

            data.DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.ItemId : def.DisplayName;
            data.Description = def.Description;
            data.ItemTier = def.ItemTier;
            data.CombatFamily = def.CombatFamily;
            data.ItemStatBias = def.ItemStatBias;
            data.Damage = instanceData.RolledDamage > 0f ? instanceData.RolledDamage + instanceData.DamageBonus : def.Damage + instanceData.DamageBonus;
            data.Defence = instanceData.RolledDefence > 0f ? instanceData.RolledDefence + instanceData.DefenceBonus : def.Defence + instanceData.DefenceBonus;
            data.AttackBonus = def.AttackBonus;
            data.SwingSpeed = instanceData.RolledSwingSpeed > 0f ? instanceData.RolledSwingSpeed + instanceData.AttackSpeedBonus : def.SwingSpeed + instanceData.AttackSpeedBonus;
            data.MoveSpeed = instanceData.RolledMovementSpeed > 0f ? instanceData.RolledMovementSpeed : def.MovementSpeed;
            data.CastSpeed = instanceData.RolledCastSpeed > 0f ? instanceData.RolledCastSpeed + instanceData.CastSpeedBonus : def.CastSpeed + instanceData.CastSpeedBonus;
            data.BlockValue = instanceData.RolledBlockValue > 0 ? instanceData.RolledBlockValue + instanceData.BlockValueBonus : def.BlockValue + instanceData.BlockValueBonus;
            data.Strength = Mathf.Max(0, def.Strength) + data.BonusStrength;
            data.Dexterity = Mathf.Max(0, def.Dexterity) + data.BonusDexterity;
            data.Intelligence = Mathf.Max(0, def.Intelligence) + data.BonusIntelligence;
            if (data.MaxDurability <= 0)
                data.MaxDurability = Mathf.Max(0, def.MaxDurability);

            return data;
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
                EquipmentSlotUI slotUI = slots[i];
                if (slotUI == null)
                    continue;

                bool isReferenceSlot = equipmentNet.IsReferenceEquipSlot(slotUI.Slot);

                string latestId;
                int latestDurability;

                if (isReferenceSlot)
                {
                    // Reference slots are driven from inventory snapshot only.
                    if (TryResolveReferenceVisualFromInventory(slotUI.Slot, out string refItemId, out int refDurability, out _, out _))
                    {
                        latestId = refItemId ?? string.Empty;
                        latestDurability = refDurability;
                    }
                    else
                    {
                        latestId = string.Empty;
                        latestDurability = 0;
                    }
                }
                else
                {
                    latestId = equipmentNet.GetEquippedItemId(slotUI.Slot) ?? string.Empty;
                    latestDurability = equipmentNet.GetEquippedDurability(slotUI.Slot);
                }

                if (!lastRenderedSlotIds.TryGetValue(slotUI.Slot, out string lastId) || !string.Equals(latestId, lastId, StringComparison.Ordinal))
                    return true;

                if (!lastRenderedSlotDurability.TryGetValue(slotUI.Slot, out int lastDurability) || latestDurability != lastDurability)
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
            equipmentNet.HelmetNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.ChestNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.LegsNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.BootsNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.GlovesNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.ShouldersNetVar.OnValueChanged += OnAnyEquipItemChanged;
            equipmentNet.BeltNetVar.OnValueChanged += OnAnyEquipItemChanged;

            equipmentNet.MainHandDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.OffHandDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.HelmetDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.ChestDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.LegsDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.BootsDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.GlovesDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.ShouldersDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
            equipmentNet.BeltDurabilityNetVar.OnValueChanged += OnAnyEquipDurabilityChanged;
        }

        private void UnsubscribeFromSlotNetVars()
        {
            if (equipmentNet == null)
                return;

            equipmentNet.MainHandNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.OffHandNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.HelmetNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.ChestNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.LegsNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.BootsNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.GlovesNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.ShouldersNetVar.OnValueChanged -= OnAnyEquipItemChanged;
            equipmentNet.BeltNetVar.OnValueChanged -= OnAnyEquipItemChanged;

            equipmentNet.MainHandDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.OffHandDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.HelmetDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.ChestDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.LegsDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.BootsDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.GlovesDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.ShouldersDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
            equipmentNet.BeltDurabilityNetVar.OnValueChanged -= OnAnyEquipDurabilityChanged;
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



























