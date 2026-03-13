using System;
using System.Collections.Generic;

namespace HuntersAndCollectors.Persistence
{
    [Serializable]
    public sealed class PlayerSaveData
    {
        public int schemaVersion = 2;
        public string playerKey = string.Empty;
        public WalletSaveData wallet = new();
        public List<SkillSaveData> skills = new();
        public List<KnownItemSaveData> knownItems = new();
        public InventoryGridSaveData inventory = new();
        public PlayerEquipmentSaveData equipment = new();
        public PlayerLocationSaveData location = new();
        public List<string> progressionFlags = new();
    }

    [Serializable]
    public sealed class WalletSaveData
    {
        public int coins;
    }

    [Serializable]
    public sealed class SkillSaveData
    {
        public string id = string.Empty;
        public int lvl;
        public int xp;
    }

    [Serializable]
    public sealed class KnownItemSaveData
    {
        public string id = string.Empty;
        public int @base;
    }

    [Serializable]
    public sealed class InventoryGridSaveData
    {
        public int w;
        public int h;
        public List<InventorySlotSaveData> slots = new();
    }

    [Serializable]
    public sealed class InventorySlotSaveData
    {
        public string kind = string.Empty;
        public string id = string.Empty;
        public int q;
        public long instanceId;
        public float rolledDamage;
        public float rolledDefence;
        public float rolledSwingSpeed;
        public float rolledMovementSpeed;
        public int maxDurability;
        public int currentDurability;
        public int bonusStrength;
        public int bonusDexterity;
        public int bonusIntelligence;
        public string craftedBy = string.Empty;
    }

    [Serializable]
    public sealed class ShardSaveData
    {
        public int schemaVersion = 2;
        public string shardKey = string.Empty;
        public List<ShelterSaveData> shelters = new();
        public List<PlacedBuildingSaveData> placedBuildings = new();
        public List<PlacedStorageChestSaveData> placedStorageChests = new();
        public List<GraveSaveData> graves = new();
        public List<BuildPieceSaveData> buildPieces = new();
    }

    [Serializable]
    public sealed class PlayerLocationSaveData
    {
        public bool hasSavedPosition;
        public string sceneName = string.Empty;
        public Vector3SaveData position = new();
        public float rotationY;
    }

    [Serializable]
    public sealed class PlayerEquipmentSaveData
    {
        public EquipmentSlotSaveData helmet;
        public EquipmentSlotSaveData chest;
        public EquipmentSlotSaveData legs;
        public EquipmentSlotSaveData boots;
        public EquipmentSlotSaveData gloves;
        public EquipmentSlotSaveData shoulders;
        public EquipmentSlotSaveData belt;
        public int mainHandInventorySlotRef = -1;
        public int offHandInventorySlotRef = -1;
        public string mainHandExpectedItemId = string.Empty;
        public string offHandExpectedItemId = string.Empty;
    }

    [Serializable]
    public sealed class EquipmentSlotSaveData
    {
        public string itemId = string.Empty;
        public int durability;
        public int maxDurability;
        public int bonusStrength;
        public int bonusDexterity;
        public int bonusIntelligence;
        public string craftedBy = string.Empty;
    }

    [Serializable]
    public sealed class ShelterSaveData
    {
        public string shelterId = string.Empty;
        public bool isComplete;
        public VendorSaveData vendor;
    }

    [Serializable]
    public sealed class VendorSaveData
    {
        public string vendorId = string.Empty;
        public string ownerPlayerKey = string.Empty;
        public string sceneName = string.Empty;
        public int treasuryCoins;
        public List<KnownItemSaveData> basePrices = new();
        public InventoryGridSaveData chest = new();
    }

    [Serializable]
    public sealed class BuildPieceSaveData
    {
        public string id = string.Empty;
        public Vector3SaveData pos = new();
        public float rotY;
    }

    [Serializable]
    public sealed class PlacedBuildingSaveData
    {
        public string persistentId = string.Empty;
        public string buildPieceId = string.Empty;
        public string sceneName = string.Empty;
        public Vector3SaveData position = new();
        public QuaternionSaveData rotation = new();
        public Vector3SaveData scale = new() { x = 1f, y = 1f, z = 1f };
        public ulong ownerPlayerId;
        public int currentHealth;
        public int maxHealth;
    }

    [Serializable]
    public sealed class PlacedStorageChestSaveData
    {
        public string persistentId = string.Empty;
        public string buildPieceId = string.Empty;
        public string sceneName = string.Empty;
        public Vector3SaveData position = new();
        public QuaternionSaveData rotation = new();
        public InventoryGridSaveData chest = new();
    }

    [Serializable]
    public sealed class GraveSaveData
    {
        public string persistentId = string.Empty;
        public string ownerPlayerKey = string.Empty;
        public string sceneName = string.Empty;
        public Vector3SaveData position = new();
        public QuaternionSaveData rotation = new();
        public InventoryGridSaveData inventory = new();
    }

    [Serializable]
    public sealed class Vector3SaveData
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class QuaternionSaveData
    {
        public float x;
        public float y;
        public float z;
        public float w = 1f;
    }

    [Serializable]
    public sealed class SchemaProbe
    {
        public int schemaVersion = -1;
    }

    public readonly struct SaveFileInfo
    {
        public SaveFileInfo(string key, string filePath, DateTime lastModifiedUtc, int schemaVersion, bool fileExists, bool isCorrupted, string parseStatus)
        {
            Key = key;
            FilePath = filePath;
            LastModifiedUtc = lastModifiedUtc;
            SchemaVersion = schemaVersion;
            FileExists = fileExists;
            IsCorrupted = isCorrupted;
            ParseStatus = parseStatus;
        }

        public string Key { get; }
        public string FilePath { get; }
        public DateTime LastModifiedUtc { get; }
        public int SchemaVersion { get; }
        public bool FileExists { get; }
        public bool IsCorrupted { get; }
        public string ParseStatus { get; }
    }
}


