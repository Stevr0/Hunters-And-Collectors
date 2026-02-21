using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Skills
{
    /// <summary>
    /// Server-authoritative replicated skill container.
    /// </summary>
    public sealed class SkillsNet : NetworkBehaviour
    {
        // IMPORTANT:
        // Give explicit permissions:
        // - Everyone can READ (clients need to display skills in UI)
        // - Only Server can WRITE (server-authoritative)
        private readonly NetworkList<SkillEntry> skills =
            new(NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// Public read-only access for UI.
        /// NOTE: Clients MUST NOT modify this list (and can't, due to Server write permission).
        /// </summary>
        public NetworkList<SkillEntry> Skills => skills;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            EnsureSkill(SkillId.Sales);
            EnsureSkill(SkillId.Negotiation);
        }

        public SkillEntry Get(string id)
        {
            var key = new FixedString64Bytes(id);

            foreach (var s in skills)
                if (s.Id.Equals(key))
                    return s;

            return new SkillEntry { Id = key, Level = 0, Xp = 0 };
        }

        public void AddXp(string id, int amount)
        {
            if (!IsServer || amount <= 0)
                return;

            var key = new FixedString64Bytes(id);

            for (int i = 0; i < skills.Count; i++)
            {
                if (!skills[i].Id.Equals(key))
                    continue;

                var entry = skills[i];
                entry.Xp += amount;

                // XP curve: 10 * (level + 1)
                while (entry.Xp >= 10 * (entry.Level + 1))
                {
                    entry.Xp -= 10 * (entry.Level + 1);
                    entry.Level++;
                }

                skills[i] = entry; // triggers replication + OnListChanged on clients
                return;
            }

            // If skill didn't exist yet, add it then retry
            skills.Add(new SkillEntry { Id = key, Level = 0, Xp = 0 });
            AddXp(id, amount);
        }

        private void EnsureSkill(string id)
        {
            var key = new FixedString64Bytes(id);

            foreach (var s in skills)
                if (s.Id.Equals(key))
                    return;

            skills.Add(new SkillEntry { Id = key, Level = 0, Xp = 0 });
        }
    }
}
