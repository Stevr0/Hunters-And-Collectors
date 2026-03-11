using System;
using System.Collections.Generic;
using HuntersAndCollectors.Building;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Storage;
using HuntersAndCollectors.Vendors;
using Unity.Netcode;
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

            VendorChestNet[] vendorChests = UnityEngine.Object.FindObjectsByType<VendorChestNet>(FindObjectsSortMode.None);
            for (int i = 0; i < vendorChests.Length; i++)
            {
                VendorChestNet chest = vendorChests[i];
                if (chest == null || !chest.IsSpawned || !chest.IsServer)
                    continue;

                string vendorId = string.IsNullOrWhiteSpace(chest.VendorId) ? $"VENDOR_{i:000}" : chest.VendorId;
                ShelterSaveData shelter = FindOrCreateShelter(snapshot.shelters, "SHELTER_AUTO_" + vendorId, vendorId);
                shelter.isComplete = true;
                shelter.vendor = chest.ServerExportSaveData();
            }

            if (snapshot.placedBuildings == null)
                snapshot.placedBuildings = new List<PlacedBuildingSaveData>();
            if (snapshot.placedStorageChests == null)
                snapshot.placedStorageChests = new List<PlacedStorageChestSaveData>();
            if (snapshot.buildPieces == null)
                snapshot.buildPieces = new List<BuildPieceSaveData>();

            snapshot.placedBuildings.Clear();
            snapshot.placedStorageChests.Clear();
            snapshot.buildPieces.Clear();

            // Persist only runtime-spawned placed pieces (not scene defaults).
            List<PlacedBuildPiece> pieces = PlacedBuildPieceRegistry.Snapshot();
            for (int i = 0; i < pieces.Count; i++)
            {
                PlacedBuildPiece piece = pieces[i];
                if (piece == null || !piece.IsSpawned || !piece.IsServer)
                    continue;

                NetworkObject networkObject = piece.NetworkObject;
                if (networkObject == null || !networkObject.IsSpawned)
                    continue;

                if (networkObject.IsSceneObject == true)
                    continue;

                snapshot.placedBuildings.Add(new PlacedBuildingSaveData
                {
                    persistentId = piece.PersistentId ?? string.Empty,
                    buildPieceId = piece.SourceItemId ?? string.Empty,
                    position = ToVector3Save(piece.transform.position),
                    rotation = ToQuaternionSave(piece.transform.rotation),
                    scale = ToVector3Save(piece.transform.localScale),
                    ownerPlayerId = piece.OwnerPlayerId,
                    currentHealth = Mathf.Max(0, piece.CurrentHealth),
                    maxHealth = Mathf.Max(1, piece.MaxHealth)
                });
            }

            List<StorageNet> placedStorages = PlacedStorageRegistry.Snapshot();
            int savedChestCount = 0;
            for (int i = 0; i < placedStorages.Count; i++)
            {
                StorageNet storage = placedStorages[i];
                if (storage == null || !storage.IsSpawned || !storage.IsServer)
                    continue;

                NetworkObject networkObject = storage.NetworkObject;
                if (networkObject == null || !networkObject.IsSpawned || networkObject.IsSceneObject == true)
                    continue;

                string persistentId = storage.PersistentId;
                if (string.IsNullOrWhiteSpace(persistentId))
                    continue;

                PlacedStorageChestSaveData savedChest = storage.ServerExportSaveData();
                snapshot.placedStorageChests.Add(savedChest);
                savedChestCount++;
                Debug.Log($"[ShardSave] Chest saved id={persistentId} nonEmptySlots={storage.CountNonEmptySlots()}");
            }

            Debug.Log($"[ShardSave] Saving placed chests count={savedChestCount}");

            // Keep legacy field populated for older tooling that still reads buildPieces.
            for (int i = 0; i < snapshot.placedBuildings.Count; i++)
            {
                PlacedBuildingSaveData placed = snapshot.placedBuildings[i];
                snapshot.buildPieces.Add(new BuildPieceSaveData
                {
                    id = placed.buildPieceId,
                    pos = placed.position,
                    rotY = ToQuaternion(placed.rotation).eulerAngles.y
                });
            }

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
            if (data.shelters != null)
            {
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
            }

            RestorePlacedBuildings(data);
            RestorePlacedStorageChests(data);
        }

        private void RestorePlacedBuildings(ShardSaveData data)
        {
            if (data == null)
                return;

            // Prevent duplicate runtime pieces on reload.
            DestroyExistingRuntimePlacedPieces();

            List<PlacedBuildingSaveData> source = data.placedBuildings;
            if ((source == null || source.Count == 0) && data.buildPieces != null && data.buildPieces.Count > 0)
                source = ConvertLegacyBuildPieces(data.buildPieces);

            if (source == null || source.Count == 0)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                PlacedBuildingSaveData saved = source[i];
                if (saved == null || string.IsNullOrWhiteSpace(saved.buildPieceId))
                    continue;

                if (itemDatabase == null || !itemDatabase.TryGet(saved.buildPieceId, out ItemDef itemDef) || itemDef == null || !itemDef.IsPlaceable || itemDef.PlaceablePrefab == null)
                {
                    Debug.LogWarning($"[ShardSaveService] Skipping unknown/invalid placed building id='{saved.buildPieceId}'.");
                    continue;
                }

                Vector3 pos = ToVector3(saved.position);
                Quaternion rot = ToQuaternion(saved.rotation);
                Vector3 scale = ToVector3(saved.scale);
                scale.x = Mathf.Clamp(Mathf.Abs(scale.x), 0.01f, 100f);
                scale.y = Mathf.Clamp(Mathf.Abs(scale.y), 0.01f, 100f);
                scale.z = Mathf.Clamp(Mathf.Abs(scale.z), 0.01f, 100f);

                NetworkObject spawnedNetworkObject = UnityEngine.Object.Instantiate(itemDef.PlaceablePrefab, pos, rot);
                if (spawnedNetworkObject == null)
                    continue;

                spawnedNetworkObject.transform.localScale = scale;

                PlacedBuildPiece placed = spawnedNetworkObject.GetComponent<PlacedBuildPiece>();
                if (placed != null)
                {
                    placed.ServerInitializeFromSave(
                        saved.persistentId,
                        itemDef,
                        saved.currentHealth,
                        saved.maxHealth,
                        saved.ownerPlayerId);
                }

                spawnedNetworkObject.Spawn(destroyWithScene: true);
            }
        }

        private void RestorePlacedStorageChests(ShardSaveData data)
        {
            List<PlacedStorageChestSaveData> source = data?.placedStorageChests;
            if (source == null)
                source = new List<PlacedStorageChestSaveData>();

            Debug.Log($"[ShardLoad] Restoring placed chests count={source.Count}");

            List<StorageNet> storages = PlacedStorageRegistry.Snapshot();
            var byId = new Dictionary<string, StorageNet>(StringComparer.Ordinal);
            for (int i = 0; i < storages.Count; i++)
            {
                StorageNet storage = storages[i];
                if (storage == null || !storage.IsServer || !storage.IsSpawned)
                    continue;

                string persistentId = storage.PersistentId;
                if (string.IsNullOrWhiteSpace(persistentId))
                    continue;

                byId[persistentId] = storage;
            }

            for (int i = 0; i < source.Count; i++)
            {
                PlacedStorageChestSaveData saved = source[i];
                if (saved == null || string.IsNullOrWhiteSpace(saved.persistentId))
                    continue;

                if (!byId.TryGetValue(saved.persistentId, out StorageNet storage) || storage == null)
                {
                    Debug.LogWarning($"[ShardLoad] Warning: no spawned chest matched saved id={saved.persistentId}");
                    continue;
                }

                storage.ServerApplySaveData(saved);
                Debug.Log($"[ShardLoad] Restored chest id={saved.persistentId} nonEmptySlots={storage.CountNonEmptySlots()}");
            }
        }

        private static List<PlacedBuildingSaveData> ConvertLegacyBuildPieces(List<BuildPieceSaveData> legacy)
        {
            var converted = new List<PlacedBuildingSaveData>(legacy.Count);
            for (int i = 0; i < legacy.Count; i++)
            {
                BuildPieceSaveData row = legacy[i];
                if (row == null || string.IsNullOrWhiteSpace(row.id))
                    continue;

                converted.Add(new PlacedBuildingSaveData
                {
                    persistentId = string.Empty,
                    buildPieceId = row.id,
                    position = row.pos ?? new Vector3SaveData(),
                    rotation = ToQuaternionSave(Quaternion.Euler(0f, row.rotY, 0f)),
                    scale = new Vector3SaveData { x = 1f, y = 1f, z = 1f },
                    ownerPlayerId = 0,
                    currentHealth = 0,
                    maxHealth = 0
                });
            }

            return converted;
        }

        private static void DestroyExistingRuntimePlacedPieces()
        {
            List<PlacedBuildPiece> existing = PlacedBuildPieceRegistry.Snapshot();
            for (int i = 0; i < existing.Count; i++)
            {
                PlacedBuildPiece piece = existing[i];
                if (piece == null || !piece.IsServer || !piece.IsSpawned)
                    continue;

                NetworkObject no = piece.NetworkObject;
                if (no == null || !no.IsSpawned)
                    continue;

                if (no.IsSceneObject == true)
                    continue;

                no.Despawn(destroy: true);
            }
        }

        private static void MigrateV1ToV2(ShardSaveData data)
        {
            if (data?.shelters != null)
            {
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

            if (data.placedBuildings == null)
                data.placedBuildings = new List<PlacedBuildingSaveData>();
            if (data.placedStorageChests == null)
                data.placedStorageChests = new List<PlacedStorageChestSaveData>();
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
            if (data.placedBuildings == null)
                data.placedBuildings = new List<PlacedBuildingSaveData>();
            if (data.placedStorageChests == null)
                data.placedStorageChests = new List<PlacedStorageChestSaveData>();
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

            for (int i = data.placedBuildings.Count - 1; i >= 0; i--)
            {
                PlacedBuildingSaveData piece = data.placedBuildings[i];
                if (piece == null || string.IsNullOrWhiteSpace(piece.buildPieceId))
                {
                    data.placedBuildings.RemoveAt(i);
                    continue;
                }

                piece.persistentId ??= string.Empty;
                piece.position ??= new Vector3SaveData();
                piece.rotation ??= new QuaternionSaveData { w = 1f };
                piece.scale ??= new Vector3SaveData { x = 1f, y = 1f, z = 1f };

                SanitizeVector(piece.position, 0f);
                SanitizeQuaternion(piece.rotation);
                SanitizeVector(piece.scale, 1f);

                piece.scale.x = Mathf.Clamp(Mathf.Abs(piece.scale.x), 0.01f, 100f);
                piece.scale.y = Mathf.Clamp(Mathf.Abs(piece.scale.y), 0.01f, 100f);
                piece.scale.z = Mathf.Clamp(Mathf.Abs(piece.scale.z), 0.01f, 100f);

                if (piece.maxHealth < 0) piece.maxHealth = 0;
                if (piece.currentHealth < 0) piece.currentHealth = 0;
            }

            for (int i = data.placedStorageChests.Count - 1; i >= 0; i--)
            {
                PlacedStorageChestSaveData chest = data.placedStorageChests[i];
                if (chest == null || string.IsNullOrWhiteSpace(chest.persistentId))
                {
                    data.placedStorageChests.RemoveAt(i);
                    continue;
                }

                chest.buildPieceId ??= string.Empty;
                chest.position ??= new Vector3SaveData();
                chest.rotation ??= new QuaternionSaveData { w = 1f };
                chest.chest ??= new InventoryGridSaveData { w = 4, h = 4, slots = new List<InventorySlotSaveData>() };
                SanitizeVector(chest.position, 0f);
                SanitizeQuaternion(chest.rotation);
                ValidateInventoryGrid(chest.chest);
            }

            for (int i = data.buildPieces.Count - 1; i >= 0; i--)
            {
                BuildPieceSaveData piece = data.buildPieces[i];
                if (piece == null || string.IsNullOrWhiteSpace(piece.id))
                {
                    data.buildPieces.RemoveAt(i);
                    continue;
                }

                piece.pos ??= new Vector3SaveData();
                SanitizeVector(piece.pos, 0f);
                if (!float.IsFinite(piece.rotY))
                    piece.rotY = 0f;
            }

            return true;
        }

        private void ValidateInventoryGrid(InventoryGridSaveData grid)
        {
            if (grid == null)
                return;

            grid.w = Mathf.Max(1, grid.w);
            grid.h = Mathf.Max(1, grid.h);
            int expected = grid.w * grid.h;
            if (grid.slots == null)
                grid.slots = new List<InventorySlotSaveData>(expected);

            while (grid.slots.Count < expected)
                grid.slots.Add(null);
            while (grid.slots.Count > expected)
                grid.slots.RemoveAt(grid.slots.Count - 1);

            for (int i = 0; i < grid.slots.Count; i++)
            {
                InventorySlotSaveData slot = grid.slots[i];
                if (slot == null || string.IsNullOrWhiteSpace(slot.id))
                {
                    grid.slots[i] = null;
                    continue;
                }

                slot.id = slot.id.Trim();
                if (itemDatabase == null || !itemDatabase.TryGet(slot.id, out ItemDef def) || def == null)
                {
                    grid.slots[i] = null;
                    continue;
                }

                bool asInstance = string.Equals(slot.kind, "Instance", StringComparison.OrdinalIgnoreCase) || def.UsesItemInstance;
                if (asInstance)
                {
                    slot.kind = "Instance";
                    slot.q = 1;
                    slot.maxDurability = slot.maxDurability > 0 ? slot.maxDurability : def.ResolveDurabilityMax();
                    if (slot.maxDurability < 0) slot.maxDurability = 0;
                    if (slot.maxDurability > 0)
                    {
                        int current = slot.currentDurability > 0 ? slot.currentDurability : slot.maxDurability;
                        slot.currentDurability = Mathf.Clamp(current, 1, slot.maxDurability);
                    }
                    else
                    {
                        slot.currentDurability = 0;
                    }
                    continue;
                }

                slot.kind = "Stack";
                if (slot.q < 1)
                {
                    grid.slots[i] = null;
                    continue;
                }

                slot.q = Mathf.Clamp(slot.q, 1, Mathf.Max(1, def.MaxStack));
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
                placedBuildings = new List<PlacedBuildingSaveData>(),
                placedStorageChests = new List<PlacedStorageChestSaveData>(),
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

        private static Vector3SaveData ToVector3Save(Vector3 value)
        {
            return new Vector3SaveData { x = value.x, y = value.y, z = value.z };
        }

        private static QuaternionSaveData ToQuaternionSave(Quaternion value)
        {
            return new QuaternionSaveData { x = value.x, y = value.y, z = value.z, w = value.w };
        }

        private static Vector3 ToVector3(Vector3SaveData value)
        {
            if (value == null)
                return Vector3.zero;

            float x = float.IsFinite(value.x) ? value.x : 0f;
            float y = float.IsFinite(value.y) ? value.y : 0f;
            float z = float.IsFinite(value.z) ? value.z : 0f;
            return new Vector3(x, y, z);
        }

        private static Quaternion ToQuaternion(QuaternionSaveData value)
        {
            if (value == null)
                return Quaternion.identity;

            Quaternion q = new Quaternion(
                float.IsFinite(value.x) ? value.x : 0f,
                float.IsFinite(value.y) ? value.y : 0f,
                float.IsFinite(value.z) ? value.z : 0f,
                float.IsFinite(value.w) ? value.w : 1f);

            if ((q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) < 0.0001f)
                return Quaternion.identity;

            q.Normalize();
            return q;
        }

        private static void SanitizeVector(Vector3SaveData value, float fallback)
        {
            if (value == null)
                return;

            if (!float.IsFinite(value.x)) value.x = fallback;
            if (!float.IsFinite(value.y)) value.y = fallback;
            if (!float.IsFinite(value.z)) value.z = fallback;
        }

        private static void SanitizeQuaternion(QuaternionSaveData value)
        {
            if (value == null)
                return;

            if (!float.IsFinite(value.x)) value.x = 0f;
            if (!float.IsFinite(value.y)) value.y = 0f;
            if (!float.IsFinite(value.z)) value.z = 0f;
            if (!float.IsFinite(value.w)) value.w = 1f;

            Quaternion q = new Quaternion(value.x, value.y, value.z, value.w);
            if ((q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w) < 0.0001f)
            {
                value.x = 0f;
                value.y = 0f;
                value.z = 0f;
                value.w = 1f;
                return;
            }

            q.Normalize();
            value.x = q.x;
            value.y = q.y;
            value.z = q.z;
            value.w = q.w;
        }
    }
}

