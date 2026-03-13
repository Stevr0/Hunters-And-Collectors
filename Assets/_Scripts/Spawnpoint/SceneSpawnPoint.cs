using HuntersAndCollectors.World;
using UnityEngine;

namespace HuntersAndCollectors.Bootstrap
{
    /// <summary>
    /// Unified scene spawn marker used by both bootstrap player spawning and area-transfer destinations.
    ///
    /// Why this exists:
    /// - The project had two different SceneSpawnPoint scripts in different namespaces.
    /// - That was confusing in Unity authoring because both looked like valid spawn markers.
    /// - We now keep a single scene component and register it with the shared spawn registry so both
    ///   bootstrap spawns and area transfers resolve the same authored marker type.
    ///
    /// Authoring notes:
    /// - This object is scene-local and not networked.
    /// - Place it anywhere the server may need to position a player.
    /// - Use stable ids such as Heartstone or FromVillage_CaveEntrance.
    /// </summary>
    public sealed class SceneSpawnPoint : MonoBehaviour
    {
        [Tooltip("Stable spawn id used by bootstrap player spawns and area transfers.")]
        [SerializeField] private string spawnId = "Heartstone";

        public string SpawnId => spawnId;

        // Alias kept so newer transfer code reads naturally without introducing a second component type.
        public string SpawnPointId => spawnId;

        private void OnEnable()
        {
            SceneSpawnRegistry.Register(this);
        }

        private void OnDisable()
        {
            SceneSpawnRegistry.Unregister(this);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            spawnId = string.IsNullOrWhiteSpace(spawnId) ? "Heartstone" : spawnId.Trim();
        }
#endif
    }
}
