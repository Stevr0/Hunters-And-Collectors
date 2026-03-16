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
            data.ItemTier = def.ItemTier;
            data.CombatFamily = def.CombatFamily;
            data.ItemStatBias = def.ItemStatBias;
            data.Damage = data.RolledDamage > 0f ? data.RolledDamage + data.DamageBonus : def.Damage + data.DamageBonus;
            data.Defence = data.RolledDefence > 0f ? data.RolledDefence + data.DefenceBonus : def.Defence + data.DefenceBonus;
            data.AttackBonus = def.AttackBonus;
            data.SwingSpeed = data.RolledSwingSpeed > 0f ? data.RolledSwingSpeed + data.AttackSpeedBonus : def.SwingSpeed + data.AttackSpeedBonus;
            data.MoveSpeed = data.RolledMovementSpeed > 0f ? data.RolledMovementSpeed : def.MovementSpeed;
            data.CastSpeed = data.RolledCastSpeed > 0f ? data.RolledCastSpeed + data.CastSpeedBonus : def.CastSpeed + data.CastSpeedBonus;
            data.BlockValue = data.RolledBlockValue > 0 ? data.RolledBlockValue + data.BlockValueBonus : def.BlockValue + data.BlockValueBonus;
            data.CritChance = data.CritChanceBonus;
            data.StatusPower = data.StatusPowerBonus;
            data.TrapPower = data.TrapPowerBonus;
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
            var sb = new StringBuilder(640);

            string title = string.IsNullOrWhiteSpace(data.DisplayName) ? data.ItemId : data.DisplayName;
            sb.AppendLine(title);

            if (data.ItemTier > 0)
                sb.Append("Tier ").Append(data.ItemTier).AppendLine();

            if (data.CombatFamily != CombatItemFamily.None || data.ItemStatBias != ItemStatBias.None)
                sb.Append(data.CombatFamily).Append(" / ").Append(data.ItemStatBias).AppendLine();

            if (!string.IsNullOrWhiteSpace(data.CraftedBy))
                sb.Append("Crafted by: ").AppendLine(data.CraftedBy);

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
            if (data.CastSpeed > 0f)
                sb.Append("Cast Speed: ").AppendLine(data.CastSpeed.ToString("0.##"));
            if (data.BlockValue > 0)
                sb.Append("Block Value: ").AppendLine(data.BlockValue.ToString());
            if (data.CritChance > 0f)
                sb.Append("Crit Chance: ").AppendLine((data.CritChance * 100f).ToString("0.#") + "%");
            if (data.StatusPower > 0)
                sb.Append("Status Power: ").AppendLine(data.StatusPower.ToString());
            if (data.TrapPower > 0)
                sb.Append("Trap Power: ").AppendLine(data.TrapPower.ToString());

            AppendAffixLine(sb, data.AffixA, data);
            AppendAffixLine(sb, data.AffixB, data);
            AppendAffixLine(sb, data.AffixC, data);
            AppendResistanceLine(sb, data);

            if (data.Durability > 0)
            {
                if (data.MaxDurability > 0)
                    sb.Append("Durability: ").Append(data.Durability.ToString()).Append("/").AppendLine(data.MaxDurability.ToString());
                else
                    sb.Append("Durability: ").AppendLine(data.Durability.ToString());
            }

            return sb.ToString().TrimEnd();
        }

        private static void AppendAttributeLine(StringBuilder sb, string label, int total, int bonus)
        {
            sb.Append(label).Append(": ").Append(total);
            if (bonus != 0)
                sb.Append(" (+").Append(bonus).Append(')');
            sb.AppendLine();
        }

        private static void AppendAffixLine(StringBuilder sb, ItemAffixId affixId, ItemTooltipData data)
        {
            if (affixId == ItemAffixId.None)
                return;

            string label = CombatItemCatalog.GetAffixDisplayName(affixId);
            if (string.IsNullOrWhiteSpace(label))
                return;

            string value = affixId switch
            {
                ItemAffixId.Strong => $"+{data.BonusStrength} Strength",
                ItemAffixId.Keen => $"+{data.BonusDexterity} Dexterity",
                ItemAffixId.Wise => $"+{data.BonusIntelligence} Intelligence",
                ItemAffixId.Brutal => $"+{data.DamageBonus} Damage",
                ItemAffixId.Guarded => $"+{data.DefenceBonus} Defence",
                ItemAffixId.Swift => $"+{(data.AttackSpeedBonus * 100f):0.#}% Attack Speed",
                ItemAffixId.Focused => $"+{(data.CastSpeedBonus * 100f):0.#}% Cast Speed",
                ItemAffixId.Warding => $"+{data.BlockValueBonus} Block Value",
                ItemAffixId.Venomous => $"+{data.StatusPowerBonus} Status Power",
                ItemAffixId.Trapper => $"+{data.TrapPowerBonus} Trap Power",
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(value))
                sb.Append(label).Append(": ").AppendLine(value);
        }

        private static void AppendResistanceLine(StringBuilder sb, ItemTooltipData data)
        {
            if (data.ResistanceAffix == ResistanceAffixId.None)
                return;

            string label = CombatItemCatalog.GetResistanceDisplayName(data.ResistanceAffix);
            int value = data.ResistanceAffix switch
            {
                ResistanceAffixId.Ironward => data.PhysicalResist,
                ResistanceAffixId.Emberward => data.FireResist,
                ResistanceAffixId.Frostward => data.FrostResist,
                ResistanceAffixId.Venomward => data.PoisonResist,
                ResistanceAffixId.Stormward => data.LightningResist,
                _ => 0
            };

            if (value > 0)
                sb.Append(label).Append(": +").Append(value).AppendLine(" Resist");
        }
    }
}
