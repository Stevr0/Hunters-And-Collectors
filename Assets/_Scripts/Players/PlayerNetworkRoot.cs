using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Skills;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Root player network component that exposes player key and sub-component references.
    /// It also tracks the player's current authored gameplay scene for shared-scene transfers.
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

        public string PlayerKey { get; private set; } = string.Empty;
        public string CurrentWorldSceneName { get; private set; } = string.Empty;

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

            CurrentWorldSceneName = string.IsNullOrWhiteSpace(sceneName)
                ? string.Empty
                : sceneName.Trim();
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

            if (string.IsNullOrWhiteSpace(CurrentWorldSceneName) && gameObject.scene.IsValid())
                CurrentWorldSceneName = gameObject.scene.name;

            if (IsOwner && IsClient)
                LocalOwnerSpawned?.Invoke(this);
        }
    }
}
