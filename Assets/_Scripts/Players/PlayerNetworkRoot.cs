using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Skills;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Root player network component that exposes player key and sub-component references.
    /// It also tracks the player's current authored gameplay scene for shared-scene transfers.
    ///
    /// Scene authority notes:
    /// - The server is the only authority that may change the active gameplay scene.
    /// - The active scene name is replicated so owner-side presentation systems can hide scenes the player is not in.
    /// - Save/load continues to use the same authored scene name so persistence stays compatible.
    /// </summary>
    public sealed class PlayerNetworkRoot : NetworkBehaviour
    {
        public static event Action<PlayerNetworkRoot> LocalOwnerSpawned;

        private static readonly Dictionary<ulong, string> PlayerKeyOverrides = new();

        [Header("Player Components (auto-filled if missing)")]
        [SerializeField] private WalletNet wallet;
        [SerializeField] private SkillsNet skills;
        [SerializeField] private KnownItemsNet knownItems;
        [SerializeField] private PlayerInventoryNet inventory;
        [SerializeField] private PlayerEquipmentNet equipment;
        [SerializeField] private PlayerProgressionNet progression;
        [SerializeField] private PlayerSceneIsolationRuntime sceneIsolationRuntime;

        private readonly NetworkVariable<FixedString64Bytes> currentWorldSceneName = new(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public string PlayerKey { get; private set; } = string.Empty;
        public string CurrentWorldSceneName => currentWorldSceneName.Value.ToString();

        private bool hasLoadedWorldPosition;
        private Vector3 loadedWorldPosition;
        private float loadedWorldYaw;
        private string loadedWorldSceneName = string.Empty;

        public WalletNet Wallet => wallet;
        public SkillsNet Skills => skills;
        public KnownItemsNet KnownItems => knownItems;
        public PlayerInventoryNet Inventory => inventory;
        public PlayerEquipmentNet Equipment => equipment;
        public PlayerProgressionNet Progression => progression;

        public event Action<string, string> ActiveWorldSceneChanged;

        public static void SetPlayerKeyOverride(ulong ownerClientId, string playerKey)
        {
            if (string.IsNullOrWhiteSpace(playerKey))
                return;

            PlayerKeyOverrides[ownerClientId] = playerKey.Trim();
        }

        public static void ClearPlayerKeyOverride(ulong ownerClientId)
        {
            PlayerKeyOverrides.Remove(ownerClientId);
        }

        private void Awake()
        {
            if (wallet == null) wallet = GetComponent<WalletNet>();
            if (skills == null) skills = GetComponent<SkillsNet>();
            if (knownItems == null) knownItems = GetComponent<KnownItemsNet>();
            if (inventory == null) inventory = GetComponent<PlayerInventoryNet>();
            if (equipment == null) equipment = GetComponent<PlayerEquipmentNet>();
            if (progression == null) progression = GetComponent<PlayerProgressionNet>();
            if (sceneIsolationRuntime == null) sceneIsolationRuntime = GetComponent<PlayerSceneIsolationRuntime>();

            // Runtime attach keeps existing prefabs/scenes working without requiring manual component rewiring.
            if (sceneIsolationRuntime == null)
                sceneIsolationRuntime = gameObject.AddComponent<PlayerSceneIsolationRuntime>();
        }

        public void ServerSetLoadedWorldPosition(Vector3 position, float yawDegrees, string sceneName = null)
        {
            if (!IsServer)
                return;

            hasLoadedWorldPosition = true;
            loadedWorldPosition = position;
            loadedWorldYaw = yawDegrees;
            loadedWorldSceneName = string.IsNullOrWhiteSpace(sceneName)
                ? CurrentWorldSceneName
                : sceneName.Trim();
        }

        public void ServerSetCurrentWorldScene(string sceneName)
        {
            if (!IsServer)
                return;

            string canonical = string.IsNullOrWhiteSpace(sceneName)
                ? string.Empty
                : sceneName.Trim();

            if (string.Equals(CurrentWorldSceneName, canonical, StringComparison.Ordinal))
                return;

            string previous = CurrentWorldSceneName;
            currentWorldSceneName.Value = new FixedString64Bytes(canonical);
            Debug.Log($"[PlayerNetworkRoot] Player '{PlayerKey}' active gameplay scene changed from '{previous}' to '{canonical}'.", this);
        }

        public void ServerClearLoadedWorldPosition()
        {
            if (!IsServer)
                return;

            hasLoadedWorldPosition = false;
            loadedWorldPosition = default;
            loadedWorldYaw = 0f;
            loadedWorldSceneName = string.Empty;
        }

        public bool ServerTryGetLoadedWorldPosition(out Vector3 position, out Quaternion rotation)
        {
            position = loadedWorldPosition;
            rotation = Quaternion.Euler(0f, loadedWorldYaw, 0f);
            return IsServer && hasLoadedWorldPosition;
        }

        public bool ServerTryGetLoadedWorldState(out string sceneName, out Vector3 position, out Quaternion rotation)
        {
            sceneName = loadedWorldSceneName;
            position = loadedWorldPosition;
            rotation = Quaternion.Euler(0f, loadedWorldYaw, 0f);
            return IsServer && hasLoadedWorldPosition;
        }

        public override void OnNetworkSpawn()
        {
            if (!PlayerKeyOverrides.TryGetValue(OwnerClientId, out string overrideKey) || string.IsNullOrWhiteSpace(overrideKey))
                PlayerKey = $"Client_{OwnerClientId}";
            else
                PlayerKey = overrideKey;

            currentWorldSceneName.OnValueChanged += HandleCurrentWorldSceneChanged;

            if (IsServer && string.IsNullOrWhiteSpace(CurrentWorldSceneName) && gameObject.scene.IsValid())
                currentWorldSceneName.Value = new FixedString64Bytes(gameObject.scene.name);

            sceneIsolationRuntime?.HandleNetworkSpawn();

            if (IsOwner && IsClient)
                LocalOwnerSpawned?.Invoke(this);
        }

        public override void OnNetworkDespawn()
        {
            currentWorldSceneName.OnValueChanged -= HandleCurrentWorldSceneChanged;
            sceneIsolationRuntime?.HandleNetworkDespawn();
        }

        private void HandleCurrentWorldSceneChanged(FixedString64Bytes previousValue, FixedString64Bytes currentValue)
        {
            ActiveWorldSceneChanged?.Invoke(previousValue.ToString(), currentValue.ToString());
        }
    }
}
