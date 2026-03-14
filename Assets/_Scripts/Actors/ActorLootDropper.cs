using System.Collections.Generic;
using HuntersAndCollectors.Items;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.Actors
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ActorLootDropper : NetworkBehaviour
    {
        [Header("Loot Source")]
        [Tooltip("Optional explicit override. When left empty, the component will fall back to ActorDef.LootTable.")]
        [SerializeField] private ActorLootTableDef lootTable;

        [Tooltip("Optional item database used to resolve the shared coin item for actor coin drops.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("References")]
        [Tooltip("Optional authored binder reference. Auto-resolved when missing.")]
        [SerializeField] private ActorDefBinder actorDefBinder;

        [Header("Drop Spawn Tuning")]
        [Min(0f)]
        [Tooltip("Random horizontal scatter radius around actor death position.")]
        [SerializeField] private float scatterRadius = 0.85f;

        [Min(0f)]
        [Tooltip("Random impulse strength applied to spawned loot so drops bounce/scatter physically.")]
        [SerializeField] private float randomForce = 1.2f;

        [Tooltip("If enabled, drops are raycast-snapped to ground before spawn.")]
        [SerializeField] private bool groundSnap = true;

        [Min(0f)]
        [Tooltip("Height above ground hit point where the drop is placed.")]
        [SerializeField] private float spawnHeight = 0.25f;

        [Min(0f)]
        [Tooltip("Optional auto-despawn lifetime for actor death drops. 0 = never.")]
        [SerializeField] private float lifetimeSeconds = 120f;

        [Tooltip("Ground layers only (terrain/static world).")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Min(0f)]
        [Tooltip("Ray origin height above actor position when searching for ground.")]
        [SerializeField] private float groundRayHeight = 6f;

        [Min(0f)]
        [Tooltip("Raycast distance for searching downward ground hits.")]
        [SerializeField] private float groundRayDistance = 30f;

        [Min(0f)]
        [Tooltip("Extra vertical nudge after spawn to reduce collider penetration.")]
        [SerializeField] private float extraUpOffsetIfIntersecting = 0.15f;

        private readonly List<ActorLootTableDef.ResolvedLootDrop> rolledDrops = new(16);

        private void Awake()
        {
            if (actorDefBinder == null)
                actorDefBinder = GetComponent<ActorDefBinder>();
        }

        public void ServerDropLoot()
        {
            if (!IsServer || !IsSpawned)
                return;

            ActorLootTableDef resolvedLootTable = ResolveLootTable();
            ActorDef actorDef = ResolveActorDef();
            rolledDrops.Clear();

            if (resolvedLootTable != null)
                resolvedLootTable.RollLoot(rolledDrops);

            Debug.Log($"[ActorLoot][SERVER] Actor died, rolling loot actor='{name}' netId={NetworkObjectId}", this);

            for (int i = 0; i < rolledDrops.Count; i++)
                SpawnLootEntry(rolledDrops[i].item, rolledDrops[i].quantity);

            if (resolvedLootTable != null && resolvedLootTable.TryRollCoins(out ItemDef coinItem, out int tableCoinQuantity))
                SpawnLootEntry(coinItem, tableCoinQuantity);
            else if (TryResolveActorCoinDrop(actorDef, out ItemDef actorCoinItem, out int actorCoinQuantity))
                SpawnLootEntry(actorCoinItem, actorCoinQuantity);
        }

        private ActorLootTableDef ResolveLootTable()
        {
            if (lootTable != null)
                return lootTable;

            if (actorDefBinder == null)
                actorDefBinder = GetComponent<ActorDefBinder>();

            return actorDefBinder != null && actorDefBinder.ActorDef != null
                ? actorDefBinder.ActorDef.LootTable
                : null;
        }

        private ActorDef ResolveActorDef()
        {
            if (actorDefBinder == null)
                actorDefBinder = GetComponent<ActorDefBinder>();

            return actorDefBinder != null ? actorDefBinder.ActorDef : null;
        }

        private bool TryResolveActorCoinDrop(ActorDef actorDef, out ItemDef coinItem, out int quantity)
        {
            coinItem = null;
            quantity = 0;

            if (actorDef == null)
                return false;

            if (actorDef.CoinDropMax <= 0)
                return false;

            quantity = Random.Range(Mathf.Max(0, actorDef.CoinDropMin), Mathf.Max(Mathf.Max(0, actorDef.CoinDropMin), actorDef.CoinDropMax) + 1);
            if (quantity <= 0)
                return false;

            if (itemDatabase != null)
            {
                if (!itemDatabase.TryGet("IT_Coin", out coinItem) || coinItem == null)
                    itemDatabase.TryGet("it_coin", out coinItem);
            }

            return coinItem != null;
        }

        private void SpawnLootEntry(ItemDef item, int quantity)
        {
            if (item == null || quantity <= 0)
                return;

            GameObject prefab = item.WorldDropPrefab;
            if (prefab == null)
            {
                Debug.LogError($"[ActorLoot][SERVER] Missing WorldDropPrefab itemId='{item.ItemId}' actor='{name}'", this);
                return;
            }

            Vector2 scatter2D = Random.insideUnitCircle * scatterRadius;
            Vector3 xzOffset = new Vector3(scatter2D.x, 0f, scatter2D.y);

            Vector3 actorPos = transform.position;
            Vector3 spawnPos = actorPos + xzOffset + Vector3.up * spawnHeight;

            if (groundSnap)
            {
                Vector3 rayOrigin = actorPos + xzOffset + Vector3.up * groundRayHeight;
                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, groundRayDistance, groundMask, QueryTriggerInteraction.Ignore))
                    spawnPos = hit.point + Vector3.up * spawnHeight;
            }

            GameObject go = Instantiate(prefab, spawnPos, Quaternion.identity);
            go.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(go, gameObject.scene);

            if (!go.TryGetComponent<NetworkObject>(out NetworkObject netObj) || netObj == null)
            {
                Debug.LogError($"[ActorLoot][SERVER] Prefab missing NetworkObject prefab='{prefab.name}' itemId='{item.ItemId}'", this);
                Destroy(go);
                return;
            }

            if (!go.TryGetComponent<ResourceDrop>(out ResourceDrop drop) || drop == null)
                drop = go.GetComponentInChildren<ResourceDrop>(true);

            if (drop == null)
            {
                Debug.LogError($"[ActorLoot][SERVER] Prefab missing ResourceDrop prefab='{prefab.name}' itemId='{item.ItemId}'", this);
                Destroy(go);
                return;
            }

            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = true;
            }

            Collider solidCollider = null;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c != null && !c.isTrigger)
                {
                    solidCollider = c;
                    break;
                }
            }

            if (solidCollider != null)
            {
                Bounds bounds = solidCollider.bounds;
                float minLift = Mathf.Max(extraUpOffsetIfIntersecting, bounds.extents.y * 0.5f);
                go.transform.position += Vector3.up * minLift;
            }

            drop.ServerInitialize(quantity, null, item);
            netObj.Spawn(true);

            Rigidbody rb = go.GetComponentInChildren<Rigidbody>();
            if (rb != null && randomForce > 0f)
            {
                Vector3 horizontal = new Vector3(scatter2D.x, 0f, scatter2D.y);
                if (horizontal.sqrMagnitude < 0.0001f)
                    horizontal = Random.insideUnitSphere;

                horizontal.y = 0f;
                horizontal.Normalize();

                float side = Random.Range(randomForce * 0.5f, randomForce);
                float up = randomForce * 0.35f;
                rb.AddForce(horizontal * side + Vector3.up * up, ForceMode.Impulse);
            }

            if (lifetimeSeconds > 0f)
                drop.ServerScheduleAutoDespawn(lifetimeSeconds);

            Debug.Log($"[ActorLoot][SERVER] Loot spawned itemId='{item.ItemId}' qty={quantity} actor='{name}' netId={netObj.NetworkObjectId}", this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (actorDefBinder == null)
                actorDefBinder = GetComponent<ActorDefBinder>();

            scatterRadius = Mathf.Max(0f, scatterRadius);
            randomForce = Mathf.Max(0f, randomForce);
            spawnHeight = Mathf.Max(0f, spawnHeight);
            lifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            groundRayHeight = Mathf.Max(0f, groundRayHeight);
            groundRayDistance = Mathf.Max(0f, groundRayDistance);
            extraUpOffsetIfIntersecting = Mathf.Max(0f, extraUpOffsetIfIntersecting);
        }
#endif
    }
}
