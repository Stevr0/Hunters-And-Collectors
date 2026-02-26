using System;
using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Runtime lookup database that maps ItemId to <see cref="ItemDef"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Items/Item Database", fileName = "ItemDatabase")]
    public sealed class ItemDatabase : ScriptableObject
    {
        [SerializeField] private List<ItemDef> itemDefs = new();
        private readonly Dictionary<string, ItemDef> byId = new(StringComparer.Ordinal);
        private bool initialized;

        private void OnEnable()
        {
            initialized = false;  // <-- critical
        }

        private void OnValidate()
        {
            initialized = false;  // <-- critical
        }
        /// <summary>
        /// Tries to fetch an item definition by stable id.
        /// </summary>
        public bool TryGet(string itemId, out ItemDef def)
        {
            EnsureInitialized();
            return byId.TryGetValue(itemId ?? string.Empty, out def);
        }

        /// <summary>
        /// Gets an item definition or throws when the id is unknown.
        /// </summary>
        public ItemDef GetOrThrow(string itemId)
        {
            if (!TryGet(itemId, out var def)) throw new KeyNotFoundException($"Unknown item id: {itemId}");
            return def;
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            byId.Clear();
            foreach (var def in itemDefs)
            {
                if (def == null || string.IsNullOrWhiteSpace(def.ItemId)) continue;
                byId[def.ItemId] = def;
                if (def.MaxStack < 1) def.MaxStack = 1;
            }
            initialized = true;
        }
    }
}