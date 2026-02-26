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
        // Replicated skill list
        // Constructor overload in your NGO version is:
        // NetworkList(IEnumerable<T> initialValues, NetworkVariableReadPermission readPerm, NetworkVariableWritePermission writePerm)
        private readonly NetworkList<SkillEntry> skills =
            new(null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// Public read-only access for UI.
        /// </summary>
        public NetworkList<SkillEntry> Skills => skills;

        // Add near top of SkillsNet class
        private const int MaxSkillLevel = 100;

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            EnsureSkill(SkillId.Sales);
            EnsureSkill(SkillId.Negotiation);
            EnsureSkill(SkillId.Running);
            EnsureSkill(SkillId.Woodcutting);
            EnsureSkill(SkillId.Mining);
            EnsureSkill(SkillId.Foraging);
            EnsureSkill(SkillId.ToolCrafting);
            EnsureSkill(SkillId.EquipmentCrafting);
            EnsureSkill(SkillId.BuildingCrafting);
        }

        public SkillEntry Get(string id)
        {
            var key = new FixedString64Bytes(id);

            foreach (var s in skills)
                if (s.Id.Equals(key))
                    return s;

            return new SkillEntry { Id = key, Level = 0, Xp = 0 };
        }

        /// <summary>
        /// Returns the current level for a skill id. Ensures the skill entry exists on the server.
        /// </summary>
        public int GetLevel(string id)
        {
            var entry = Get(id);

            if (IsServer && entry.Id.Length > 0)
                EnsureSkill(id);

            return entry.Level;
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
                while (entry.Level < MaxSkillLevel && entry.Xp >= 10 * (entry.Level + 1))
                {
                    entry.Xp -= 10 * (entry.Level + 1);
                    entry.Level++;
                }

                // If we hit max level, you can choose what to do with XP.
                // MVP: clamp XP to 0 at cap so UI is clean.
                if (entry.Level >= MaxSkillLevel)
                {
                    entry.Level = MaxSkillLevel;
                    entry.Xp = 0;
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