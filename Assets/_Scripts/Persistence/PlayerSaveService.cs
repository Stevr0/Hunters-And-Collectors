using System;
using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using Unity.Collections;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    public sealed class PlayerSaveService
    {
        private readonly ItemDatabase itemDatabase;

        public PlayerSaveService(ItemDatabase itemDatabase)
        {
            this.itemDatabase = itemDatabase;
        }

        public PlayerSaveData LoadOrCreateAndApply(PlayerNetworkRoot playerRoot)
        {
            if (playerRoot == null)
                throw new ArgumentNullException(nameof(playerRoot));

            SavePaths.EnsureDirectories();

            string playerKey = playerRoot.PlayerKey;
            if (string.IsNullOrWhiteSpace(playerKey))
                playerKey = $"Client_{playerRoot.OwnerClientId}";

            string filePath = SavePaths.PlayerPath(playerKey);

            PlayerSaveData saveData;
            bool createFresh = false;
            bool migratedFromOldSchema = false;

            if (!System.IO.File.Exists(filePath))
            {
                Debug.Log($"[PlayerSaveService] No save found for {playerKey}. Creating default save.");
                saveData = CreateDefaultSave(playerRoot, playerKey);
                SavePaths.WriteJson(filePath, saveData);
            }
            else if (!SavePaths.TryReadJson(filePath, out saveData, out string readError) || saveData == null)
            {
                Debug.LogError($"[PlayerSaveService] Failed to parse player save '{filePath}'. Error: {readError}");
                ArchiveCorruptFile(filePath);
                createFresh = true;
                saveData = CreateDefaultSave(playerRoot, playerKey);
            }
            else
            {
                // v1 -> v2 migration is supported. Any other schema is archived.
                if (saveData.schemaVersion == 1)
                {
                    migratedFromOldSchema = true;
                    MigrateV1ToV2(saveData);
                    saveData.schemaVersion = SavePaths.CurrentSchemaVersion;
                }
                else if (saveData.schemaVersion != SavePaths.CurrentSchemaVersion)
                {
                    Debug.LogWarning($"[PlayerSaveService] Unsupported schemaVersion={saveData.schemaVersion} for {filePath}. Archiving and recreating.");
                    ArchiveCorruptFile(filePath);
                    createFresh = true;
                    saveData = CreateDefaultSave(playerRoot, playerKey);
                }
            }

            bool severeValidationFailure = !ValidateAndSanitize(saveData, playerRoot, out string validationMessage);
            if (severeValidationFailure)
            {
                Debug.LogError($"[PlayerSaveService] Severe validation failure for {filePath}. {validationMessage}");
                if (System.IO.File.Exists(filePath))
                    ArchiveCorruptFile(filePath);

                saveData = CreateDefaultSave(playerRoot, playerKey);
                createFresh = true;
            }

            ApplyToRuntime(playerRoot, saveData);

            if (createFresh || migratedFromOldSchema)
                SavePaths.WriteJson(filePath, saveData);

            return saveData;
        }

        public void Save(PlayerNetworkRoot playerRoot)
        {
            if (playerRoot == null)
                return;

            if (!playerRoot.IsServer)
                return;

            SavePaths.EnsureDirectories();
            string playerKey = string.IsNullOrWhiteSpace(playerRoot.PlayerKey)
                ? $"Client_{playerRoot.OwnerClientId}"
                : playerRoot.PlayerKey;

            string filePath = SavePaths.PlayerPath(playerKey);
            PlayerSaveData saveData = BuildFromRuntime(playerRoot, playerKey);
            SavePaths.WriteJson(filePath, saveData);
            Debug.Log($"[PlayerSaveService] Saved player '{playerKey}' to '{filePath}'.");
        }

        private PlayerSaveData BuildFromRuntime(PlayerNetworkRoot playerRoot, string playerKey)
        {
            var data = CreateDefaultSave(playerRoot, playerKey);

            data.wallet.coins = Mathf.Max(0, playerRoot.Wallet != null ? playerRoot.Wallet.Coins : 0);

            data.skills.Clear();
            if (playerRoot.Skills != null)
            {
                foreach (SkillEntry entry in playerRoot.Skills.Skills)
                {
                    data.skills.Add(new SkillSaveData
                    {
                        id = entry.Id.ToString(),
                        lvl = Mathf.Max(0, entry.Level),
                        xp = Mathf.Max(0, entry.Xp)
                    });
                }
            }

            data.knownItems.Clear();
            if (playerRoot.KnownItems != null)
            {
                foreach (KnownItemEntry entry in playerRoot.KnownItems.Entries)
                {
                    data.knownItems.Add(new KnownItemSaveData
                    {
                        id = entry.ItemId.ToString(),
                        @base = Mathf.Max(0, entry.BasePrice)
                    });
                }
            }

            data.inventory = BuildInventorySave(playerRoot.Inventory);
            return data;
        }

        private InventoryGridSaveData BuildInventorySave(PlayerInventoryNet inventoryNet)
        {
            var inventoryData = new InventoryGridSaveData
            {
                w = 8,
                h = 4,
                slots = new List<InventorySlotSaveData>()
            };

            if (inventoryNet == null || inventoryNet.Grid == null)
                return inventoryData;

            InventoryGrid grid = inventoryNet.Grid;
            inventoryData.w = grid.Width;
            inventoryData.h = grid.Height;
            inventoryData.slots = new List<InventorySlotSaveData>(grid.Slots.Length);

            for (int i = 0; i < grid.Slots.Length; i++)
            {
                InventorySlot slot = grid.Slots[i];
                if (slot.IsEmpty)
                {
                    inventoryData.slots.Add(null);
                    continue;
                }

                if (slot.ContentType == InventorySlotContentType.Instance)
                {
                    inventoryData.slots.Add(new InventorySlotSaveData
                    {
                        kind = "Instance",
                        id = slot.Instance.ItemId,
                        q = 1,
                        instanceId = slot.Instance.InstanceId,
                        rolledDamage = slot.Instance.RolledDamage,
                        rolledDefence = slot.Instance.RolledDefence,
                        rolledSwingSpeed = slot.Instance.RolledSwingSpeed,
                        rolledMovementSpeed = slot.Instance.RolledMovementSpeed,
                        maxDurability = slot.Instance.MaxDurability,
                        currentDurability = slot.Instance.CurrentDurability,
                        bonusStrength = slot.InstanceData.BonusStrength,
                        bonusDexterity = slot.InstanceData.BonusDexterity,
                        bonusIntelligence = slot.InstanceData.BonusIntelligence,
                        craftedBy = slot.InstanceData.CraftedBy.ToString()
                    });
                }
                else
                {
                    inventoryData.slots.Add(new InventorySlotSaveData
                    {
                        kind = "Stack",
                        id = slot.Stack.ItemId,
                        q = slot.Stack.Quantity
                    });
                }
            }

            return inventoryData;
        }

        private bool ValidateAndSanitize(PlayerSaveData data, PlayerNetworkRoot playerRoot, out string severeFailureReason)
        {
            severeFailureReason = string.Empty;
            if (data == null)
            {
                severeFailureReason = "Save data object was null.";
                return false;
            }

            if (data.wallet == null)
                data.wallet = new WalletSaveData();

            data.wallet.coins = Mathf.Max(0, data.wallet.coins);

            if (data.skills == null)
                data.skills = new List<SkillSaveData>();

            for (int i = 0; i < data.skills.Count; i++)
            {
                SkillSaveData row = data.skills[i];
                if (row == null)
                    continue;

                row.id = string.IsNullOrWhiteSpace(row.id) ? SkillId.Sales : row.id.Trim();
                row.lvl = Mathf.Max(0, row.lvl);
                row.xp = Mathf.Max(0, row.xp);
            }

            if (data.knownItems == null)
                data.knownItems = new List<KnownItemSaveData>();

            for (int i = data.knownItems.Count - 1; i >= 0; i--)
            {
                KnownItemSaveData row = data.knownItems[i];
                if (row == null || string.IsNullOrWhiteSpace(row.id))
                {
                    data.knownItems.RemoveAt(i);
                    continue;
                }

                row.id = row.id.Trim();
                row.@base = Mathf.Max(0, row.@base);

                if (itemDatabase != null && !itemDatabase.TryGet(row.id, out _))
                {
                    Debug.LogWarning($"[PlayerSaveService] Removing unknown known-item entry '{row.id}' from player '{playerRoot.PlayerKey}'.");
                    data.knownItems.RemoveAt(i);
                }
            }

            if (data.inventory == null)
            {
                severeFailureReason = "Inventory object missing.";
                return false;
            }

            if (data.inventory.w < 1 || data.inventory.h < 1)
            {
                severeFailureReason = $"Invalid inventory dimensions w={data.inventory.w}, h={data.inventory.h}.";
                return false;
            }

            int expectedCount = data.inventory.w * data.inventory.h;
            if (data.inventory.slots == null || data.inventory.slots.Count != expectedCount)
            {
                severeFailureReason = $"Inventory slot count mismatch. expected={expectedCount}, actual={(data.inventory.slots == null ? -1 : data.inventory.slots.Count)}.";
                return false;
            }

            for (int i = 0; i < data.inventory.slots.Count; i++)
            {
                InventorySlotSaveData slot = data.inventory.slots[i];
                if (slot == null)
                    continue;

                if (string.IsNullOrWhiteSpace(slot.id))
                {
                    data.inventory.slots[i] = null;
                    continue;
                }

                slot.id = slot.id.Trim();
                if (itemDatabase == null || !itemDatabase.TryGet(slot.id, out ItemDef def) || def == null)
                {
                    Debug.LogWarning($"[PlayerSaveService] Removing unknown inventory item '{slot.id}' from slot {i}.");
                    data.inventory.slots[i] = null;
                    continue;
                }

                bool instanceSave = string.Equals(slot.kind, "Instance", StringComparison.OrdinalIgnoreCase) || def.UsesItemInstance;
                if (instanceSave)
                {
                    slot.kind = "Instance";
                    slot.q = 1;
                    slot.maxDurability = slot.maxDurability > 0 ? slot.maxDurability : def.ResolveDurabilityMax();
                    if (slot.maxDurability < 0) slot.maxDurability = 0;

                    if (slot.maxDurability > 0)
                    {
                        if (slot.currentDurability <= 0)
                            slot.currentDurability = slot.maxDurability;

                        slot.currentDurability = Mathf.Clamp(slot.currentDurability, 1, slot.maxDurability);
                    }
                    else
                    {
                        slot.currentDurability = 0;
                    }
                }
                else
                {
                    slot.kind = "Stack";
                    if (slot.q < 1)
                    {
                        data.inventory.slots[i] = null;
                        continue;
                    }

                    int maxStack = Mathf.Max(1, def.MaxStack);
                    if (slot.q > maxStack)
                    {
                        Debug.LogWarning($"[PlayerSaveService] Clamping stack qty for {slot.id} in slot {i} from {slot.q} to {maxStack}.");
                        slot.q = maxStack;
                    }

                    // Clear stale instance-only fields for stack rows.
                    slot.instanceId = 0;
                    slot.rolledDamage = 0f;
                    slot.rolledDefence = 0f;
                    slot.rolledSwingSpeed = 0f;
                    slot.rolledMovementSpeed = 0f;
                    slot.maxDurability = 0;
                    slot.currentDurability = 0;
                    slot.bonusStrength = 0;
                    slot.bonusDexterity = 0;
                    slot.bonusIntelligence = 0;
                    slot.craftedBy = string.Empty;
                }
            }

            return true;
        }

        private static void MigrateV1ToV2(PlayerSaveData data)
        {
            if (data == null || data.inventory == null || data.inventory.slots == null)
                return;

            for (int i = 0; i < data.inventory.slots.Count; i++)
            {
                InventorySlotSaveData slot = data.inventory.slots[i];
                if (slot == null)
                    continue;

                // v1 only had id + q, so treat as stack row by default.
                slot.kind = "Stack";
                slot.instanceId = 0;
                slot.rolledDamage = 0f;
                slot.rolledDefence = 0f;
                slot.rolledSwingSpeed = 0f;
                slot.rolledMovementSpeed = 0f;
                slot.maxDurability = 0;
                slot.currentDurability = 0;
                slot.bonusStrength = 0;
                slot.bonusDexterity = 0;
                slot.bonusIntelligence = 0;
                slot.craftedBy = string.Empty;
            }
        }

        private void ApplyToRuntime(PlayerNetworkRoot playerRoot, PlayerSaveData data)
        {
            playerRoot.Wallet?.ServerSetCoins(Mathf.Max(0, data.wallet.coins));
            playerRoot.Skills?.ServerLoadEntries(data.skills);
            playerRoot.KnownItems?.ServerLoadEntries(data.knownItems);
            playerRoot.Inventory?.ServerLoadGrid(data.inventory);

            Debug.Log($"[PlayerSaveService] Applied save for player '{playerRoot.PlayerKey}'.");
        }

        private PlayerSaveData CreateDefaultSave(PlayerNetworkRoot playerRoot, string playerKey)
        {
            int width = 8;
            int height = 4;

            if (playerRoot != null && playerRoot.Inventory != null && playerRoot.Inventory.Grid != null)
            {
                width = Mathf.Max(1, playerRoot.Inventory.Grid.Width);
                height = Mathf.Max(1, playerRoot.Inventory.Grid.Height);
            }

            var data = new PlayerSaveData
            {
                schemaVersion = SavePaths.CurrentSchemaVersion,
                playerKey = playerKey,
                wallet = new WalletSaveData { coins = 100 },
                skills = BuildDefaultSkills(),
                knownItems = new List<KnownItemSaveData>(),
                inventory = new InventoryGridSaveData
                {
                    w = width,
                    h = height,
                    slots = CreateEmptySlots(width, height)
                }
            };

            return data;
        }

        private static List<SkillSaveData> BuildDefaultSkills()
        {
            string[] ids =
            {
                SkillId.Sales,
                SkillId.Negotiation,
                SkillId.Running,
                SkillId.Vitality,
                SkillId.Endurance,
                SkillId.Woodcutting,
                SkillId.Mining,
                SkillId.Foraging,
                SkillId.ToolCrafting,
                SkillId.EquipmentCrafting,
                SkillId.BuildingCrafting,
                SkillId.CombatAxe,
                SkillId.CombatPickaxe,
                SkillId.CombatKnife,
                SkillId.CombatClub,
                SkillId.CombatUnarmed
            };

            var result = new List<SkillSaveData>(ids.Length);
            for (int i = 0; i < ids.Length; i++)
                result.Add(new SkillSaveData { id = ids[i], lvl = 0, xp = 0 });

            return result;
        }

        private static List<InventorySlotSaveData> CreateEmptySlots(int width, int height)
        {
            int count = Mathf.Max(1, width) * Mathf.Max(1, height);
            var list = new List<InventorySlotSaveData>(count);
            for (int i = 0; i < count; i++)
                list.Add(null);

            return list;
        }

        private static void ArchiveCorruptFile(string filePath)
        {
            try
            {
                string archived = SavePaths.ArchiveWithTimestamp(filePath);
                Debug.LogWarning($"[PlayerSaveService] Archived invalid save: {archived}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerSaveService] Failed to archive corrupt file '{filePath}': {ex.Message}");
            }
        }
    }
}
