using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Skills
{
    /// <summary>
    /// Holds server-authoritative skills and applies MVP xp/level-up logic.
    /// </summary>
    public sealed class SkillsNet : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab with PlayerNetworkRoot.
        private readonly Dictionary<string, SkillEntry> skills = new();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            EnsureSkill(SkillId.Sales);
            EnsureSkill(SkillId.Negotiation);
        }

        /// <summary>
        /// Gets a skill entry by id or a default row when missing.
        /// </summary>
        public SkillEntry Get(string id) => skills.TryGetValue(id, out var e) ? e : new SkillEntry { Id = id, Level = 0, Xp = 0 };

        /// <summary>
        /// Adds xp to a skill and performs deterministic level-up checks.
        /// </summary>
        public void AddXp(string id, int amount)
        {
            if (!IsServer || amount <= 0) return;
            var entry = Get(id);
            entry.Id = id;
            entry.Xp += amount;
            while (entry.Xp >= 10 * (entry.Level + 1))
            {
                entry.Xp -= 10 * (entry.Level + 1);
                entry.Level++;
            }
            skills[id] = entry;
        }

        private void EnsureSkill(string id) { if (!skills.ContainsKey(id)) skills[id] = new SkillEntry { Id = id, Level = 0, Xp = 0 }; }
    }
}
