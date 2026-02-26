using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// SkillRowUI
    /// ---------------------------------------------------------
    /// Visual row for ONE skill.
    /// This is "dumb UI": it does not know networking.
    /// It just displays values that SkillsWindowUI passes in.
    /// </summary>
    public sealed class SkillRowUI : MonoBehaviour
    {
        [Header("UI Refs")]
        [SerializeField] private TMP_Text skillNameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text xpText;

        [Tooltip("Optional XP bar. If you don't use a bar, leave null.")]
        [SerializeField] private Slider xpBar;

        /// <summary>
        /// Populate the row with the latest data.
        /// </summary>
        public void Set(string skillName, int level, int xp, int xpToNextLevel)
        {
            if (skillNameText) skillNameText.text = skillName;
            if (levelText) levelText.text = $"Lv {level}";

            // e.g. "7 / 130"
            if (xpText) xpText.text = $"{xp} / {xpToNextLevel}";

            // Normalized 0..1 for the bar
            if (xpBar)
            {
                xpBar.minValue = 0f;
                xpBar.maxValue = 1f;
                xpBar.value = (xpToNextLevel <= 0) ? 0f : (xp / (float)xpToNextLevel);
            }
        }
    }
}