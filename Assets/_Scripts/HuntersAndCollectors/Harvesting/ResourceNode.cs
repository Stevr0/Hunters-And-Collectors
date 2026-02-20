using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Scene resource node with harvest drop and cooldown state.
    /// </summary>
    public sealed class ResourceNode : MonoBehaviour
    {
        // Editor wiring checklist: place in scene, assign unique node id, drop item id, drop quantity, and respawn seconds.
        [SerializeField] private string nodeId = "NODE_001";
        [SerializeField] private string dropItemId = "IT_Wood";
        [SerializeField] private int dropQuantity = 1;
        [SerializeField] private float respawnSeconds = 30f;
        private float nextHarvestTime;

        public string NodeId => nodeId;
        public string DropItemId => dropItemId;
        public int DropQuantity => dropQuantity < 1 ? 1 : dropQuantity;

        /// <summary>
        /// Returns true when node is currently harvestable.
        /// </summary>
        public bool IsHarvestable() => Time.time >= nextHarvestTime;

        /// <summary>
        /// Consumes node and starts server-side cooldown timer.
        /// </summary>
        public void Consume()
        {
            nextHarvestTime = Time.time + (respawnSeconds < 0f ? 0f : respawnSeconds);
        }
    }
}
