using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// HarvestingNet
    /// --------------------------------------------------------------------
    /// Server-authoritative harvesting + pickup coordinator (Hit-to-Harvest).
    ///
    /// GOAL (Valheim-style feel):
    /// - Left click swing -> server validates -> damage node HP
    /// - When HP reaches 0:
    ///     - Spawn world drops that SCATTER around the node (pop + random push)
    ///     - Drops are independent world objects (NEVER parented to node)
    ///     - Drops are moved into the SAME SCENE as the node (important for additive scenes / bootstrap)
    ///
    /// AUTHORITY RULES:
    /// - Clients NEVER mutate inventory, XP, node HP, or drops.
    /// - Clients only request actions via ServerRpc:
    ///     - HitNodeServerRpc(nodeId)
    ///     - RequestPickupDropServerRpc(dropNetworkObjectId)
    /// - Server validates and applies results.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class HarvestingNet : NetworkBehaviour
    {
        // --------------------------------------------------------------------
        // Dependencies
        // --------------------------------------------------------------------
        [Header("Dependencies")]
        [SerializeField] private PlayerInventoryNet inventory;
        [SerializeField] private SkillsNet skills;
        [SerializeField] private ResourceNodeRegistry nodeRegistry;
        [SerializeField] private PlayerEquipmentNet equipment;
        [SerializeField] private PlayerMovement playerMovement;
        [SerializeField] private PlayerHarvestAnimNet harvestAnim;
        [SerializeField] private PlayerVitalsNet playerVitals;

        [Header("Item Lookup (required for drop spawning)")]
        [Tooltip("Used to look up ItemDef so we can spawn the correct prefab.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Currency Pickup")]
        [Tooltip("Wallet on this player object. Coin drops credit this directly.")]
        [SerializeField] private WalletNet wallet;

        [Tooltip("Optional explicit coin item definition that should go to WalletNet on pickup.")]
        [SerializeField] private ItemDef walletCoinItemDef;

        [Tooltip("Fallback coin item id used when Wallet Coin ItemDef is not assigned.")]
        [SerializeField] private string walletCoinItemId = "IT_Coin";

        // --------------------------------------------------------------------
        // Validation / Tuning
        // --------------------------------------------------------------------
        [Header("Validation")]
        [Tooltip("Max world distance allowed when hitting/harvesting a node (meters).")]
        [Min(0.5f)]
        [SerializeField] private float nodeMaxDistance = 3.0f;

        [Tooltip("Minimum seconds between server-validated hits for this player (anti-spam).")]
        [Min(0.05f)]
        [SerializeField] private float hitCooldownSeconds = 0.35f;

        [Header("Swing Rate Limiting")]
        [Tooltip("How ItemDef.SwingSpeed should be interpreted when converting to a swing interval.")]
        [SerializeField] private SwingSpeedUnit swingSpeedUnit = SwingSpeedUnit.SwingsPerSecond;

        [Tooltip("When true, logs server/client swing rate decisions and cooldown rejects.")]
        [SerializeField] private bool debugSwingRate = false;

        [Tooltip("Damage applied to a node per successful hit (node health is server-side).")]
        [Min(1)]
        [SerializeField] private int hitDamagePerSwing = 1;

        [Tooltip("Max world distance allowed when picking up a resource drop (meters).")]
        [Min(0.5f)]
        [SerializeField] private float dropMaxDistance = 3.0f;

        // --------------------------------------------------------------------
        // Yield scaling
        // --------------------------------------------------------------------
        [Header("Yield Scaling")]
        [Tooltip("Hard cap applied after skill scaling to prevent runaway yields.")]
        [Min(1)]
        [SerializeField] private int nodeYieldMax = 50;

        [Tooltip("Hard cap applied to drop pickups after skill scaling.")]
        [Min(1)]
        [SerializeField] private int dropYieldMax = 30;

        // Yield scaling knobs:
        // - Node: + floor(level / 10)
        // - Drop: + floor(level / 20)
        private const int NodeYieldDivisor = 10;
        private const int DropYieldDivisor = 20;

        // --------------------------------------------------------------------
        // Drop spawning (Valheim-style scatter)
        // --------------------------------------------------------------------
        [Header("Drop Spawn - Scatter (Valheim Feel)")]
        [Tooltip("Random horizontal scatter radius around the node.")]
        [Min(0f)]
        [SerializeField] private float dropScatterRadius = 0.85f;

        [Tooltip("How high above the ground we place drops after ground hit.")]
        [Min(0f)]
        [SerializeField] private float dropSpawnHeight = 0.25f;

        [Tooltip("How high above the node we start the ground raycast.")]
        [Min(0f)]
        [SerializeField] private float groundRayHeight = 6f;

        [Tooltip("How far down we search for ground.")]
        [Min(0f)]
        [SerializeField] private float groundRayDistance = 30f;

        [Tooltip("Ground layers ONLY (Terrain/Ground). Do NOT include Interactable.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Extra upward nudge after spawn to reduce initial penetration.")]
        [Min(0f)]
        [SerializeField] private float extraUpOffsetIfIntersecting = 0.15f;

        [Tooltip("Upward impulse when the drop spawns (makes it pop out).")]
        [Min(0f)]
        [SerializeField] private float dropUpwardImpulse = 2.0f;

        [Tooltip("Random horizontal push strength (makes it scatter/bounce).")]
        [Min(0f)]
        [SerializeField] private float dropRandomForce = 1.2f;

        [Tooltip("Optional auto-despawn time (seconds) for harvested drops. 0 = never.")]
        [Min(0f)]
        [SerializeField] private float harvestedDropLifetimeSeconds = 120f;

        [Tooltip("If true, apply random rotation to drops on spawn.")]
        [SerializeField] private bool randomizeDropRotation = true;

        [Header("Drop Spawn - Valheim Pieces")]
        [Tooltip("If true, spawn many small drop objects (Valheim-style). If false, spawn one stack drop.")]
        [SerializeField] private bool spawnAsPieces = true;

        [Tooltip("Max number of physical pieces to spawn per drop (prevents spam).")]
        [Min(1)]
        [SerializeField] private int maxPiecesPerDrop = 10;

        [Tooltip("How many units each piece represents when spawning as pieces (e.g. 1 = one item per piece).")]
        [Min(1)]
        [SerializeField] private int unitsPerPiece = 1;

        [Tooltip("Random spin applied to drops (adds life).")]
        [Min(0f)]
        [SerializeField] private float dropRandomTorque = 2.0f;

        [Tooltip("Extra random upward variance on impulse.")]
        [Min(0f)]
        [SerializeField] private float dropUpwardVariance = 0.8f;

        [Tooltip("Extra random horizontal variance on impulse.")]
        [Min(0f)]
        [SerializeField] private float dropHorizontalVariance = 0.6f;

        // --------------------------------------------------------------------
        // XP
        // --------------------------------------------------------------------
        [Header("XP Rewards")]
        [Tooltip("Base XP granted for every successful harvest depletion or pickup.")]
        [Min(0)]
        [SerializeField] private int xpPerAction = 1;

        [Tooltip("Optional bonus XP granted whenever a node is depleted.")]
        [Min(0)]
        [SerializeField] private int xpBonusOnNodeDeplete = 0;

        [Tooltip("Bonus XP when at least one rare drop fires (set 0 to disable).")]
        [Min(0)]
        [SerializeField] private int xpBonusOnRareDrop = 1;

        // --------------------------------------------------------------------
        // Internal state (server)
        // --------------------------------------------------------------------
        private double _nextAllowedServerHitTime;

        // Reused buffers to avoid allocations during rare-drop processing.
        private readonly List<string> _rareDropIdsBuffer = new(4);
        private readonly List<int> _rareDropAmountsBuffer = new(4);

        private enum SwingSpeedUnit
        {
            SwingsPerSecond = 0,
            SecondsPerSwing = 1,
            AutoDetect = 2
        }


        // --------------------------------------------------------------------
        // Client entry points
        // --------------------------------------------------------------------
        #region Client Entry Points

        /// <summary>
        /// Client helper invoked by interaction logic for hit-to-harvest swings.
        /// This does NOT apply damage locally; it only requests the server to process a hit.
        /// </summary>
        // HarvestingNet.cs
        // Replace RequestHitNode + HitNodeServerRpc with this version.

        public void RequestHitNode(ResourceNodeNet node)
        {
            if (!IsOwner || node == null)
                return;

            if (!node.IsSpawned)
                return;

            // Send the NetworkObjectId instead of a string id.
            HitNodeServerRpc(node.NetworkObjectId);
        }

        /// <summary>
        /// Owner-side helper used by input code to locally throttle hold-to-swing requests.
        /// Server still validates and enforces final timing.
        /// </summary>
        public float GetOwnerExpectedSwingIntervalSeconds(ResourceNodeNet node)
        {
            if (!EnsureDependencies())
                return Mathf.Max(0.01f, hitCooldownSeconds);

            float interval = ResolveSwingIntervalSeconds(node, out _, out _, out _, out _);
            return Mathf.Max(0.01f, interval);
        }

        [ServerRpc(RequireOwnership = true)]
        private void HitNodeServerRpc(ulong nodeNetworkObjectId)
        {
            if (!IsServer)
                return;

            if (!EnsureDependencies())
            {
                SendHarvestResult(false, HarvestFailureReason.ConfigError, string.Empty, string.Empty, 0);
                return;
            }

            if (NetworkManager.SpawnManager == null ||
                !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(nodeNetworkObjectId, out var nodeNetObj) ||
                nodeNetObj == null ||
                !nodeNetObj.TryGetComponent<ResourceNodeNet>(out var node) ||
                node == null)
            {
                SendHarvestResult(false, HarvestFailureReason.NodeNotFound, nodeNetworkObjectId.ToString(), string.Empty, 0);
                return;
            }

            TryProcessServerHitRequest(node, node.NodeId);
        }

        /// <summary>
        /// Client helper for picking up spawned drops.
        /// Pickup is validated + applied ONLY on the server.
        /// </summary>
        public void RequestPickup(ResourceDrop drop)
        {
            if (!IsOwner || drop == null)
                return;

            if (!drop.IsSpawned)
                return;

            RequestPickupDropServerRpc(drop.NetworkObjectId);
        }

        #endregion

        // --------------------------------------------------------------------
        // Server RPCs - Harvesting (node hits)
        // --------------------------------------------------------------------
        #region Server RPCs - Harvesting
        [ServerRpc(RequireOwnership = true)]
        private void HitNodeServerRpc(FixedString64Bytes nodeId)
        {
            if (!IsServer)
                return;

            string nodeIdString = nodeId.ToString();

            if (!EnsureDependencies())
            {
                LogHarvestEvent("HitNode.Fail.Config", null, HarvestFailureReason.ConfigError, -1f, "Dependencies missing");
                SendHarvestResult(false, HarvestFailureReason.ConfigError, nodeIdString, string.Empty, 0);
                return;
            }

            if (nodeId.Length == 0)
            {
                LogHarvestEvent("HitNode.Fail.InvalidId", null, HarvestFailureReason.NodeNotFound, -1f, "nodeId empty");
                SendHarvestResult(false, HarvestFailureReason.NodeNotFound, nodeIdString, string.Empty, 0);
                return;
            }

            if (!nodeRegistry.TryGet(nodeIdString, out var node) || node == null)
            {
                LogHarvestEvent("HitNode.Fail.NodeLookup", null, HarvestFailureReason.NodeNotFound, -1f, $"nodeId={nodeIdString}");
                SendHarvestResult(false, HarvestFailureReason.NodeNotFound, nodeIdString, string.Empty, 0);
                return;
            }

            TryProcessServerHitRequest(node, nodeIdString);
        }

        #endregion

        // --------------------------------------------------------------------
        // Server RPCs - Drop Pickup
        // --------------------------------------------------------------------
        #region Server RPCs - Drop Pickup

        [ServerRpc(RequireOwnership = true)]
        private void RequestPickupDropServerRpc(ulong dropNetworkObjectId)
        {
            if (!IsServer)
                return;

            if (!EnsureDependencies())
            {
                SendDropResult(false, HarvestFailureReason.ConfigError, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            // Find the spawned network object by id.
            if (NetworkManager.SpawnManager == null ||
                !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(dropNetworkObjectId, out var dropNetObj))
            {
                SendDropResult(false, HarvestFailureReason.DropMissing, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            if (!dropNetObj.TryGetComponent<ResourceDrop>(out var drop) || drop == null)
            {
                drop = dropNetObj.GetComponentInChildren<ResourceDrop>(true);
            }

            if (drop == null)
            {
                SendDropResult(false, HarvestFailureReason.DropMissing, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            // Prevent double pickup.
            if (drop.IsConsumed)
            {
                SendDropResult(false, HarvestFailureReason.AlreadyConsumed, dropNetworkObjectId, drop.ItemId, 0);
                return;
            }

            // Distance check.
            if (!IsWithinRange(dropNetObj.transform.position, dropMaxDistance))
            {
                SendDropResult(false, HarvestFailureReason.OutOfRange, dropNetworkObjectId, drop.ItemId, 0);
                return;
            }

            string itemId = drop.ItemId;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                SendDropResult(false, HarvestFailureReason.ConfigError, dropNetworkObjectId, string.Empty, 0);
                return;
            }

            bool isWalletCoin = IsWalletCoinDrop(drop, itemId);
            if (isWalletCoin)
            {
                if (wallet == null)
                    wallet = GetComponent<WalletNet>();

                if (wallet == null)
                {
                    SendDropResult(false, HarvestFailureReason.ConfigError, dropNetworkObjectId, itemId, 0);
                    return;
                }

                int grantedCoins = Mathf.Max(1, drop.Quantity);
                wallet.AddCoins(grantedCoins);

                // Despawn drop after success.
                drop.ServerConsumeAndDespawn(0.75f);

                // Cosmetic gather anim.
                if (playerMovement != null)
                    playerMovement.PlayGatherClientRpc();

                playerVitals?.ServerMarkBusyFromAction();

                Debug.Log($"[HarvestingNet][SERVER] Wallet pickup owner={OwnerClientId} item={itemId} coins={grantedCoins}");
                SendDropResult(true, HarvestFailureReason.None, dropNetworkObjectId, itemId, grantedCoins);
                return;
            }

            // Optional skill influence for pickup scaling.
            // Some items (e.g., berries/rare drops) may not map to a harvesting skill yet.
            bool hasMappedSkill = TryGetSkillForItem(itemId, out var skillId);
            int level = hasMappedSkill ? GetSkillLevel(skillId) : 0;

            // Scale pickup yield by skill.
            int desiredQuantity = CalculateDropYield(drop.Quantity, level);

            // Validate capacity before consuming world drop.
            if (!CanInventoryAccept(itemId, desiredQuantity))
            {
                SendDropResult(false, HarvestFailureReason.InventoryFull, dropNetworkObjectId, itemId, 0);
                return;
            }

            // Add server-side.
            int remainder = inventory.ServerAddItem(itemId, desiredQuantity);
            int granted = desiredQuantity - remainder;

            if (granted <= 0)
            {
                SendDropResult(false, HarvestFailureReason.InventoryFull, dropNetworkObjectId, itemId, 0);
                return;
            }

            // Despawn drop after success.
            drop.ServerConsumeAndDespawn(0.75f);


            // Any successful pickup counts as a busy action for regen suppression.
            playerVitals?.ServerMarkBusyFromAction();

            // Cosmetic gather anim.
            if (playerMovement != null)
                playerMovement.PlayGatherClientRpc();

            SendDropResult(true, HarvestFailureReason.None, dropNetworkObjectId, itemId, granted);        }

        #endregion

        // --------------------------------------------------------------------
        // Client feedback RPCs (targeted to owner only)
        // --------------------------------------------------------------------
        #region Client Feedback RPCs

        [ClientRpc]
        private void HarvestResultClientRpc(
            bool success,
            HarvestFailureReason reason,
            string nodeId,
            string itemId,
            int amountAwarded,
            RareDropPayload[] rareDropsPayload,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            var rareDrops = ConvertRareDropPayload(rareDropsPayload);

            Debug.Log($"[HarvestingNet][CLIENT] Harvest result: success={success} reason={reason} node={nodeId} item={itemId} amount={amountAwarded} rareCount={rareDrops.Count}");
            OnHarvestResult?.Invoke(new HarvestResultEvent(success, reason, nodeId, itemId, amountAwarded, rareDrops));
        }

        [ClientRpc]
        private void DropPickupResultClientRpc(
            bool success,
            HarvestFailureReason reason,
            ulong dropNetworkObjectId,
            string itemId,
            int amountAwarded,
            ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            Debug.Log($"[HarvestingNet][CLIENT] Drop pickup result: success={success} reason={reason} dropId={dropNetworkObjectId} item={itemId} amount={amountAwarded}");
        }

        private void SendHarvestResult(
            bool success,
            HarvestFailureReason reason,
            string nodeId,
            string itemId,
            int amount,
            List<string> rareIds = null,
            List<int> rareAmounts = null)
        {
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            var payload = BuildRareDropPayload(rareIds, rareAmounts);

            HarvestResultClientRpc(
                success,
                reason,
                nodeId ?? string.Empty,
                itemId ?? string.Empty,
                amount,
                payload,
                rpcParams);
        }

        private void SendDropResult(bool success, HarvestFailureReason reason, ulong dropId, string itemId, int amount)
        {
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            DropPickupResultClientRpc(success, reason, dropId, itemId ?? string.Empty, amount, rpcParams);
        }

        private RareDropPayload[] BuildRareDropPayload(List<string> rareIds, List<int> rareAmounts)
        {
            if (rareIds == null || rareAmounts == null || rareIds.Count == 0)
                return Array.Empty<RareDropPayload>();

            int count = Mathf.Min(rareIds.Count, rareAmounts.Count);
            if (count <= 0)
                return Array.Empty<RareDropPayload>();

            var payload = new RareDropPayload[count];

            for (int i = 0; i < count; i++)
            {
                string id = rareIds[i];
                int qty = rareAmounts[i];

                payload[i] = new RareDropPayload
                {
                    ItemId = string.IsNullOrWhiteSpace(id) ? string.Empty : id,
                    Quantity = Mathf.Max(0, qty)
                };
            }

            return payload;
        }

        #endregion

        // --------------------------------------------------------------------
        // Node depletion flow
        // --------------------------------------------------------------------
        #region Node Depletion Flow

        private void HandleNodeDepleted(ResourceNodeNet node, string equippedItemIdUsed)
        {
            if (node == null)
                return;

            string nodeId = node.NodeId ?? string.Empty;
            string itemId = node.DropItemId;

            if (string.IsNullOrWhiteSpace(itemId))
            {
                node.ServerRestoreFullHealth();
                SendHarvestResult(false, HarvestFailureReason.ConfigError, nodeId, string.Empty, 0);
                return;
            }

            string skillId = SkillIdForResource(node.ResourceType);
            int level = GetSkillLevel(skillId);
            int desiredYield = CalculateNodeYield(node.BaseYield, level);

            // Base drop (scatter + pop).
            if (!TrySpawnHarvestedDrop(node, itemId, desiredYield))
            {
                node.ServerRestoreFullHealth();
                SendHarvestResult(false, HarvestFailureReason.ConfigError, nodeId, itemId, 0);
                return;
            }

            // Rare drops (optional).
            _rareDropIdsBuffer.Clear();
            _rareDropAmountsBuffer.Clear();
            bool rareAwarded = ProcessRareDrops(node, level);

            // Cooldown.
            node.ServerConsumeStartCooldown();

            int xpAward = xpPerAction + xpBonusOnNodeDeplete;
            if (rareAwarded && xpBonusOnRareDrop > 0)
                xpAward += xpBonusOnRareDrop;

            GrantXp(skillId, xpAward);

            SendHarvestResult(true, HarvestFailureReason.None, nodeId, itemId, desiredYield, _rareDropIdsBuffer, _rareDropAmountsBuffer);

            // Durability is consumed only when harvest completion actually succeeded.
            // Failed/rejected swings never reach this successful completion path.
            ServerConsumeHarvestDurability(equippedItemIdUsed);
        }


        /// <summary>
        /// SERVER ONLY: consume one durability use for the equipped harvesting item.
        /// This runs only after a successful harvest completion (resource payout path).
        /// </summary>
        private void ServerConsumeHarvestDurability(string equippedItemIdUsed)
        {
            if (!IsServer)
                return;

            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            if (equipment == null)
                return;

            if (string.IsNullOrWhiteSpace(equippedItemIdUsed))
                return;

            equipment.ServerDamageDurabilityForEquippedItem(equippedItemIdUsed, 1, out _, out _);
        }
        private bool ProcessRareDrops(ResourceNodeNet node, int playerLevel)
        {
            if (node == null || !node.HasRareDrops)
                return false;

            bool awarded = false;

            foreach (var entry in node.RareDrops)
            {
                if (!entry.IsConfigured())
                    continue;

                float chance = entry.EvaluateChance01(playerLevel);
                if (chance <= 0f)
                    continue;

                if (UnityEngine.Random.value > chance)
                    continue;

                string rareItemId = entry.CanonicalItemId;
                if (string.IsNullOrWhiteSpace(rareItemId))
                    continue;

                // Rare drops should also scatter/pop.
                if (!TrySpawnHarvestedDrop(node, rareItemId, entry.Quantity))
                {
                    Debug.LogWarning($"[HarvestingNet][SERVER] Rare drop spawn failed. player={OwnerClientId} item={rareItemId}");
                    continue;
                }

                _rareDropIdsBuffer.Add(rareItemId);
                _rareDropAmountsBuffer.Add(entry.Quantity);
                awarded = true;
            }

            return awarded;
        }

        #endregion

        // --------------------------------------------------------------------
        // Drop spawning (server-only) with Valheim-style scatter
        // --------------------------------------------------------------------
        #region Drop Spawning

        /// <summary>
        /// SERVER: Spawn a harvested drop using the ItemDef prefab referenced by itemId.
        ///
        /// Key rules:
        /// - Drops are independent world objects (NOT children of the node)
        /// - Drops are moved into the SAME SCENE as the node (Bootstrap + additive scenes safe)
        /// - Drops scatter around the node and get a small physics "pop"
        ///
        /// Prefab requirements:
        /// - ItemDef.VisualPrefab assigned
        /// - VisualPrefab has NetworkObject + ResourceDrop
        /// - VisualPrefab has a Collider on Interactable layer (so PlayerInteract ray can hit)
        /// </summary>
        private bool TrySpawnHarvestedDrop(ResourceNodeNet node, string itemId, int quantityToSpawn)
        {
            if (!IsServer)
                return false;

            if (node == null)
                return false;

            if (string.IsNullOrWhiteSpace(itemId) || quantityToSpawn <= 0)
                return false;

            if (itemDatabase == null)
                return false;

            // 1) Lookup item definition (must have VisualPrefab with NetworkObject + ResourceDrop).
            if (!itemDatabase.TryGet(itemId, out var def) || def == null)
            {
                Debug.LogError($"[HarvestingNet][SERVER] ItemDef not found for itemId='{itemId}'");
                return false;
            }

            if (def.VisualPrefab == null)
            {
                Debug.LogError($"[HarvestingNet][SERVER] ItemDef VisualPrefab missing for itemId='{itemId}'");
                return false;
            }

            // Decide whether to spawn as many pieces or a single stack.
            if (!spawnAsPieces)
            {
                // Single stack drop (old behavior).
                return SpawnOneDropObject(node, def, itemId, quantityToSpawn);
            }

            // 2) Piece spawning (Valheim style).
            // Break total quantity into multiple pieces so they "pop" separately.
            int pieces = Mathf.CeilToInt(quantityToSpawn / (float)Mathf.Max(1, unitsPerPiece));
            pieces = Mathf.Clamp(pieces, 1, maxPiecesPerDrop);

            // Compute how many units each piece gets (last piece takes remainder).
            int remaining = quantityToSpawn;

            bool anySpawned = false;

            for (int i = 0; i < pieces; i++)
            {
                int qtyThisPiece;

                // If we're at the last piece, take all remaining.
                if (i == pieces - 1)
                    qtyThisPiece = remaining;
                else
                    qtyThisPiece = Mathf.Min(unitsPerPiece, remaining);

                remaining -= qtyThisPiece;

                if (qtyThisPiece <= 0)
                    continue;

                // Spawn one physical piece with its own scatter/impulse.
                bool ok = SpawnOneDropObject(node, def, itemId, qtyThisPiece);
                if (ok)
                    anySpawned = true;
            }

            return anySpawned;
        }

        /// <summary>
        /// Spawns a single ResourceDrop object with scatter + impulse, Valheim-style.
        /// This function guarantees:
        /// - Not parented to node
        /// - Moved into node's scene (additive-safe)
        /// - Collider enabled
        /// - NetworkObject spawned on server
        /// - Rigidbody gets a pop + push + spin
        /// </summary>
        private bool SpawnOneDropObject(ResourceNodeNet node, ItemDef def, string itemId, int quantity)
        {
            // 1) Choose scatter offset (XZ) around node.
            Vector2 scatter2D = UnityEngine.Random.insideUnitCircle * dropScatterRadius;
            Vector3 xzOffset = new Vector3(scatter2D.x, 0f, scatter2D.y);

            // 2) Raycast down to find ground near the node.
            Vector3 nodePos = node.transform.position;
            Vector3 rayOrigin = nodePos + xzOffset + Vector3.up * groundRayHeight;

            Vector3 spawnPos;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayDistance, groundMask, QueryTriggerInteraction.Ignore))
            {
                spawnPos = hit.point + Vector3.up * dropSpawnHeight;
            }
            else
            {
                // Fallback: still spawn visible.
                spawnPos = nodePos + xzOffset + Vector3.up * dropSpawnHeight;
            }

            // 3) Instantiate, ensure not parented, move into node scene.
            Quaternion rot = randomizeDropRotation ? UnityEngine.Random.rotation : Quaternion.identity;
            GameObject go = Instantiate(def.VisualPrefab, spawnPos, rot);

            go.transform.SetParent(null, true); // never parent to node
            SceneManager.MoveGameObjectToScene(go, node.gameObject.scene); // additive-safe

            // 4) Validate required components.
            if (!go.TryGetComponent<NetworkObject>(out var netObj) || netObj == null)
            {
                Debug.LogError($"[HarvestingNet][SERVER] Drop prefab missing NetworkObject. prefab='{def.VisualPrefab.name}' itemId='{itemId}'");
                Destroy(go);
                return false;
            }

            if (!go.TryGetComponent<ResourceDrop>(out var drop) || drop == null)
            {
                drop = go.GetComponentInChildren<ResourceDrop>(true);
            }

            if (drop == null)
            {
                Debug.LogError($"[HarvestingNet][SERVER] Drop prefab missing ResourceDrop. prefab='{def.VisualPrefab.name}' itemId='{itemId}'");
                Destroy(go);
                return false;
            }

            // 5) Ensure collider is enabled so it can be raycast/interacted with.
            // (Your issue earlier looked like collider getting disabled; we force it on for the drop.)
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = true;
            }

            // 6) Reduce ground penetration by nudging up based on first solid collider bounds (if present).
            Collider solidCollider = null;
            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c != null && !c.isTrigger)
                {
                    solidCollider = c;
                    break;
                }
            }

            if (solidCollider != null)
            {
                var bounds = solidCollider.bounds;
                float minLift = Mathf.Max(extraUpOffsetIfIntersecting, bounds.extents.y * 0.5f);
                go.transform.position += Vector3.up * minLift;
            }

            // 7) Initialize quantity BEFORE spawning network object.
            drop.ServerInitialize(quantity, null, def);

            // 8) Spawn network object so all clients see it.
            netObj.Spawn(true);

            // 9) Apply Valheim-like pop + push + spin.
            var rb = go.GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                // Small random horizontal direction and magnitude.
                Vector3 horizontalDir = new Vector3(scatter2D.x, 0f, scatter2D.y);
                if (horizontalDir.sqrMagnitude < 0.0001f)
                    horizontalDir = UnityEngine.Random.onUnitSphere; // safety
                horizontalDir.y = 0f;
                horizontalDir.Normalize();

                float up = dropUpwardImpulse + UnityEngine.Random.Range(0f, dropUpwardVariance);
                float side = dropRandomForce + UnityEngine.Random.Range(0f, dropHorizontalVariance);

                Vector3 impulse = Vector3.up * up + horizontalDir * side;
                rb.AddForce(impulse, ForceMode.Impulse);

                // Random spin so pieces feel alive.
                Vector3 torqueAxis = UnityEngine.Random.onUnitSphere;
                rb.AddTorque(torqueAxis * dropRandomTorque, ForceMode.Impulse);
            }

            // 10) Optional cleanup.
            if (harvestedDropLifetimeSeconds > 0f)
                drop.ServerScheduleAutoDespawn(harvestedDropLifetimeSeconds);

            return true;
        }

        #endregion

        // --------------------------------------------------------------------
        // Helpers (validation + math + logging)
        // --------------------------------------------------------------------
        #region Helpers

        private bool EnsureDependencies()
        {
            if (inventory == null)
                inventory = GetComponent<PlayerInventoryNet>();

            if (skills == null)
                skills = GetComponent<SkillsNet>();

            if (nodeRegistry == null)
                nodeRegistry = ResourceNodeRegistry.Instance ?? FindFirstObjectByType<ResourceNodeRegistry>();

            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            if (playerMovement == null)
                playerMovement = GetComponent<PlayerMovement>();

            if (harvestAnim == null)
                harvestAnim = GetComponent<PlayerHarvestAnimNet>();

            if (playerVitals == null)
                playerVitals = GetComponent<PlayerVitalsNet>();

            if (itemDatabase == null)
                itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (wallet == null)
                wallet = GetComponent<WalletNet>();

            // Skills can be optional in some MVP setups; the rest are required.
            return inventory != null && nodeRegistry != null && equipment != null && itemDatabase != null;
        }

        private bool IsWithinRange(Vector3 targetPosition, float maxDistance)
        {
            float dist = Vector3.Distance(transform.position, targetPosition);
            return dist <= maxDistance + 0.01f;
        }

        private float ComputeDistanceToNode(ResourceNodeNet node)
        {
            if (node == null)
                return -1f;

            return Vector3.Distance(transform.position, node.transform.position);
        }

        private bool TryProcessServerHitRequest(ResourceNodeNet node, string nodeIdForResult)
        {
            if (node == null)
            {
                SendHarvestResult(false, HarvestFailureReason.NodeNotFound, nodeIdForResult ?? string.Empty, string.Empty, 0);
                return false;
            }

            if (!node.IsSpawned)
            {
                SendHarvestResult(false, HarvestFailureReason.NodeNotFound, nodeIdForResult ?? string.Empty, node.DropItemId, 0);
                return false;
            }

            if (!node.IsHarvestableNow() || node.CurrentHealth <= 0)
            {
                SendHarvestResult(false, HarvestFailureReason.NodeOnCooldown, nodeIdForResult ?? node.NodeId, node.DropItemId, 0);
                return false;
            }

            if (!IsWithinRange(node.transform.position, nodeMaxDistance))
            {
                SendHarvestResult(false, HarvestFailureReason.OutOfRange, nodeIdForResult ?? node.NodeId, node.DropItemId, 0);
                return false;
            }

            if (!TryValidateToolRequirement(node, out var toolFailureReason))
            {
                SendHarvestResult(false, toolFailureReason, nodeIdForResult ?? node.NodeId, node.DropItemId, 0);
                return false;
            }

            double now = ServerTimeNow();
            float intervalSeconds = ResolveSwingIntervalSeconds(node, out var equippedItemId, out var swingSpeedRaw, out var conversionMode, out var sourceLabel);

            if (now < _nextAllowedServerHitTime)
            {
                if (debugSwingRate)
                {
                    Debug.Log($"[HarvestingNet][SERVER][SwingRate] REJECT owner={OwnerClientId} itemId={equippedItemId} source={sourceLabel} raw={swingSpeedRaw:0.###} interval={intervalSeconds:0.###} now={now:0.###} nextAllowed={_nextAllowedServerHitTime:0.###}");
                }

                SendHarvestResult(false, HarvestFailureReason.HitRateLimited, nodeIdForResult ?? node.NodeId, node.DropItemId, 0);
                return false;
            }

            _nextAllowedServerHitTime = now + intervalSeconds;

            if (debugSwingRate)
            {
                Debug.Log($"[HarvestingNet][SERVER][SwingRate] ACCEPT owner={OwnerClientId} itemId={equippedItemId} source={sourceLabel} raw={swingSpeedRaw:0.###} mode={conversionMode} interval={intervalSeconds:0.###} now={now:0.###} nextAllowed={_nextAllowedServerHitTime:0.###}");
            }

            if (harvestAnim != null)
                harvestAnim.ServerPlaySwing(ToolToAnim(node.RequiredTool));

            int damage = Mathf.Max(1, hitDamagePerSwing);
            bool depleted = node.ServerApplyDamage(damage);

            if (damage > 0 && playerVitals != null)
            {
                playerVitals.ServerSpendStamina(1);
                playerVitals.ServerMarkBusyFromAction();
            }

            if (depleted)
                HandleNodeDepleted(node, equippedItemId);

            return true;
        }

        private float ResolveSwingIntervalSeconds(
            ResourceNodeNet node,
            out string equippedItemId,
            out float swingSpeedRaw,
            out string conversionMode,
            out string sourceLabel)
        {
            equippedItemId = string.Empty;
            swingSpeedRaw = 0f;
            conversionMode = "FallbackCooldown";
            sourceLabel = "fallback";

            float fallbackInterval = Mathf.Max(0.01f, hitCooldownSeconds);

            if (!TryGetEquippedSwingItem(node, out equippedItemId, out var equippedDef, out sourceLabel) || equippedDef == null)
                return fallbackInterval;

            swingSpeedRaw = Mathf.Max(0.0001f, equippedDef.SwingSpeed);

            float interval = ConvertSwingSpeedToIntervalSeconds(swingSpeedRaw, out conversionMode);
            return Mathf.Max(0.01f, interval);
        }

        private bool TryGetEquippedSwingItem(ResourceNodeNet node, out string itemId, out ItemDef def, out string sourceLabel)
        {
            itemId = string.Empty;
            def = null;
            sourceLabel = "none";

            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            if (equipment == null)
                return false;

            string requiredItemId = node != null ? node.RequiredToolItemId : string.Empty;
            if (!string.IsNullOrWhiteSpace(requiredItemId) && equipment.HasEquippedItem(requiredItemId) && equipment.TryGetItemDef(requiredItemId, out def) && def != null)
            {
                itemId = requiredItemId;
                sourceLabel = "required-item";
                return true;
            }

            ToolTag requiredTag = ToToolTag(node != null ? node.RequiredTool : ToolType.None);

            if (TryGetSlotItemDef(EquipSlot.MainHand, requiredTag, out itemId, out def))
            {
                sourceLabel = "main-hand";
                return true;
            }

            if (TryGetSlotItemDef(EquipSlot.OffHand, requiredTag, out itemId, out def))
            {
                sourceLabel = "off-hand";
                return true;
            }

            if (requiredTag == ToolTag.None)
            {
                if (TryGetSlotItemDef(EquipSlot.MainHand, ToolTag.None, out itemId, out def))
                {
                    sourceLabel = "main-hand-any";
                    return true;
                }

                if (TryGetSlotItemDef(EquipSlot.OffHand, ToolTag.None, out itemId, out def))
                {
                    sourceLabel = "off-hand-any";
                    return true;
                }
            }

            return false;
        }

        private bool TryGetSlotItemDef(EquipSlot slot, ToolTag requiredTag, out string itemId, out ItemDef def)
        {
            itemId = string.Empty;
            def = null;

            if (equipment == null)
                return false;

            string equippedId = equipment.GetEquippedItemId(slot);
            if (string.IsNullOrWhiteSpace(equippedId))
                return false;

            if (!equipment.TryGetItemDef(equippedId, out def) || def == null)
                return false;

            if (requiredTag != ToolTag.None && !ItemHasToolTag(def, requiredTag))
                return false;

            itemId = equippedId;
            return true;
        }

        private static bool ItemHasToolTag(ItemDef def, ToolTag requiredTag)
        {
            if (def == null || requiredTag == ToolTag.None)
                return true;

            var tags = def.ToolTags;
            if (tags == null || tags.Length == 0)
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == requiredTag)
                    return true;
            }

            return false;
        }

        private static ToolTag ToToolTag(ToolType tool)
        {
            return tool switch
            {
                ToolType.Axe => ToolTag.Axe,
                ToolType.Pickaxe => ToolTag.Pickaxe,
                ToolType.Sickle => ToolTag.Sickle,
                ToolType.Knife => ToolTag.Knife,
                _ => ToolTag.None
            };
        }

        private float ConvertSwingSpeedToIntervalSeconds(float swingSpeedRaw, out string conversionMode)
        {
            float safeRaw = Mathf.Max(0.0001f, swingSpeedRaw);

            switch (swingSpeedUnit)
            {
                case SwingSpeedUnit.SwingsPerSecond:
                    conversionMode = "SwingsPerSecond";
                    return 1f / safeRaw;

                case SwingSpeedUnit.SecondsPerSwing:
                    conversionMode = "SecondsPerSwing";
                    return safeRaw;

                default:
                    bool looksLikePerSecond = safeRaw >= 1f;
                    conversionMode = looksLikePerSecond ? "AutoDetect->SwingsPerSecond" : "AutoDetect->SecondsPerSwing";
                    return looksLikePerSecond ? (1f / safeRaw) : safeRaw;
            }
        }
        private bool HasToolTypeEquipped(ToolType requiredTool)
        {
            if (requiredTool == ToolType.None)
                return true;

            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            if (equipment == null)
                return false;

            return equipment.HasEquippedToolType(requiredTool);
        }

        private static HarvestToolAnim ToolToAnim(ToolType tool)
        {
            return tool switch
            {
                ToolType.Axe => HarvestToolAnim.Axe,
                ToolType.Pickaxe => HarvestToolAnim.Pickaxe,
                _ => HarvestToolAnim.None
            };
        }

        private string GetEquipmentSnapshot()
        {
            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            return equipment != null ? equipment.BuildServerDebugString() : "equipment=null";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x.ToString("F3", CultureInfo.InvariantCulture)}, {value.y.ToString("F3", CultureInfo.InvariantCulture)}, {value.z.ToString("F3", CultureInfo.InvariantCulture)})";
        }

        private void LogHarvestEvent(string stage, ResourceNodeNet node, HarvestFailureReason reason, float computedDistance, string details = null)
        {
            if (!IsServer)
                return;

            var builder = new StringBuilder(512);

            string dropId = node != null ? node.DropItemId : string.Empty;
            string requiredTool = node != null ? node.RequiredTool.ToString() : "None";
            string requiredItemId = node != null ? node.RequiredToolItemId : string.Empty;
            string nodeId = node != null ? node.NodeId : "null";
            Vector3 nodePos = node != null ? node.transform.position : Vector3.zero;
            Vector3 playerPos = transform.position;

            builder.Append("[HarvestingNet][SERVER] ")
                .Append(stage)
                .Append(" owner=").Append(OwnerClientId)
                .Append(" nodeId=").Append(nodeId)
                .Append(" drop=").Append(string.IsNullOrWhiteSpace(dropId) ? "<none>" : dropId)
                .Append(" requiredTool=").Append(requiredTool)
                .Append(" requiredItem=").Append(string.IsNullOrWhiteSpace(requiredItemId) ? "<none>" : requiredItemId)
                .Append(" playerPos=").Append(FormatVector(playerPos))
                .Append(" nodePos=").Append(node != null ? FormatVector(nodePos) : "n/a")
                .Append(" distance=").Append(computedDistance >= 0f ? computedDistance.ToString("F3", CultureInfo.InvariantCulture) : "n/a")
                .Append(" equipment=").Append(GetEquipmentSnapshot());

            if (reason != HarvestFailureReason.None)
                builder.Append(" reason=").Append(reason);

            if (!string.IsNullOrWhiteSpace(details))
                builder.Append(" details=").Append(details);

            Debug.Log(builder.ToString());
        }

        private bool TryValidateToolRequirement(ResourceNodeNet node, out HarvestFailureReason failureReason)
        {
            failureReason = HarvestFailureReason.None;

            if (node == null)
            {
                failureReason = HarvestFailureReason.ConfigError;
                LogHarvestEvent("ToolValidation.NodeNull", null, failureReason, -1f);
                return false;
            }

            if (equipment == null)
                equipment = GetComponent<PlayerEquipmentNet>();

            if (equipment == null)
            {
                failureReason = HarvestFailureReason.ConfigError;
                LogHarvestEvent("ToolValidation.NoEquipment", node, failureReason, ComputeDistanceToNode(node));
                return false;
            }

            // Specific required item id (ex: IT_IronAxe)
            string requiredItemId = node.RequiredToolItemId;
            if (!string.IsNullOrWhiteSpace(requiredItemId))
            {
                if (!equipment.HasEquippedItem(requiredItemId))
                {
                    failureReason = HarvestFailureReason.MissingTool;
                    LogHarvestEvent("ToolValidation.MissingSpecificItem", node, failureReason, ComputeDistanceToNode(node), $"requiredItem={requiredItemId}");
                    return false;
                }
            }

            // Required tool type (Axe, Pickaxe...)
            if (!HasToolTypeEquipped(node.RequiredTool))
            {
                failureReason = HarvestFailureReason.WrongTool;
                LogHarvestEvent("ToolValidation.WrongToolType", node, failureReason, ComputeDistanceToNode(node), $"requiredTool={node.RequiredTool}");
                return false;
            }

            return true;
        }

        private int CalculateNodeYield(int baseYield, int level)
        {
            int scaled = baseYield + Mathf.FloorToInt(level / (float)NodeYieldDivisor);
            return Mathf.Clamp(scaled, 1, nodeYieldMax);
        }

        private int CalculateDropYield(int baseQuantity, int level)
        {
            int scaled = baseQuantity + Mathf.FloorToInt(level / (float)DropYieldDivisor);
            return Mathf.Clamp(scaled, 1, dropYieldMax);
        }

        private bool CanInventoryAccept(string itemId, int quantity)
        {
            if (inventory == null || inventory.Grid == null)
                return false;

            if (!inventory.Grid.CanAdd(itemId, quantity, out var remainder))
                return false;

            return remainder == 0;
        }

        private bool IsWalletCoinDrop(ResourceDrop drop, string itemId)
        {
            if (drop == null)
                return false;

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            string canonical = itemId.Trim();

            if (walletCoinItemDef != null)
            {
                if (ReferenceEquals(drop.ItemDefinition, walletCoinItemDef))
                    return true;

                string configuredCoinId = walletCoinItemDef.ItemId;
                if (!string.IsNullOrWhiteSpace(configuredCoinId) &&
                    canonical.Equals(configuredCoinId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(walletCoinItemId) &&
                canonical.Equals(walletCoinItemId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private int GetSkillLevel(string skillId)
        {
            if (skills == null || string.IsNullOrWhiteSpace(skillId))
                return 0;

            return Mathf.Clamp(skills.GetLevel(skillId), 0, 100);
        }

        private void GrantXp(string skillId, int amount)
        {
            if (skills == null)
                return;

            if (amount <= 0)
                return;

            if (string.IsNullOrWhiteSpace(skillId))
                return;

            skills.AddXp(skillId, amount);
        }

        private static string SkillIdForResource(ResourceType resourceType)
        {
            return resourceType switch
            {
                ResourceType.Wood => SkillId.Woodcutting,
                ResourceType.Stone => SkillId.Mining,
                ResourceType.Fiber => SkillId.Foraging,
                _ => SkillId.Foraging
            };
        }

        private static bool TryGetSkillForItem(string itemId, out string skillId)
        {
            skillId = string.Empty;

            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            string canonical = itemId.Trim();

            if (canonical.Equals("IT_Wood", StringComparison.OrdinalIgnoreCase))
            {
                skillId = SkillId.Woodcutting;
                return true;
            }

            if (canonical.Equals("IT_Stone", StringComparison.OrdinalIgnoreCase))
            {
                skillId = SkillId.Mining;
                return true;
            }

            if (canonical.Equals("IT_Fiber", StringComparison.OrdinalIgnoreCase))
            {
                skillId = SkillId.Foraging;
                return true;
            }

            return false;
        }

        private static double ServerTimeNow()
        {
            if (NetworkManager.Singleton == null)
                return Time.timeAsDouble;

            return NetworkManager.Singleton.ServerTime.Time;
        }

        private static List<RareDropResult> ConvertRareDropPayload(RareDropPayload[] payload)
        {
            var result = new List<RareDropResult>(payload?.Length ?? 0);

            if (payload == null || payload.Length == 0)
                return result;

            for (int i = 0; i < payload.Length; i++)
            {
                var entry = payload[i];

                if (string.IsNullOrWhiteSpace(entry.ItemId))
                    continue;

                if (entry.Quantity <= 0)
                    continue;

                result.Add(new RareDropResult(entry.ItemId, entry.Quantity));
            }

            return result;
        }

        #endregion

        // --------------------------------------------------------------------
        // Network payload types
        // --------------------------------------------------------------------
        #region Network Payload Types

        [Serializable]
        public struct RareDropPayload : INetworkSerializable
        {
            public string ItemId;
            public int Quantity;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref ItemId);
                serializer.SerializeValue(ref Quantity);
            }
        }

        #endregion

        // --------------------------------------------------------------------
        // Client events (UI hooks)
        // --------------------------------------------------------------------
        #region Client Events

        public event Action<HarvestResultEvent> OnHarvestResult;

        public readonly struct HarvestResultEvent
        {
            public HarvestResultEvent(
                bool success,
                HarvestFailureReason reason,
                string nodeId,
                string itemId,
                int amountAwarded,
                IReadOnlyList<RareDropResult> rareDrops)
            {
                Success = success;
                Reason = reason;
                NodeId = nodeId;
                ItemId = itemId;
                AmountAwarded = amountAwarded;
                RareDrops = rareDrops;
            }

            public bool Success { get; }
            public HarvestFailureReason Reason { get; }
            public string NodeId { get; }
            public string ItemId { get; }
            public int AmountAwarded { get; }
            public IReadOnlyList<RareDropResult> RareDrops { get; }
        }

        public readonly struct RareDropResult
        {
            public RareDropResult(string itemId, int quantity)
            {
                ItemId = itemId;
                Quantity = quantity;
            }

            public string ItemId { get; }
            public int Quantity { get; }
        }

        #endregion
    }

    /// <summary>
    /// Reasons returned to the client for harvest/pickup failures.
    /// Keep this stable because UI may switch on it.
    /// </summary>
    public enum HarvestFailureReason
    {
        None,
        NodeNotFound,
        NodeOnCooldown,
        NodeLocked,
        OutOfRange,
        InventoryFull,
        ConfigError,
        DropMissing,
        AlreadyConsumed,
        WrongTool,
        MissingTool,
        AlreadyHarvesting,
        CancelledByPlayer,
        CancelledByServer,
        HitRateLimited
    }
}















