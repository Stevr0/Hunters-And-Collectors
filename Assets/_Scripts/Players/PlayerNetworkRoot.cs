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

        public string PlayerKey { get; private set; } = string.Empty;

        public WalletNet Wallet => wallet;
        public SkillsNet Skills => skills;
        public KnownItemsNet KnownItems => knownItems;
        public PlayerInventoryNet Inventory => inventory;
        public PlayerEquipmentNet Equipment => equipment;

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
        }

        public override void OnNetworkSpawn()
        {
            if (!PlayerKeyOverrides.TryGetValue(OwnerClientId, out string overrideKey) || string.IsNullOrWhiteSpace(overrideKey))
                PlayerKey = $"Client_{OwnerClientId}";
            else
                PlayerKey = overrideKey;

            if (IsOwner && IsClient)
                LocalOwnerSpawned?.Invoke(this);
        }
    }
}

