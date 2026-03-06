using System;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Single source of truth baseline for an actor's authored identity, social defaults,
    /// base attributes, base combat/movement stats, and starting skills.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Actors/ActorDef")]
    public sealed class ActorDef : ScriptableObject
    {
        [Serializable]
        public struct StartingSkill
        {
            public string SkillId;

            [Range(0, 100)]
            public int Level;
        }

        [Header("Identity")]
        public string ActorId = string.Empty;
        public string DisplayName = string.Empty;

        [Header("Social Defaults")]
        public int DefaultFactionId = 0;
        public bool DefaultPvpEnabled = false;

        [Header("Base Attributes")]
        public int BaseStrength = 10;
        public int BaseDexterity = 10;
        public int BaseIntelligence = 10;

        [Header("Base Combat / Movement")]
        public float BaseMoveSpeedMult = 1f;
        public float BaseDamage = 0f;
        public float BaseDefence = 0f;
        public float BaseSwingSpeed = 1f;

        [Header("AI (Optional)")]
        public ActorAIDef AiDef;

        [Header("Starting Skills")]
        public StartingSkill[] StartingSkills;

#if UNITY_EDITOR
        private void OnValidate()
        {
            BaseStrength = Mathf.Max(0, BaseStrength);
            BaseDexterity = Mathf.Max(0, BaseDexterity);
            BaseIntelligence = Mathf.Max(0, BaseIntelligence);

            BaseMoveSpeedMult = Mathf.Max(0.0001f, BaseMoveSpeedMult);
            BaseSwingSpeed = Mathf.Max(0.0001f, BaseSwingSpeed);
            BaseDamage = Mathf.Max(0f, BaseDamage);
            BaseDefence = Mathf.Max(0f, BaseDefence);

            if (StartingSkills == null)
                return;

            for (int i = 0; i < StartingSkills.Length; i++)
            {
                StartingSkill skill = StartingSkills[i];
                skill.Level = Mathf.Clamp(skill.Level, 0, 100);
                StartingSkills[i] = skill;
            }
        }
#endif
    }
}



