using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Skills;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Root player network component that exposes player key and sub-component references.
    /// </summary>
    public sealed class PlayerNetworkRoot : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab root with NetworkObject and all player net components.
        [SerializeField] private WalletNet wallet;
        [SerializeField] private SkillsNet skills;
        [SerializeField] private KnownItemsNet knownItems;
        [SerializeField] private PlayerInventoryNet inventory;

        /// <summary>Persistence key assigned from owner client id.</summary>
        public string PlayerKey { get; private set; } = string.Empty;
        public WalletNet Wallet => wallet;
        public SkillsNet Skills => skills;
        public KnownItemsNet KnownItems => knownItems;
        public PlayerInventoryNet Inventory => inventory;

        public override void OnNetworkSpawn()
        {
            if (IsServer) PlayerKey = $"Client_{OwnerClientId}";
        }
    }
}
