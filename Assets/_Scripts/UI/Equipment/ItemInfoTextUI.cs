using System.Text;
using HuntersAndCollectors.Items;
using TMPro;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// Shared tooltip/details text panel renderer.
    ///
    /// Reads ItemTooltipData payload from UI hover sources.
    /// Never writes gameplay state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemInfoTextUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Debug")]
        [SerializeField] private bool debugHover;

        private void Awake()
        {
            if (infoText == null)
                infoText = FindInfoTextReference();

            EnsureItemDatabase();
        }

        private void OnEnable()
        {
            ItemHoverBus.HoveredItemChanged += HandleHoveredItemChanged;
            ItemHoverBus.HoverCleared += HandleHoverCleared;
        }

        private void OnDisable()
        {
            ItemHoverBus.HoveredItemChanged -= HandleHoveredItemChanged;
            ItemHoverBus.HoverCleared -= HandleHoverCleared;
        }

        private void HandleHoveredItemChanged(ItemTooltipData tooltipData)
        {
            SetFromTooltipData(tooltipData);
        }

        private void HandleHoverCleared()
        {
            Clear();
        }

        public void SetFromTooltipData(ItemTooltipData tooltipData)
        {
            if (infoText == null)
                return;

            if (string.IsNullOrWhiteSpace(tooltipData.ItemId))
            {
                Clear();
                return;
            }

            // If source payload did not include ItemDef-resolved fields, resolve here.
            if (string.IsNullOrWhiteSpace(tooltipData.DisplayName))
                EnrichFromItemDef(ref tooltipData);

            if (debugHover)
                Debug.Log($"[ItemInfoTextUI] Tooltip set itemId='{tooltipData.ItemId}'");

            infoText.text = BuildInfoText(tooltipData);
        }

        public void Clear()
        {
            if (infoText != null)
                infoText.text = string.Empty;
        }

        private void EnrichFromItemDef(ref ItemTooltipData data)
        {
            EnsureItemDatabase();
            if (itemDatabase == null)
                return;

            if (!itemDatabase.TryGet(data.ItemId, out ItemDef def) || def == null)
                return;

            data.DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.ItemId : def.DisplayName;
            data.Description = def.Description;
            data.Damage = def.Damage;
            data.Defence = def.Defence;
            data.AttackBonus = def.AttackBonus;
            data.SwingSpeed = def.SwingSpeed;
            data.MoveSpeed = def.MovementSpeed;

            data.Strength = Mathf.Max(0, def.Strength) + data.BonusStrength;
            data.Dexterity = Mathf.Max(0, def.Dexterity) + data.BonusDexterity;
            data.Intelligence = Mathf.Max(0, def.Intelligence) + data.BonusIntelligence;
        }

        private void EnsureItemDatabase()
        {
            if (itemDatabase != null)
                return;

            itemDatabase = FindFirstObjectByType<ItemDatabase>();

            if (itemDatabase == null)
            {
                var all = Resources.FindObjectsOfTypeAll<ItemDatabase>();
                if (all != null && all.Length > 0)
                    itemDatabase = all[0];
            }
        }

        private TMP_Text FindInfoTextReference()
        {
            var localTexts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < localTexts.Length; i++)
            {
                var t = localTexts[i];
                if (t != null && string.Equals(t.name, "InfoText", System.StringComparison.Ordinal))
                    return t;
            }

            var allTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();
            for (int i = 0; i < allTexts.Length; i++)
            {
                var t = allTexts[i];
                if (t != null && string.Equals(t.name, "InfoText", System.StringComparison.Ordinal))
                    return t;
            }

            return GetComponentInChildren<TMP_Text>(true);
        }

        private static string BuildInfoText(ItemTooltipData data)
        {
            var sb = new StringBuilder(384);

            string title = string.IsNullOrWhiteSpace(data.DisplayName) ? data.ItemId : data.DisplayName;
            sb.AppendLine(title);

            if (!string.IsNullOrWhiteSpace(data.CraftedBy))
            {
                sb.Append("Crafted by: ").AppendLine(data.CraftedBy);
            }

            if (!string.IsNullOrWhiteSpace(data.Description))
            {
                sb.AppendLine();
                sb.AppendLine(data.Description.Trim());
            }

            sb.AppendLine();
            sb.AppendLine("Attributes:");
            AppendAttributeLine(sb, "Strength", data.Strength, data.BonusStrength);
            AppendAttributeLine(sb, "Dexterity", data.Dexterity, data.BonusDexterity);
            AppendAttributeLine(sb, "Intelligence", data.Intelligence, data.BonusIntelligence);


            sb.AppendLine();
            sb.AppendLine("Combat:");
            sb.Append("Damage: ").AppendLine(data.Damage.ToString("0.##"));
            sb.Append("Defence: ").AppendLine(data.Defence.ToString("0.##"));
            sb.Append("Attack Bonus: ").AppendLine(data.AttackBonus.ToString());
            sb.Append("Swing Speed: ").AppendLine(data.SwingSpeed.ToString("0.##"));
            sb.Append("Move Speed: ").AppendLine(data.MoveSpeed.ToString("0.##"));

            if (data.Durability > 0)
                sb.Append("Durability: ").Append(data.Durability.ToString());

            return sb.ToString().TrimEnd();
        }

        private static void AppendAttributeLine(StringBuilder sb, string label, int total, int bonus)
        {
            sb.Append(label).Append(": ").Append(total);
            if (bonus != 0)
                sb.Append(" (+").Append(bonus).Append(')');
            sb.AppendLine();
        }
    }
}


