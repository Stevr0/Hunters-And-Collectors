using System;
using HuntersAndCollectors.Items;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// PlacedBuildPiece
    /// --------------------------------------------------------------------
    /// Server-authoritative runtime structure component for placed build items.
    ///
    /// Responsibilities in this first pass:
    /// - Store source item id that created this structure.
    /// - Hold authoritative current health/max health state.
    /// - Own a stable persistence id for save/load matching.
    /// - Accept server-side damage.
    /// - Despawn on zero health.
    /// - Trigger shelter re-evaluation after destruction.
    /// - Register/unregister with runtime persistence registry.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlacedBuildPiece : NetworkBehaviour
    {
        [Header("Source")]
        [SerializeField] private string sourceItemId;

        [Header("Persistence")]
        [SerializeField] private string persistentId;
        [SerializeField] private ulong ownerPlayerId;

        [Header("Structure Health")]
        [Min(1)]
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private bool destroyOnZeroHealth = true;

        // Server-authoritative health state. Clients only observe.
        private readonly NetworkVariable<int> currentHealth =
            new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Optional replicated max health so clients can read accurate values even when
        // source item health differs from prefab defaults.
        private readonly NetworkVariable<int> replicatedMaxHealth =
            new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private bool isDestroyedOrPendingDestroy;

        /// <summary>
        /// Stable source item id used to place this structure (eg: IT_Wall).
        /// </summary>
        public string SourceItemId => sourceItemId;

        /// <summary>
        /// Stable persistence id used to match shard save rows back to the correct runtime object.
        /// This is generated once on the server and restored from shard save on reload.
        /// </summary>
        public string PersistentId => persistentId;

        /// <summary>
        /// Backward-compatible alias for older code still reading BuildPieceId.
        /// </summary>
        public string BuildPieceId => sourceItemId;

        public ulong OwnerPlayerId => ownerPlayerId;

        /// <summary>
        /// Current replicated structure health.
        /// </summary>
        public int CurrentHealth => Mathf.Max(0, currentHealth.Value);

        /// <summary>
        /// Current replicated max structure health.
        /// </summary>
        public int MaxHealth => Mathf.Max(1, replicatedMaxHealth.Value);

        /// <summary>
        /// True after destruction has started, so duplicate damage calls are ignored safely.
        /// </summary>
        public bool IsDestroyedOrPendingDestroy => isDestroyedOrPendingDestroy;

        /// <summary>
        /// Small convenience helper used by systems that classify placed pieces.
        /// </summary>
        public bool MatchesItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;

            return string.Equals(sourceItemId, itemId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// SERVER ONLY helper so placement system can stamp the source item id.
        /// Uses server-session authority rather than IsServer so pre-spawn restores can initialize safely.
        /// </summary>
        public void ServerSetSourceItemId(string value)
        {
            if (!HasServerAuthorityContext())
                return;

            sourceItemId = value ?? string.Empty;
        }

        public void ServerSetOwnerPlayerId(ulong value)
        {
            if (!HasServerAuthorityContext())
                return;

            ownerPlayerId = value;
        }

        public void ServerSetPersistentId(string value)
        {
            if (!HasServerAuthorityContext())
                return;

            persistentId = string.IsNullOrWhiteSpace(value) ? GeneratePersistentId() : value.Trim();
        }

        public void ServerEnsurePersistentId()
        {
            if (!HasServerAuthorityContext())
                return;

            if (string.IsNullOrWhiteSpace(persistentId))
                persistentId = GeneratePersistentId();
        }

        /// <summary>
        /// Backward-compatible setter wrapper.
        /// </summary>
        public void ServerSetBuildPieceId(string value)
        {
            ServerSetSourceItemId(value);
        }

        /// <summary>
        /// SERVER ONLY: Initializes this placed structure from the source placeable item def.
        /// Must be called by BuildingNet before spawn.
        /// </summary>
        public void ServerInitializeFromItem(ItemDef sourceItemDef)
        {
            if (!HasServerAuthorityContext())
                return;

            if (sourceItemDef == null)
                return;

            ServerEnsurePersistentId();
            sourceItemId = sourceItemDef.ItemId ?? string.Empty;
            maxHealth = Mathf.Max(1, sourceItemDef.StructureMaxHealth);
            destroyOnZeroHealth = sourceItemDef.DestroyOnZeroHealth;

            replicatedMaxHealth.Value = maxHealth;
            currentHealth.Value = maxHealth;
            isDestroyedOrPendingDestroy = false;

            Debug.Log($"[PlacedBuildPiece][SERVER] Initialized persistentId={persistentId} sourceItemId={sourceItemId} maxHealth={maxHealth}", this);
        }

        /// <summary>
        /// SERVER ONLY: restore this runtime piece from save data.
        /// </summary>
        public void ServerInitializeFromSave(string savedPersistentId, ItemDef sourceItemDef, int savedCurrentHealth, int savedMaxHealth, ulong savedOwnerPlayerId)
        {
            if (!HasServerAuthorityContext())
                return;

            ServerSetPersistentId(savedPersistentId);
            ServerInitializeFromItem(sourceItemDef);
            ownerPlayerId = savedOwnerPlayerId;

            int baseMax = Mathf.Max(1, sourceItemDef != null ? sourceItemDef.StructureMaxHealth : maxHealth);
            int restoredMax = Mathf.Max(1, savedMaxHealth > 0 ? savedMaxHealth : baseMax);
            int restoredCurrent = Mathf.Clamp(savedCurrentHealth > 0 ? savedCurrentHealth : restoredMax, 1, restoredMax);

            maxHealth = restoredMax;
            replicatedMaxHealth.Value = restoredMax;
            currentHealth.Value = restoredCurrent;
            isDestroyedOrPendingDestroy = false;
        }

        /// <summary>
        /// SERVER ONLY: Applies structure damage.
        /// Returns true only when damage was accepted and applied.
        /// </summary>
        public bool ServerTryApplyDamage(int damageAmount)
        {
            if (!IsServer)
                return false;

            if (damageAmount <= 0)
                return false;

            if (isDestroyedOrPendingDestroy)
            {
                Debug.Log("[PlacedBuildPiece][SERVER] Damage ignored: already destroyed.", this);
                return false;
            }

            int safeMaxHealth = Mathf.Max(1, MaxHealth);
            int oldHealth = Mathf.Clamp(CurrentHealth, 0, safeMaxHealth);
            int newHealth = Mathf.Clamp(oldHealth - damageAmount, 0, safeMaxHealth);

            if (newHealth == oldHealth)
                return false;

            currentHealth.Value = newHealth;

            Debug.Log($"[PlacedBuildPiece][SERVER] Damage applied persistentId={persistentId} sourceItemId={sourceItemId} damage={damageAmount} current={newHealth}/{safeMaxHealth}", this);

            if (newHealth <= 0)
            {
                if (destroyOnZeroHealth)
                    ServerDestroyStructure();
                else
                    isDestroyedOrPendingDestroy = true;
            }

            return true;
        }

        /// <summary>
        /// SERVER ONLY: Destroys this placed structure by despawning its NetworkObject.
        /// </summary>
        public void ServerDestroyStructure()
        {
            if (!IsServer)
                return;

            if (isDestroyedOrPendingDestroy)
                return;

            isDestroyedOrPendingDestroy = true;
            currentHealth.Value = 0;

            Debug.Log($"[PlacedBuildPiece][SERVER] Destroyed persistentId={persistentId} sourceItemId={sourceItemId}", this);

            ServerNotifyShelterStatePieceChanged();

            NetworkObject networkObject = NetworkObject;
            if (networkObject != null && networkObject.IsSpawned)
                networkObject.Despawn(destroy: true);
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            ServerEnsurePersistentId();
            Debug.Log($"[PlacedBuildPiece] OnNetworkSpawn persistentId={persistentId}", this);

            // Defensive fallback in case object is spawned without explicit initialization.
            if (replicatedMaxHealth.Value < 1)
                replicatedMaxHealth.Value = Mathf.Max(1, maxHealth);

            if (currentHealth.Value < 1)
                currentHealth.Value = replicatedMaxHealth.Value;

            PlacedBuildPieceRegistry.Register(this);
            Debug.Log($"[PlacedBuildPiece][SERVER] Spawned persistentId={persistentId} sourceItemId={sourceItemId} health={CurrentHealth}/{MaxHealth} at pos={transform.position}", this);
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer)
                return;

            PlacedBuildPieceRegistry.Unregister(this);
        }

        public override void OnDestroy()
        {
            // Defensive cleanup when object is destroyed while not fully spawned.
            PlacedBuildPieceRegistry.Unregister(this);
            base.OnDestroy();
        }

        private bool HasServerAuthorityContext()
        {
            return IsServer || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
        }

        private void ServerNotifyShelterStatePieceChanged()
        {
            if (!IsServer)
                return;

            StructureRequirementController.ServerReevaluateAllActive();
        }

        private static string GeneratePersistentId()
        {
            return $"PB_{Guid.NewGuid():N}";
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (maxHealth < 1)
                maxHealth = 1;
        }
#endif
    }

    /// <summary>
    /// Server-side runtime registry for placed build pieces.
    /// Kept in this compiled file so it is always included by Unity-generated csproj.
    /// </summary>
    public static class PlacedBuildPieceRegistry
    {
        private static readonly System.Collections.Generic.HashSet<PlacedBuildPiece> Active = new();

        public static void Register(PlacedBuildPiece piece)
        {
            if (piece == null)
                return;

            Active.Add(piece);
        }

        public static void Unregister(PlacedBuildPiece piece)
        {
            if (piece == null)
                return;

            Active.Remove(piece);
        }

        public static System.Collections.Generic.List<PlacedBuildPiece> Snapshot()
        {
            var result = new System.Collections.Generic.List<PlacedBuildPiece>(Active.Count);
            foreach (PlacedBuildPiece piece in Active)
            {
                if (piece != null)
                    result.Add(piece);
            }

            return result;
        }
    }
}


