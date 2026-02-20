using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// Server-side registry mapping stable node ids to scene node references.
    /// 
    /// IMPORTANT:
    /// - Only the server should rely on this registry for harvest validation.
    /// - NodeId must be unique across scene.
    /// </summary>
    public sealed class ResourceNodeRegistry : MonoBehaviour
    {
        private readonly Dictionary<string, ResourceNode> byId = new();

        private void Awake()
        {
            byId.Clear();

            var nodes = FindObjectsOfType<ResourceNode>(true);

            foreach (var node in nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.NodeId))
                {
                    Debug.LogWarning("[ResourceNodeRegistry] Node with missing or empty NodeId detected.");
                    continue;
                }

                if (byId.ContainsKey(node.NodeId))
                {
                    Debug.LogError($"[ResourceNodeRegistry] Duplicate NodeId detected: {node.NodeId}");
                    continue;
                }

                byId[node.NodeId] = node;
            }

#if UNITY_EDITOR
            Debug.Log($"[ResourceNodeRegistry] Registered {byId.Count} nodes.");
#endif
        }

        /// <summary>
        /// Attempts to resolve a resource node by stable node id.
        /// Server-only usage recommended.
        /// </summary>
        public bool TryGet(string nodeId, out ResourceNode node)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                node = null;
                return false;
            }

            return byId.TryGetValue(nodeId, out node);
        }
    }
}
