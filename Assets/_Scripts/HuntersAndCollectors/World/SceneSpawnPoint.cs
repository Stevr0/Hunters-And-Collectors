using UnityEngine;

namespace HuntersAndCollectors.World
{
    /// <summary>
    /// Authored marker used by area transfers to place a player in a destination scene.
    /// This object is scene-local and does not need to be networked.
    /// </summary>
    public sealed class SceneSpawnPoint : MonoBehaviour
    {
        [Tooltip("Stable id used by area transfers and saved scene restores. Examples: FromVillage_CaveEntrance, FromBeastCaverns_DeepGate.")]
        [SerializeField] private string spawnPointId = "Heartstone";

        public string SpawnPointId => spawnPointId;

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
            spawnPointId = string.IsNullOrWhiteSpace(spawnPointId) ? "Heartstone" : spawnPointId.Trim();
        }
#endif
    }
}
