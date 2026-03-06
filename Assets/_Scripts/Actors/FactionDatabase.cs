using System;
using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Runtime lookup catalog for faction definitions.
    ///
    /// This centralizes faction-id meaning so systems and content authors share
    /// one source for "what does id X represent?".
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Actors/FactionDatabase", fileName = "FactionDatabase")]
    public sealed class FactionDatabase : ScriptableObject
    {
        [SerializeField] private List<FactionDef> factions = new();

        private readonly Dictionary<int, FactionDef> byId = new();
        private readonly Dictionary<string, FactionDef> byKey = new(StringComparer.OrdinalIgnoreCase);
        private bool initialized;

        private void OnEnable()
        {
            initialized = false;
        }

        private void OnValidate()
        {
            initialized = false;
        }

        public bool TryGetById(int factionId, out FactionDef faction)
        {
            EnsureInitialized();
            return byId.TryGetValue(factionId, out faction);
        }

        public bool TryGetByKey(string key, out FactionDef faction)
        {
            EnsureInitialized();
            faction = null;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            return byKey.TryGetValue(key.Trim(), out faction);
        }

        public bool ContainsId(int factionId)
        {
            EnsureInitialized();
            return byId.ContainsKey(factionId);
        }

        public IReadOnlyList<FactionDef> Factions => factions;

        private void EnsureInitialized()
        {
            if (initialized)
                return;

            byId.Clear();
            byKey.Clear();

            for (int i = 0; i < factions.Count; i++)
            {
                FactionDef def = factions[i];
                if (def == null)
                    continue;

                if (!byId.ContainsKey(def.FactionId))
                    byId.Add(def.FactionId, def);
                else
                    Debug.LogWarning($"[Actors] Duplicate faction id {def.FactionId} in FactionDatabase '{name}'. Keeping first definition.", this);

                if (!string.IsNullOrWhiteSpace(def.Key))
                {
                    string key = def.Key.Trim();
                    if (!byKey.ContainsKey(key))
                        byKey.Add(key, def);
                    else
                        Debug.LogWarning($"[Actors] Duplicate faction key '{key}' in FactionDatabase '{name}'. Keeping first definition.", this);
                }
            }

            initialized = true;
        }
    }
}
