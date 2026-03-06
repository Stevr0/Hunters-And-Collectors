using HuntersAndCollectors.Actors;
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

        private const int MaxSkillLevel = 100;
        private bool _startingSkillsApplied;

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
            EnsureSkill(SkillId.CombatAxe);
            EnsureSkill(SkillId.CombatPickaxe);
            EnsureSkill(SkillId.CombatKnife);
            EnsureSkill(SkillId.CombatClub);
            EnsureSkill(SkillId.CombatUnarmed);
        }

        /// <summary>
        /// SERVER ONLY: applies ActorDef starting skills one time for this actor instance.
        /// Existing levels are replaced by the authored ActorDef baseline and XP is reset to 0.
        /// </summary>
        public void ServerApplyStartingSkills(ActorDef def)
        {
            if (!IsServer)
                return;

            if (_startingSkillsApplied)
                return;

            if (def == null || def.StartingSkills == null || def.StartingSkills.Length == 0)
            {
                _startingSkillsApplied = true;
                return;
            }

            for (int i = 0; i < def.StartingSkills.Length; i++)
            {
                ActorDef.StartingSkill authored = def.StartingSkills[i];
                if (string.IsNullOrWhiteSpace(authored.SkillId))
                    continue;

                int clampedLevel = Mathf.Clamp(authored.Level, 0, MaxSkillLevel);
                SetSkillLevel(authored.SkillId, clampedLevel);
            }

            _startingSkillsApplied = true;
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

        private void SetSkillLevel(string id, int level)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            FixedString64Bytes key = new(id);
            int clampedLevel = Mathf.Clamp(level, 0, MaxSkillLevel);

            for (int i = 0; i < skills.Count; i++)
            {
                if (!skills[i].Id.Equals(key))
                    continue;

                SkillEntry entry = skills[i];
                entry.Level = clampedLevel;
                entry.Xp = 0;
                skills[i] = entry;
                return;
            }

            skills.Add(new SkillEntry
            {
                Id = key,
                Level = clampedLevel,
                Xp = 0
            });
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

