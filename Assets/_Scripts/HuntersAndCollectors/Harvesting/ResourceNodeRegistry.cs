using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Runtime registry mapping stable node ids to scene node references.
    /// </summary>
    public sealed class ResourceNodeRegistry : MonoBehaviour
    {
        // Editor wiring checklist: add to scene with all ResourceNode children loaded in active scene.
        private readonly Dictionary<string, ResourceNode> byId = new();

        private void Awake()
        {
            byId.Clear();
            foreach (var node in FindObjectsOfType<ResourceNode>()) byId[node.NodeId] = node;
        }

        /// <summary>
        /// Attempts to resolve a resource node by stable node id.
        /// </summary>
        public bool TryGet(string nodeId, out ResourceNode node) => byId.TryGetValue(nodeId ?? string.Empty, out node);
    }
}
