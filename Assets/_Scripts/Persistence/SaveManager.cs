using System.Collections;
using System.Collections.Generic;
using HuntersAndCollectors.Bootstrap;
using HuntersAndCollectors.Graves;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Storage;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    /// <summary>
    /// Server-authoritative save coordinator.
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private float autosaveIntervalSeconds = 60f;
        [SerializeField] private GraveNet gravePrefab;

        [Header("Grave Grounding")]
        [SerializeField] private LayerMask graveGroundMask;
        [Min(0.1f)] [SerializeField] private float graveGroundRayStartHeight = 2f;
        [Min(1f)] [SerializeField] private float graveGroundRayDistance = 16f;
        [Min(0f)] [SerializeField] private float graveGroundOffset = 0.05f;

        private PlayerSaveService playerSaveService;
        private ShardSaveService shardSaveService;
        private readonly HashSet<ulong> loadedClientIds = new();
        private float autosaveTimer;
        private string activeShardKey = string.Empty;
        private bool initialized;

        public static SaveManager Instance { get; private set; }
        public string ActiveShardKey => activeShardKey;

        public bool IsPlayerLoaded(ulong clientId) => loadedClientIds.Contains(clientId);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SaveManager] Duplicate instance detected. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // SaveManager must live on a root object if it is going to survive scene loads.
            // Unity ignores DontDestroyOnLoad on child objects and emits a warning, so we
            // enforce that rule explicitly instead of silently relying on fragile hierarchy setup.
            if (transform.parent != null)
            {
                Debug.LogError($"[SaveManager] '{name}' is not on a root GameObject. DontDestroyOnLoad was skipped. Move SaveManager onto a root services object such as BootstrapServices.", this);
            }
            else
            {
                DontDestroyOnLoad(gameObject);
            }

            if (itemDatabase == null)
            {
                ItemDatabase[] databases = Resources.FindObjectsOfTypeAll<ItemDatabase>();
                if (databases != null && databases.Length > 0)
                    itemDatabase = databases[0];
            }

            playerSaveService = new PlayerSaveService(itemDatabase);
            shardSaveService = new ShardSaveService(itemDatabase, gravePrefab);
            SavePaths.EnsureDirectories();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            UnsubscribeFromNetworkCallbacks();
        }

        private void OnApplicationQuit()
        {
            // Server-authoritative shutdown save hook.
            if (IsServerReady() && initialized)
                SaveAllNow();
        }
        private void Update()
        {
            if (!IsServerReady())
                return;

            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveTimer < Mathf.Max(1f, autosaveIntervalSeconds))
                return;

            autosaveTimer = 0f;
            SaveAllNow();
        }

        public void InitializeForShard(string shardKey)
        {
            if (!IsServerReady())
            {
                Debug.LogWarning("[SaveManager] InitializeForShard ignored because server is not running.");
                return;
            }

            if (string.IsNullOrWhiteSpace(shardKey))
                shardKey = "Shard_Default";

            activeShardKey = shardKey.Trim();
            autosaveTimer = 0f;
            initialized = true;
            loadedClientIds.Clear();

            SubscribeToNetworkCallbacks();
            LoadShardNow();

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                StartCoroutine(LoadPlayerWhenReady(client.ClientId));

            Debug.Log($"[SaveManager] Initialized for shard '{activeShardKey}'.");
        }

        public void LoadShardNow()
        {
            if (!IsServerReady() || !initialized)
                return;

            shardSaveService.LoadOrCreateAndApply(activeShardKey);
            Debug.Log($"[SaveManager] Shard loaded: {activeShardKey}");
        }

        public void SaveShardNow()
        {
            if (!IsServerReady() || !initialized)
                return;

            shardSaveService.Save(activeShardKey);
        }

        public void LoadPlayerNow(PlayerNetworkRoot playerRoot)
        {
            if (!IsServerReady() || !initialized || playerRoot == null)
                return;

            if (!playerRoot.IsSpawned)
                return;

            playerSaveService.LoadOrCreateAndApply(playerRoot);
            loadedClientIds.Add(playerRoot.OwnerClientId);
            Debug.Log($"[SaveManager] Player loaded: {playerRoot.PlayerKey}");
        }

        public void SavePlayerNow(PlayerNetworkRoot playerRoot)
        {
            if (!IsServerReady() || !initialized || playerRoot == null)
                return;

            playerSaveService.Save(playerRoot);
        }

        public void SaveAllNow()
        {
            if (!IsServerReady() || !initialized)
                return;

            SaveShardNow();

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null)
                    continue;

                PlayerNetworkRoot root = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
                if (root == null)
                    continue;

                SavePlayerNow(root);
            }

            Debug.Log("[SaveManager] SaveAllNow completed.");
        }

        public static void NotifyPlayerProgressChanged(PlayerNetworkRoot playerRoot)
        {
            if (Instance == null || playerRoot == null)
                return;

            if (!Instance.IsServerReady() || !Instance.initialized)
                return;

            Instance.SavePlayerNow(playerRoot);
        }

        public static void NotifyShardStateChanged()
        {
            if (Instance == null)
                return;

            if (!Instance.IsServerReady() || !Instance.initialized)
                return;

            Instance.SaveShardNow();
        }

        public static void NotifyPlacedStorageSpawned(StorageNet storage)
        {
            if (Instance == null || storage == null)
                return;

            if (!Instance.IsServerReady() || !Instance.initialized || Instance.shardSaveService == null)
                return;

            // Loaded placed chests can register a frame earlier than the initial restore sweep.
            // Re-trying on registration keeps chest persistence deterministic instead of timing-sensitive.
            Instance.shardSaveService.TryApplyPendingPlacedStorageRestore(storage);
        }

        public static bool IsPlayerLoadedStatic(ulong clientId)
        {
            return Instance != null && Instance.IsPlayerLoaded(clientId);
        }

        public static bool TryCreateGraveForPlayerDeath(PlayerNetworkRoot playerRoot, Vector3 position)
        {
            return Instance != null && Instance.ServerCreateGraveForPlayerDeath(playerRoot, position);
        }

        private bool ServerCreateGraveForPlayerDeath(PlayerNetworkRoot playerRoot, Vector3 position)
        {
            if (!IsServerReady() || !initialized || playerRoot == null || playerRoot.Inventory == null)
                return false;

            if (gravePrefab == null)
            {
                Debug.LogWarning("[Grave] Cannot spawn grave because SaveManager.gravePrefab is not assigned.");
                return false;
            }

            Vector3 groundedGravePosition = ResolveGroundedGravePosition(position);
            GraveNet spawnedGrave = Instantiate(gravePrefab, groundedGravePosition, Quaternion.identity);
            if (spawnedGrave == null)
                return false;

            string targetSceneName = playerRoot.gameObject.scene.IsValid() ? playerRoot.gameObject.scene.name : playerRoot.CurrentWorldSceneName;
            Debug.Log($"[SaveManager] Instantiated grave '{spawnedGrave.name}' in scene '{spawnedGrave.gameObject.scene.name}'. Target scene='{targetSceneName}'.", spawnedGrave);
            if (!Bootstrapper.MoveRuntimeGameplayObjectToScene(spawnedGrave.gameObject, targetSceneName, "SaveManager.CreateGrave"))
            {
                Destroy(spawnedGrave.gameObject);
                return false;
            }

            Vector3 finalGraveSpawnPosition = AdjustSpawnedGraveToGround(spawnedGrave, groundedGravePosition);
            Debug.Log($"[Grave] Final grave spawn position=({finalGraveSpawnPosition.x:F3},{finalGraveSpawnPosition.y:F3},{finalGraveSpawnPosition.z:F3})");

            spawnedGrave.ServerInitializeNew(playerRoot.PlayerKey);
            NetworkObject graveNetworkObject = spawnedGrave.GetComponent<NetworkObject>();
            if (graveNetworkObject == null)
            {
                Destroy(spawnedGrave.gameObject);
                return false;
            }

            graveNetworkObject.Spawn(destroyWithScene: true);
            Debug.Log($"[Death] Grave created id={spawnedGrave.PersistentId} at pos=({position.x:F3},{position.y:F3},{position.z:F3})");

            PlayerEquipmentSaveData equipmentSnapshot = playerRoot.Equipment != null
                ? playerRoot.Equipment.ServerExportSaveData() ?? new PlayerEquipmentSaveData()
                : new PlayerEquipmentSaveData();

            int transferredInventoryCount = TransferInventoryToGrave(playerRoot.Inventory, spawnedGrave);
            Debug.Log($"[Grave] Transferred inventory items count={transferredInventoryCount}");

            int transferredEquipmentCount = TransferMovedEquipmentToGrave(equipmentSnapshot, spawnedGrave);
            Debug.Log($"[Grave] Transferred equipment items count={transferredEquipmentCount}");

            playerRoot.Equipment?.ServerApplySaveData(new PlayerEquipmentSaveData());
            playerRoot.Inventory.ForceSendSnapshotToOwner();

            NetworkObject survivingPlayerObject = playerRoot.NetworkObject;
            bool playerObjectStillAlive = survivingPlayerObject != null && survivingPlayerObject.IsSpawned && playerRoot.gameObject != null;
            Debug.Log($"[Death] Player object still alive after death handling={playerObjectStillAlive}");

            if (!Bootstrapper.TryRespawnPlayerAtDefaultSpawn(playerRoot))
                Debug.LogWarning($"[Respawn] Failed to respawn player key={playerRoot.PlayerKey} after death.");

            NotifyPlayerProgressChanged(playerRoot);
            NotifyShardStateChanged();
            return true;
        }

        private Vector3 ResolveGroundedGravePosition(Vector3 deathPosition)
        {
            Debug.Log($"[Grave] Raw death position=({deathPosition.x:F3},{deathPosition.y:F3},{deathPosition.z:F3})");
            EnsureGraveGroundMaskInitialized();

            Vector3 rayOrigin = deathPosition + Vector3.up * Mathf.Max(0.1f, graveGroundRayStartHeight);
            if (!Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, Mathf.Max(1f, graveGroundRayDistance), graveGroundMask, QueryTriggerInteraction.Ignore))
            {
                Debug.LogWarning($"[Grave] Warning: no ground hit found, using raw death position");
                return deathPosition;
            }

            Debug.Log($"[Grave] Ground hit point=({hit.point.x:F3},{hit.point.y:F3},{hit.point.z:F3})");
            return hit.point + Vector3.up * Mathf.Max(0f, graveGroundOffset);
        }

        private Vector3 AdjustSpawnedGraveToGround(GraveNet grave, Vector3 desiredGroundedPosition)
        {
            if (grave == null)
                return desiredGroundedPosition;

            grave.transform.position = desiredGroundedPosition;

            Collider[] colliders = grave.GetComponentsInChildren<Collider>(true);
            float lowestPoint = float.PositiveInfinity;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null || !col.enabled)
                    continue;

                lowestPoint = Mathf.Min(lowestPoint, col.bounds.min.y);
            }

            if (!float.IsFinite(lowestPoint))
                return grave.transform.position;

            float pivotLift = grave.transform.position.y - lowestPoint;
            if (pivotLift <= 0.001f)
                return grave.transform.position;

            Vector3 adjusted = grave.transform.position;
            adjusted.y += pivotLift;
            grave.transform.position = adjusted;
            return adjusted;
        }

        private void EnsureGraveGroundMaskInitialized()
        {
            if (graveGroundMask.value != 0)
                return;

            int groundLayer = LayerMask.NameToLayer("Ground");
            graveGroundMask = groundLayer >= 0 ? (1 << groundLayer) : Physics.DefaultRaycastLayers;
        }
        private static int TransferInventoryToGrave(PlayerInventoryNet inventory, GraveNet grave)
        {
            if (inventory == null || inventory.Grid == null || grave == null)
                return 0;

            int movedCount = 0;
            InventorySlot[] slots = inventory.Grid.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty)
                    continue;

                if (!grave.ServerTryAddSlotPayload(slot, out int movedQuantity))
                    continue;

                movedCount++;
                if (slot.ContentType == InventorySlotContentType.Instance || movedQuantity >= slot.Stack.Quantity)
                {
                    slots[i] = MakeEmptySlot();
                    continue;
                }

                slot.Stack.Quantity -= movedQuantity;
                slots[i] = slot;
            }

            return movedCount;
        }

        private static int TransferMovedEquipmentToGrave(PlayerEquipmentSaveData equipmentSnapshot, GraveNet grave)
        {
            if (equipmentSnapshot == null || grave == null)
                return 0;

            int movedCount = 0;
            movedCount += TransferMovedEquipmentSlot(equipmentSnapshot.helmet, grave);
            movedCount += TransferMovedEquipmentSlot(equipmentSnapshot.chest, grave);
            movedCount += TransferMovedEquipmentSlot(equipmentSnapshot.legs, grave);
            movedCount += TransferMovedEquipmentSlot(equipmentSnapshot.boots, grave);
            movedCount += TransferMovedEquipmentSlot(equipmentSnapshot.gloves, grave);
            movedCount += TransferMovedEquipmentSlot(equipmentSnapshot.shoulders, grave);
            movedCount += TransferMovedEquipmentSlot(equipmentSnapshot.belt, grave);
            return movedCount;
        }

        private static int TransferMovedEquipmentSlot(EquipmentSlotSaveData equipmentSlot, GraveNet grave)
        {
            if (equipmentSlot == null || string.IsNullOrWhiteSpace(equipmentSlot.itemId) || grave == null)
                return 0;

            InventorySlot slot = new InventorySlot
            {
                IsEmpty = false,
                ContentType = InventorySlotContentType.Instance,
                Stack = new ItemStack { ItemId = equipmentSlot.itemId, Quantity = 1 },
                Instance = new ItemInstance
                {
                    InstanceId = 0,
                    ItemId = equipmentSlot.itemId,
                    RolledDamage = 0f,
                    RolledDefence = 0f,
                    RolledSwingSpeed = 0f,
                    RolledMovementSpeed = 0f,
                    MaxDurability = Mathf.Max(0, equipmentSlot.maxDurability),
                    CurrentDurability = Mathf.Max(0, equipmentSlot.durability)
                },
                Durability = Mathf.Max(0, equipmentSlot.durability),
                InstanceData = new ItemInstanceData
                {
                    BonusStrength = equipmentSlot.bonusStrength,
                    BonusDexterity = equipmentSlot.bonusDexterity,
                    BonusIntelligence = equipmentSlot.bonusIntelligence,
                    CraftedBy = new Unity.Collections.FixedString64Bytes(equipmentSlot.craftedBy ?? string.Empty),
                    MaxDurability = Mathf.Max(0, equipmentSlot.maxDurability),
                    CurrentDurability = Mathf.Max(0, equipmentSlot.durability)
                }
            };

            return grave.ServerTryAddSlotPayload(slot, out int movedQuantity) && movedQuantity > 0 ? 1 : 0;
        }

        private static InventorySlot MakeEmptySlot()
        {
            return new InventorySlot
            {
                IsEmpty = true,
                ContentType = InventorySlotContentType.Empty,
                Stack = default,
                Instance = default,
                Durability = 0,
                InstanceData = default
            };
        }
        private IEnumerator LoadPlayerWhenReady(ulong clientId)
        {
            const int maxFrames = 300;

            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (!IsServerReady())
                    yield break;

                if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null)
                {
                    yield return null;
                    continue;
                }

                PlayerNetworkRoot root = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
                if (root == null || root.Wallet == null || root.Skills == null || root.KnownItems == null || root.Inventory == null)
                {
                    yield return null;
                    continue;
                }

                LoadPlayerNow(root);
                yield break;
            }

            Debug.LogWarning($"[SaveManager] Timed out waiting for player object readiness for client {clientId}.");
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServerReady() || !initialized)
                return;

            if (loadedClientIds.Contains(clientId))
                return;

            StartCoroutine(LoadPlayerWhenReady(clientId));
        }

        private void HandleClientDisconnect(ulong clientId)
        {
            if (!initialized)
                return;

            // PlayerObject may already be gone at this point. If still available, save it.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
            {
                PlayerNetworkRoot root = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
                if (root != null)
                    SavePlayerNow(root);
            }

            loadedClientIds.Remove(clientId);
        }

        private bool IsServerReady()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && NetworkManager.Singleton.IsListening;
        }

        private void SubscribeToNetworkCallbacks()
        {
            if (NetworkManager.Singleton == null)
                return;

            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;

            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }

        private void UnsubscribeFromNetworkCallbacks()
        {
            if (NetworkManager.Singleton == null)
                return;

            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
    }
}














