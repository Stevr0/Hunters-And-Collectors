using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Small server-side harness for quickly validating ActorSpawner behavior in host mode.
    ///
    /// Intended for development/testing only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorSpawnTester : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ActorSpawner actorSpawner;

        [Header("Defs")]
        [SerializeField] private ActorDef dummyDef;
        [SerializeField] private ActorDef npcDef;

        [Header("Startup Spawn")]
        [SerializeField] private bool spawnDummyOnServerStart;
        [SerializeField] private bool spawnNpcOnServerStart;
        [SerializeField] private string dummySpawnPointId = string.Empty;
        [SerializeField] private string npcSpawnPointId = string.Empty;

        private bool _spawned;
        private bool _warnedMissingSpawner;

        private void Start()
        {
            TrySpawnForServerStart();
        }

        private void Update()
        {
            if (!_spawned)
                TrySpawnForServerStart();
        }

        private void TrySpawnForServerStart()
        {
            if (_spawned)
                return;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer)
                return;

            if (actorSpawner == null)
                actorSpawner = FindFirstObjectByType<ActorSpawner>();

            if (actorSpawner == null)
            {
                if (!_warnedMissingSpawner)
                {
                    _warnedMissingSpawner = true;
                    Debug.LogWarning("[ActorSpawnTester] Missing ActorSpawner reference. Waiting for one to appear.", this);
                }

                return;
            }

            _warnedMissingSpawner = false;

            if (spawnDummyOnServerStart)
            {
                NetworkObject dummy = actorSpawner.ServerSpawnDummyActor(dummyDef, dummySpawnPointId);
                if (dummy == null)
                    Debug.LogWarning("[ActorSpawnTester] Dummy spawn request returned null.", this);
            }

            if (spawnNpcOnServerStart)
            {
                NetworkObject npc = actorSpawner.ServerSpawnNpcActor(npcDef, npcSpawnPointId);
                if (npc == null)
                    Debug.LogWarning("[ActorSpawnTester] NPC spawn request returned null.", this);
            }

            _spawned = true;
        }
    }
}
