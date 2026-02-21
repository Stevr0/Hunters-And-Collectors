using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Skills;
using System;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Root player network component that exposes player key and sub-component references.
    /// 
    /// MVP goals:
    /// - Provide stable access to server-authoritative subcomponents (Wallet/Skills/KnownItems/Inventory).
    /// - Avoid prefab wiring mistakes by auto-fetching missing refs.
    /// </summary>
    public sealed class PlayerNetworkRoot : NetworkBehaviour
    {
        public static event Action<PlayerNetworkRoot> LocalOwnerSpawned;

        [Header("Player Components (auto-filled if missing)")]
        [SerializeField] private WalletNet wallet;
        [SerializeField] private SkillsNet skills;
        [SerializeField] private KnownItemsNet knownItems;
        [SerializeField] private PlayerInventoryNet inventory;

        /// <summary>Persistence key derived from OwnerClientId (MVP).</summary>
        public string PlayerKey { get; private set; } = string.Empty;

        public WalletNet Wallet => wallet;
        public SkillsNet Skills => skills;
        public KnownItemsNet KnownItems => knownItems;
        public PlayerInventoryNet Inventory => inventory;

        private void Awake()
        {
            // Auto-wire in editor/runtime to prevent null refs if prefab fields weren't assigned.
            if (wallet == null) wallet = GetComponent<WalletNet>();
            if (skills == null) skills = GetComponent<SkillsNet>();
            if (knownItems == null) knownItems = GetComponent<KnownItemsNet>();
            if (inventory == null) inventory = GetComponent<PlayerInventoryNet>();
        }

        public override void OnNetworkSpawn()
        {
            // Safe to set on all peers (useful for logs/UI). Authority remains server-side for saves.
            PlayerKey = $"Client_{OwnerClientId}";

            if (IsOwner && IsClient)
                LocalOwnerSpawned?.Invoke(this);
        }
    }
}
