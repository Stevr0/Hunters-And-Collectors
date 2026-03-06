using HuntersAndCollectors.Combat;
using HuntersAndCollectors.Skills;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Runtime bridge from scene/prefab objects to ActorDef baseline data.
    ///
    /// Prefab wiring guidance:
    /// - Player: ActorDefBinder + ActorIdentityNet + ActorStatsProvider + SkillsNet + PlayerEquipmentNet + (optional) PvpToggleNet + DamageableNet/HealthNet.
    /// - Training Dummy: ActorDefBinder + ActorIdentityNet + ActorStatsProvider + DamageableNet/HealthNet.
    /// - NPC: ActorDefBinder + ActorIdentityNet + ActorStatsProvider + (optional) SkillsNet + (optional) equipment + DamageableNet/HealthNet.
    ///
    /// Authority model:
    /// - On server spawn, this applies ActorDef social defaults to ActorIdentityNet (when uninitialized)
    ///   and applies starting skills through SkillsNet.
    /// - Clients only read replicated results.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorDefBinder : MonoBehaviour
    {
        [SerializeField] private ActorDef actorDef;

        public ActorDef ActorDef => actorDef;

        private NetworkObject networkObject;
        private ActorIdentityNet actorIdentity;
        private SkillsNet skillsNet;
        private HealthNet healthNet;

        private bool serverDefaultsApplied;
        private bool warnedMissingActorDef;

        private void Awake()
        {
            CacheRefs();
        }

        private void Start()
        {
            TryApplyServerDefaults();
        }

        private void OnEnable()
        {
            TryApplyServerDefaults();
        }

        private void Update()
        {
            // NetworkObject may not be spawned during Start/OnEnable in some spawn paths.
            if (!serverDefaultsApplied)
                TryApplyServerDefaults();
        }

        private void CacheRefs()
        {
            if (networkObject == null)
                networkObject = GetComponent<NetworkObject>();

            if (actorIdentity == null)
                actorIdentity = GetComponent<ActorIdentityNet>();

            if (skillsNet == null)
                skillsNet = GetComponent<SkillsNet>();

            if (healthNet == null)
                healthNet = GetComponent<HealthNet>();
        }

        private void TryApplyServerDefaults()
        {
            if (serverDefaultsApplied)
                return;

            CacheRefs();

            if (actorDef == null)
            {
                if (!warnedMissingActorDef)
                {
                    warnedMissingActorDef = true;
                    Debug.LogWarning($"[Actors] ActorDefBinder on '{name}' has no ActorDef assigned.", this);
                }
                return;
            }

            // If this actor is networked, only proceed after spawn and only on server.
            if (networkObject != null)
            {
                if (!networkObject.IsSpawned)
                    return;

                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                    return;
            }
            else
            {
                // Non-network fallback path (mainly editor/test usage).
                if (actorIdentity != null && (!actorIdentity.IsSpawned || !actorIdentity.IsServer))
                    return;
            }

            ApplyIdentityDefaultsServer();
            ApplyStartingSkillsServer();
            healthNet?.ServerRecalculateMaxHealth();
            serverDefaultsApplied = true;
        }

        private void ApplyIdentityDefaultsServer()
        {
            if (actorIdentity == null || !actorIdentity.IsServer)
                return;

            bool identityUninitialized = actorIdentity.ActorId.Value.Length == 0;

            if (identityUninitialized && !string.IsNullOrWhiteSpace(actorDef.ActorId))
                actorIdentity.ActorId.Value = new FixedString64Bytes(actorDef.ActorId);

            // ActorDef is authoritative for social defaults during initial spawn setup.
            if (identityUninitialized || actorIdentity.FactionId.Value == 0)
                actorIdentity.FactionId.Value = actorDef.DefaultFactionId;

            // PvP defaults are applied only during initial identity setup.
            if (identityUninitialized)
                actorIdentity.ServerSetPvpEnabled(actorDef.DefaultPvpEnabled);
        }

        private void ApplyStartingSkillsServer()
        {
            if (skillsNet == null || !skillsNet.IsServer)
                return;

            skillsNet.ServerApplyStartingSkills(actorDef);
        }
    }
}
