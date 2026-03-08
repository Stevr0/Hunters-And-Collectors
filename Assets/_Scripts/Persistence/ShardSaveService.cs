using System;
using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Vendors;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    public sealed class ShardSaveService
    {
        private readonly ItemDatabase itemDatabase;
        private ShardSaveData runtimeState;

        public ShardSaveService(ItemDatabase itemDatabase)
        {
            this.itemDatabase = itemDatabase;
        }

        public ShardSaveData LoadOrCreateAndApply(string shardKey)
        {
            SavePaths.EnsureDirectories();
            string filePath = SavePaths.ShardPath(shardKey);
            bool createFresh = false;
            bool migratedFromOldSchema = false;
            ShardSaveData data;

            if (!System.IO.File.Exists(filePath))
            {
                data = CreateDefault(shardKey);
                createFresh = true;
                Debug.Log($"[ShardSaveService] No shard save found for '{shardKey}'. Creating default save.");
            }
            else if (!SavePaths.TryReadJson(filePath, out data, out string error) || data == null)
            {
                Debug.LogError($"[ShardSaveService] Failed to parse shard save '{filePath}'. Error: {error}");
                ArchiveCorrupt(filePath);
                data = CreateDefault(shardKey);
                createFresh = true;
            }
            else if (data.schemaVersion == 1)
            {
                migratedFromOldSchema = true;
                MigrateV1ToV2(data);
                data.schemaVersion = SavePaths.CurrentSchemaVersion;
            }
            else if (data.schemaVersion != SavePaths.CurrentSchemaVersion)
            {
                Debug.LogWarning($"[ShardSaveService] Unsupported schemaVersion={data.schemaVersion} for '{filePath}'. Archiving and creating new shard.");
                ArchiveCorrupt(filePath);
                data = CreateDefault(shardKey);
                createFresh = true;
            }

            if (!ValidateAndSanitize(data, out string validationError))
            {
                Debug.LogError($"[ShardSaveService] Severe shard save corruption in '{filePath}'. {validationError}");
                if (System.IO.File.Exists(filePath))
                    ArchiveCorrupt(filePath);
                data = CreateDefault(shardKey);
                createFresh = true;
            }

            ApplyToRuntime(data);
            runtimeState = data;

            if (createFresh || migratedFromOldSchema)
                SavePaths.WriteJson(filePath, data);

            return data;
        }

        public void Save(string shardKey)
        {
            SavePaths.EnsureDirectories();
            string filePath = SavePaths.ShardPath(shardKey);
            ShardSaveData snapshot = BuildFromRuntime(shardKey);
            SavePaths.WriteJson(filePath, snapshot);
            runtimeState = snapshot;
            Debug.Log($"[ShardSaveService] Saved shard '{shardKey}' to '{filePath}'.");
        }

        private ShardSaveData BuildFromRuntime(string shardKey)
        {
            var snapshot = runtimeState ?? CreateDefault(shardKey);
            snapshot.schemaVersion = SavePaths.CurrentSchemaVersion;
            snapshot.shardKey = shardKey;

            if (snapshot.shelters == null)
                snapshot.shelters = new List<ShelterSaveData>();

            VendorChestNet[] chests = UnityEngine.Object.FindObjectsByType<VendorChestNet>(FindObjectsSortMode.None);
            for (int i = 0; i < chests.Length; i++)
            {
                VendorChestNet chest = chests[i];
                if (chest == null || !chest.IsSpawned || !chest.IsServer)
                    continue;

                string vendorId = string.IsNullOrWhiteSpace(chest.VendorId) ? $"VENDOR_{i:000}" : chest.VendorId;
                ShelterSaveData shelter = FindOrCreateShelter(snapshot.shelters, "SHELTER_AUTO_" + vendorId, vendorId);
                shelter.isComplete = true;
                shelter.vendor = chest.ServerExportSaveData();
            }

            if (snapshot.buildPieces == null)
                snapshot.buildPieces = new List<BuildPieceSaveData>();

            // TODO: Hook real build piece runtime list when BuildingNet/build registry exists.
            return snapshot;
        }

        private static ShelterSaveData FindOrCreateShelter(List<ShelterSaveData> shelters, string shelterId, string vendorId)
        {
            for (int i = 0; i < shelters.Count; i++)
            {
                ShelterSaveData shelter = shelters[i];
                if (shelter == null)
                    continue;

                if (string.Equals(shelter.shelterId, shelterId, StringComparison.Ordinal))
                    return shelter;

                if (shelter.vendor != null && string.Equals(shelter.vendor.vendorId, vendorId, StringComparison.Ordinal))
                    return shelter;
            }

            var created = new ShelterSaveData
            {
                shelterId = shelterId,
                isComplete = true,
                vendor = new VendorSaveData
                {
                    vendorId = vendorId,
                    ownerPlayerKey = string.Empty,
                    treasuryCoins = 0,
                    basePrices = new List<KnownItemSaveData>(),
                    chest = new InventoryGridSaveData { w = 4, h = 4, slots = new List<InventorySlotSaveData>() }
                }
            };

            shelters.Add(created);
            return created;
        }

        private void ApplyToRuntime(ShardSaveData data)
        {
            if (data.shelters == null)
                return;

            VendorChestNet[] chests = UnityEngine.Object.FindObjectsByType<VendorChestNet>(FindObjectsSortMode.None);
            var byVendorId = new Dictionary<string, VendorChestNet>(StringComparer.Ordinal);
            for (int i = 0; i < chests.Length; i++)
            {
                VendorChestNet chest = chests[i];
                if (chest == null || !chest.IsSpawned || !chest.IsServer)
                    continue;

                if (!string.IsNullOrWhiteSpace(chest.VendorId))
                    byVendorId[chest.VendorId] = chest;
            }

            for (int i = 0; i < data.shelters.Count; i++)
            {
                ShelterSaveData shelter = data.shelters[i];
                if (shelter?.vendor == null || string.IsNullOrWhiteSpace(shelter.vendor.vendorId))
                    continue;

                if (!byVendorId.TryGetValue(shelter.vendor.vendorId, out VendorChestNet chest))
                    continue;

                chest.ServerApplySaveData(shelter.vendor, itemDatabase);
            }

            // TODO: Hook build pieces and shelter completion flags to gameplay systems when those runtime systems exist.
        }


        private static void MigrateV1ToV2(ShardSaveData data)
        {
            if (data?.shelters == null)
                return;

            for (int i = 0; i < data.shelters.Count; i++)
            {
                VendorSaveData vendor = data.shelters[i]?.vendor;
                if (vendor?.chest?.slots == null)
                    continue;

                for (int s = 0; s < vendor.chest.slots.Count; s++)
                {
                    InventorySlotSaveData slot = vendor.chest.slots[s];
                    if (slot == null)
                        continue;

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
        }
        private bool ValidateAndSanitize(ShardSaveData data, out string severeFailureReason)
        {
            severeFailureReason = string.Empty;

            if (data == null)
            {
                severeFailureReason = "Shard save object was null.";
                return false;
            }

            if (data.shelters == null)
                data.shelters = new List<ShelterSaveData>();

            if (data.buildPieces == null)
                data.buildPieces = new List<BuildPieceSaveData>();

            for (int i = 0; i < data.shelters.Count; i++)
            {
                ShelterSaveData shelter = data.shelters[i];
                if (shelter == null)
                {
                    data.shelters.RemoveAt(i--);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(shelter.shelterId))
                    shelter.shelterId = $"SHELTER_{i:000}";

                if (shelter.vendor == null)
                    continue;

                ValidateVendor(shelter.vendor, ref severeFailureReason);
                if (!string.IsNullOrEmpty(severeFailureReason))
                    return false;
            }

            for (int i = 0; i < data.buildPieces.Count; i++)
            {
                BuildPieceSaveData piece = data.buildPieces[i];
                if (piece == null || string.IsNullOrWhiteSpace(piece.id))
                {
                    data.buildPieces.RemoveAt(i--);
                    continue;
                }

                if (piece.pos == null)
                    piece.pos = new Vector3SaveData();
            }

            return true;
        }

        private static void ValidateVendor(VendorSaveData vendor, ref string severeFailureReason)
        {
            vendor.treasuryCoins = Mathf.Max(0, vendor.treasuryCoins);

            if (vendor.basePrices == null)
                vendor.basePrices = new List<KnownItemSaveData>();

            for (int i = vendor.basePrices.Count - 1; i >= 0; i--)
            {
                KnownItemSaveData basePrice = vendor.basePrices[i];
                if (basePrice == null || string.IsNullOrWhiteSpace(basePrice.id))
                {
                    vendor.basePrices.RemoveAt(i);
                    continue;
                }

                basePrice.@base = Mathf.Max(0, basePrice.@base);
            }

            if (vendor.chest == null)
            {
                severeFailureReason = "Vendor chest payload missing.";
                return;
            }

            if (vendor.chest.w < 1 || vendor.chest.h < 1)
            {
                severeFailureReason = "Vendor chest dimensions are invalid.";
                return;
            }

            int expected = vendor.chest.w * vendor.chest.h;
            if (vendor.chest.slots == null || vendor.chest.slots.Count != expected)
            {
                severeFailureReason = $"Vendor chest slot count mismatch expected={expected}.";
            }
        }

        private static ShardSaveData CreateDefault(string shardKey)
        {
            return new ShardSaveData
            {
                schemaVersion = SavePaths.CurrentSchemaVersion,
                shardKey = shardKey,
                shelters = new List<ShelterSaveData>(),
                buildPieces = new List<BuildPieceSaveData>()
            };
        }

        private static void ArchiveCorrupt(string filePath)
        {
            try
            {
                string archived = SavePaths.ArchiveWithTimestamp(filePath);
                Debug.LogWarning($"[ShardSaveService] Archived invalid save: {archived}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ShardSaveService] Failed to archive corrupt file '{filePath}': {ex.Message}");
            }
        }
    }
}

