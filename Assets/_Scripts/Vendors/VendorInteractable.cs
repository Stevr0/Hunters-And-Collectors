using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// VendorInteractable
    /// --------------------------------------------------------------------
    /// Networked "world vendor" the player interacts with.
    ///
    /// Key responsibilities:
    /// 1) Provide a reliable reference to the correct *spawned* VendorChestNet.
    ///    - In additive scenes / prefabs it's easy to accidentally reference a non-spawned chest (NetworkObjectId = 0).
    ///    - We resolve by VendorId at runtime to ensure we bind to the authoritative spawned instance.
    ///
    /// 2) Accept client requests to:
    ///    - Open vendor (request latest stock snapshot)
    ///    - Checkout (server-authoritative transaction)
    ///
    /// Netcode rules:
    /// - This is a world object, clients do NOT own it.
    /// - Therefore ServerRpcs that clients call MUST set RequireOwnership = false.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VendorInteractable : NetworkBehaviour
    {
        [Header("Chest Binding")]
        [Tooltip("Optional editor reference. At runtime we will prefer the spawned chest instance.")]
        [SerializeField] private VendorChestNet vendorChest;

        [Tooltip("Stable vendor identity used to resolve the correct spawned chest at runtime.")]
        [SerializeField] private string vendorId = "VENDOR_001";

        /// <summary>
        /// Chest used by this vendor (resolved to a spawned instance whenever possible).
        /// </summary>
        public VendorChestNet Chest
        {
            get
            {
                ResolveChestOrWarn(); // ensures we never return netId=0 if the spawned one exists
                return vendorChest;
            }
        }

        // Stateless service: OK to new() (no Unity refs inside).
        private readonly VendorTransactionService transactionService = new();

        private void OnValidate()
        {
            // Editor-time hints only (does not run in builds the same way).
            if (string.IsNullOrWhiteSpace(vendorId))
                vendorId = "VENDOR_001";

            if (vendorChest == null)
                Debug.LogWarning($"[VendorInteractable] '{name}' has no VendorChestNet assigned (will try runtime resolve).", this);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Ensure we never keep a "netId=0" (unspawned) reference by mistake.
            ResolveChestOrWarn();

            Debug.Log(
                $"[VendorInteractable] OnNetworkSpawn vendor='{name}' netId={NetworkObjectId} IsServer={IsServer} IsClient={IsClient} " +
                $"Chest='{(vendorChest ? vendorChest.name : "null")}' chestNetId={(vendorChest ? vendorChest.NetworkObjectId : 0)} vendorId={vendorId}",
                this);
        }

        /// <summary>
        /// Client asks server to broadcast the latest chest snapshot.
        /// World object: allow non-owner calls.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestOpenVendorServerRpc(ServerRpcParams serverRpcParams = default)
        {
            if (!IsServer)
                return;

            ResolveChestOrWarn();
            if (vendorChest == null)
                return;

            vendorChest.ForceBroadcastSnapshot();
        }

        /// <summary>
        /// Client asks server to checkout a cart.
        /// World object: allow non-owner calls.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestCheckoutServerRpc(CheckoutRequest request, ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            ResolveChestOrWarn();
            if (vendorChest == null)
                return;

            ulong buyerClientId = rpcParams.Receive.SenderClientId;

            // Lightweight diagnostics: helps confirm RPC routing and request contents.
            var lines = request.Lines;
            Debug.Log(
                $"[VendorInteractable] RequestCheckoutServerRpc RECEIVED senderClientId={buyerClientId} lines={lines?.Length ?? 0} " +
                $"vendorId={vendorId} chestNetId={vendorChest.NetworkObjectId}",
                this);

            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    Debug.Log(
                        $"[VendorInteractable] CheckoutLine[{i}] slotIndex={lines[i].SlotIndex} qty={lines[i].Quantity}",
                        this);
                }
            }

            // Resolve buyer player object on the server.
            if (!NetworkManager.ConnectedClients.TryGetValue(buyerClientId, out var buyerClient) || buyerClient.PlayerObject == null)
                return;

            var buyer = buyerClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            if (buyer == null)
                return;

            // Seller is optional for MVP (could be offline).
            PlayerNetworkRoot seller = null;

            ulong sellerClientId = vendorChest.OwnerClientId;
            if (sellerClientId != NetworkManager.ServerClientId &&
                NetworkManager.ConnectedClients.TryGetValue(sellerClientId, out var sellerClient) &&
                sellerClient.PlayerObject != null)
            {
                seller = sellerClient.PlayerObject.GetComponent<PlayerNetworkRoot>();
            }

            var context = new VendorTransactionService.VendorContext
            {
                Chest = vendorChest,
                Seller = seller
            };

            // Server-authoritative checkout (validates slot/qty/stock/prices/coins etc.).
            var result = transactionService.TryCheckout(buyer, context, request);

            // Respond only to the buyer.
            var toBuyer = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { buyerClientId } }
            };

            TransactionResultClientRpc(result, toBuyer);

            // Refresh chest state for everyone.
            vendorChest.ForceBroadcastSnapshot();
        }

        [ClientRpc]
        private void TransactionResultClientRpc(TransactionResult result, ClientRpcParams rpc = default)
        {
            // UI hook lives elsewhere (intentionally empty for MVP).
        }

        /// <summary>
        /// Ensures vendorChest points to the correct *spawned* chest instance.
        /// This prevents binding to a duplicate unspawned scene object (NetworkObjectId = 0).
        /// </summary>
        private void ResolveChestOrWarn()
        {
            // If we already have a spawned reference, we are good.
            if (vendorChest != null && vendorChest.IsSpawned)
                return;

            // Try resolve in hierarchy first (common setup: chest is a child).
            var inChildren = GetComponentsInChildren<VendorChestNet>(true);
            for (int i = 0; i < inChildren.Length; i++)
            {
                if (inChildren[i] != null && inChildren[i].IsSpawned)
                {
                    vendorChest = inChildren[i];
                    return;
                }
            }

            // Global resolve: find a spawned VendorChestNet with the matching VendorId.
            if (NetworkManager.Singleton != null)
            {
                foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
                {
                    var no = kvp.Value;
                    if (no == null) continue;

                    var chest = no.GetComponent<VendorChestNet>();
                    if (chest == null) continue;

                    // Must be spawned and match vendor id.
                    if (chest.IsSpawned && chest.VendorId == vendorId)
                    {
                        vendorChest = chest;
                        return;
                    }
                }
            }

            // If we get here, we could not resolve a valid chest.
            // This typically means:
            // - vendorId mismatch
            // - chest not spawned (scene setup / NGO scene management issue)
            // - duplicate chest objects exist, and the spawned one has different VendorId
            if (vendorChest == null)
            {
                Debug.LogWarning($"[VendorInteractable] No VendorChestNet found for vendorId={vendorId}.", this);
            }
            else
            {
                Debug.LogWarning(
                    $"[VendorInteractable] VendorChestNet reference is not spawned. name={vendorChest.name} netId={vendorChest.NetworkObjectId} vendorId={vendorId}",
                    vendorChest);
            }
        }
    }
}