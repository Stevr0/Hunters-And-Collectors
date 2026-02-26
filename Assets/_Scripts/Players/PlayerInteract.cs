using HuntersAndCollectors.Items;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Vendors;
using HuntersAndCollectors.Vendors.UI;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    public sealed class PlayerInteract : NetworkBehaviour
    {
        private PlayerInputActions input;

        [Header("UI")]
        [SerializeField] private VendorWindowUI vendorUI;

        [Header("Camera")]
        [Tooltip("Camera used for reticle raycasting. If null, will use Camera.main.")]
        [SerializeField] private Camera playerCamera;

        [Header("Interact Settings")]
        [Tooltip("Max distance the player can interact.")]
        [SerializeField] private float interactRange = 2.5f;

        [Tooltip("Only objects on these layers can be interacted with.")]
        [SerializeField] private LayerMask interactableMask;

        // Reference to your real inventory network component (the one you pasted)
        private PlayerInventoryNet inventoryNet;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
            {
                enabled = false;
                return;
            }

            // Find UI even if it's inactive
            vendorUI = FindObjectOfType<VendorWindowUI>(true);

            // Default layer if mask not set
            if (interactableMask.value == 0)
            {
                var layer = LayerMask.NameToLayer("Interactable");
                if (layer >= 0)
                    interactableMask = 1 << layer;
            }

            // Camera for reticle ray
            if (playerCamera == null)
                playerCamera = Camera.main;

            if (playerCamera == null)
                Debug.LogWarning("[PlayerInteract] No camera assigned and Camera.main not found.");

            // Grab inventory component on player
            inventoryNet = GetComponent<PlayerInventoryNet>();
            if (inventoryNet == null)
                Debug.LogWarning("[PlayerInteract] PlayerInventoryNet not found on player prefab.");

            input = new PlayerInputActions();
            input.Player.Interact.performed += _ => TryInteract();
            input.Enable();
        }

        private void OnDisable()
        {
            input?.Disable();
        }

        private void TryInteract()
        {
            if (playerCamera == null)
                return;

            // Ray straight out of the camera = centre of screen reticle
            var ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

            // Only hit interactable layer objects, within range
            if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableMask, QueryTriggerInteraction.Collide))
                return;

            Debug.Log($"[PlayerInteract] Ray hit: {hit.collider.name} (root: {hit.collider.transform.root.name})");

            // ------------------------------------------------------------
            // 1) Vendor interaction (keep your existing behaviour)
            // ------------------------------------------------------------
            var vendor = hit.collider.GetComponentInParent<VendorInteractable>();
            if (vendor != null)
            {
                if (vendorUI == null)
                {
                    Debug.LogWarning("[PlayerInteract] No VendorWindowUI found in scene.");
                    return;
                }

                vendorUI.Open(vendor);
                return;
            }

            // ------------------------------------------------------------
            // 2) World pickup interaction (NEW)
            // ------------------------------------------------------------
            var pickup = hit.collider.GetComponentInParent<WorldPickup>();
            if (pickup != null)
            {
                // We require a NetworkObject to reference it on the server
                var pickupNetObj = pickup.GetComponent<NetworkObject>();
                if (pickupNetObj == null)
                {
                    Debug.LogWarning("[PlayerInteract] WorldPickup has no NetworkObject (required).");
                    return;
                }

                // Ask server to pick it up (server validates + adds to inventory)
                RequestPickupServerRpc(pickupNetObj.NetworkObjectId);
                return;
            }

            // ------------------------------------------------------------
            // 3) Resource node interaction (NEW)
            // ------------------------------------------------------------
            var node = hit.collider.GetComponentInParent<HuntersAndCollectors.Harvesting.ResourceNodeNet>();
            if (node != null)
            {
                var nodeNetObj = node.GetComponent<NetworkObject>();
                if (nodeNetObj == null)
                {
                    Debug.LogWarning("[PlayerInteract] ResourceNodeNet has no NetworkObject (required).");
                    return;
                }

                RequestHarvestNodeServerRpc(nodeNetObj.NetworkObjectId);
                return;
            }

            // Later: you can add other interactables here (chests, doors, harvest nodes, etc.)
        }

        /// <summary>
        /// SERVER: Attempt to pick up a WorldPickup by its NetworkObjectId.
        ///
        /// Security:
        /// - RequireOwnership=true ensures only the owning client can call this on their player object.
        /// - We also validate the pickup exists and is within range on the server.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void RequestPickupServerRpc(ulong pickupNetworkObjectId)
        {
            if (!IsServer)
                return;

            if (inventoryNet == null)
                inventoryNet = GetComponent<PlayerInventoryNet>();

            if (inventoryNet == null)
                return;

            // Find the pickup NetworkObject
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(pickupNetworkObjectId, out var pickupNetObj))
                return;

            var pickup = pickupNetObj.GetComponent<WorldPickup>();
            if (pickup == null)
                return;

            // Distance validation on server (prevents “pick up from across map”)
            float dist = Vector3.Distance(transform.position, pickupNetObj.transform.position);
            if (dist > interactRange + 0.25f) // small tolerance due to camera vs player pivot
                return;

            // Validate the pickup has a real item definition assigned
            if (pickup.ItemDefinition == null)
            {
                Debug.LogWarning($"[Pickup][SERVER] WorldPickup '{pickupNetObj.name}' has no ItemDefinition assigned.");
                return;
            }

            // Use the stable id from the ItemDefinition (no more typed strings)
            string itemId = pickup.ItemDefinition.ItemId;

            if (string.IsNullOrWhiteSpace(itemId))
            {
                Debug.LogWarning($"[Pickup][SERVER] ItemDefinition on '{pickupNetObj.name}' has an empty ItemId.");
                return;
            }

            int remainder = inventoryNet.ServerAddItem(itemId, pickup.Quantity);

            // If we couldn't add anything (inventory full), do nothing
            if (remainder >= pickup.Quantity)
                return;

            // If we added all items, consume (despawn + notify spawner)
            if (remainder == 0)
            {
                pickup.ServerConsumeAndDespawn();
            }
            else
            {
                // Partial add handling (not in MVP yet):
                // - easiest: do nothing and leave pickup as-is.
                // - later: reduce remaining quantity in world.
            }
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestHarvestNodeServerRpc(ulong nodeNetworkObjectId)
        {
            // ServerRpc executes on server, but this guard keeps intent crystal clear.
            if (!IsServer)
                return;

            // Ensure we have the authoritative inventory component.
            if (inventoryNet == null)
                inventoryNet = GetComponent<PlayerInventoryNet>();

            if (inventoryNet == null)
                return;

            // Resolve the node NetworkObject by id.
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(nodeNetworkObjectId, out var nodeNetObj))
                return;

            // Ensure the target is actually a ResourceNodeNet.
            var node = nodeNetObj.GetComponent<HuntersAndCollectors.Harvesting.ResourceNodeNet>();
            if (node == null)
                return;

            // Server distance validation (prevents harvesting across the map).
            float dist = Vector3.Distance(transform.position, nodeNetObj.transform.position);
            if (dist > interactRange + 0.25f) // tolerance for camera pivot vs player pivot
                return;

            // Check availability (server-authoritative via replicated server time).
            if (!node.IsHarvestableNow())
                return;

            // ------------------------------------------------------------
            // ItemDef-based drop validation (prevents string ID typos)
            // ------------------------------------------------------------
            if (node.DropItem == null)
            {
                Debug.LogWarning($"[Harvest][SERVER] Resource node '{node.name}' has no DropItem assigned.");
                return;
            }

            string itemId = node.DropItemId;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                Debug.LogWarning($"[Harvest][SERVER] Resource node '{node.name}' DropItem has an empty ItemId.");
                return;
            }

            // Try to add items to inventory (server-authoritative).
            int remainder = inventoryNet.ServerAddItem(itemId, node.DropQuantity);

            // If we couldn't add anything (inventory full), don't consume the node.
            if (remainder >= node.DropQuantity)
                return;

            // If we added all items, consume node and start cooldown.
            // (Partial adds can be handled later if you introduce stack caps.)
            if (remainder == 0)
                node.ServerConsumeStartCooldown();

            // else: partial add occurred — for MVP we leave the node unconsumed.
        }
    }
}