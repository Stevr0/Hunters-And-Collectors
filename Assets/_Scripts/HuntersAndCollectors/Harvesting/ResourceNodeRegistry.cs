using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Harvesting
{
    /// <summary>
    /// ResourceNodeRegistry
    /// --------------------------------------------------------------------
    /// Scene registry mapping stable node ids to ResourceNodeNet references.
    ///
    /// Intended usage:
    /// - Server-side validation / lookup (optional convenience).
    ///
    /// Notes:
    /// - NodeId must be unique across the scene.
    /// - This registry does NOT need to be networked.
    /// - Safe to keep enabled on clients, but only server should rely on it
    ///   for authoritative decisions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResourceNodeRegistry : MonoBehaviour
    {
        private readonly Dictionary<string, ResourceNodeNet> byId = new();

        private void Awake()
        {
            Rebuild();
        }

        /// <summary>
        /// Rebuilds the registry by scanning the scene for ResourceNodeNet.
        /// Useful if you load additively and want to rebuild after scene load.
        /// </summary>
        [ContextMenu("Rebuild Registry")]
        public void Rebuild()
        {
            byId.Clear();

            // includeInactive=true so disabled nodes still register.
            var nodes = FindObjectsOfType<ResourceNodeNet>(true);

            foreach (var node in nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.NodeId))
                {
                    Debug.LogWarning("[ResourceNodeRegistry] Node with missing or empty NodeId detected.", node);
                    continue;
                }

                if (byId.ContainsKey(node.NodeId))
                {
                    Debug.LogError($"[ResourceNodeRegistry] Duplicate NodeId detected: {node.NodeId}", node);
                    continue;
                }

                byId[node.NodeId] = node;
            }

#if UNITY_EDITOR
            Debug.Log($"[ResourceNodeRegistry] Registered {byId.Count} nodes.", this);
#endif
        }

        /// <summary>
        /// Attempts to resolve a resource node by stable node id.
        /// </summary>
        public bool TryGet(string nodeId, out ResourceNodeNet node)
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
