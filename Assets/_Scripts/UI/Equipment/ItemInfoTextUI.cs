using System.Text;
using HuntersAndCollectors.Items;
using TMPro;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// Central controller for the shared item info text (tooltip/details panel).
    /// Listens to local UI hover events and renders resolved ItemDef info.
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

        private void HandleHoveredItemChanged(string itemId)
        {
            SetFromItemId(itemId);
        }

        private void HandleHoverCleared()
        {
            Clear();
        }

        public void SetFromItemId(string itemId)
        {
            if (infoText == null)
                return;

            if (string.IsNullOrWhiteSpace(itemId))
            {
                Clear();
                return;
            }

            EnsureItemDatabase();

            if (itemDatabase == null)
            {
                if (debugHover)
                    Debug.LogWarning($"[ItemInfoTextUI] Tooltip set failed. itemId='{itemId}' (ItemDatabase missing)");
                infoText.text = itemId;
                return;
            }

            if (!itemDatabase.TryGet(itemId, out ItemDef def) || def == null)
            {
                if (debugHover)
                    Debug.LogWarning($"[ItemInfoTextUI] Tooltip set failed. itemId='{itemId}' (not found)");
                infoText.text = itemId;
                return;
            }

            if (debugHover)
                Debug.Log($"[ItemInfoTextUI] Tooltip set success. itemId='{itemId}'");

            infoText.text = BuildInfoText(def);
        }

        public void Clear()
        {
            if (infoText != null)
                infoText.text = string.Empty;
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

            if (itemDatabase == null)
                Debug.LogError("[ItemInfoTextUI] ItemDatabase reference missing. Assign it in inspector.");
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

        private static string BuildInfoText(ItemDef def)
        {
            var lines = new StringBuilder(256);

            string title = string.IsNullOrWhiteSpace(def.DisplayName) ? def.ItemId : def.DisplayName;
            lines.AppendLine(title);

            if (!string.IsNullOrWhiteSpace(def.Description))
            {
                lines.AppendLine();
                lines.AppendLine(def.Description.Trim());
            }

            string properties = BuildGeneratedProperties(def);
            if (!string.IsNullOrWhiteSpace(properties))
            {
                lines.AppendLine();
                lines.AppendLine("Properties:");
                lines.AppendLine(properties);
            }

            return lines.ToString();
        }

        private static string BuildGeneratedProperties(ItemDef def)
        {
            var sb = new StringBuilder(256);

            void AddLine(string label, string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;

                if (sb.Length > 0)
                    sb.AppendLine();

                sb.Append(label).Append(": ").Append(value);
            }

            AddLine("Category", def.Category.ToString());
            AddLine("Max Stack", def.MaxStack.ToString());

            if (def.IsEquippable)
            {
                AddLine("Equip Slot", def.EquipSlot.ToString());
                if (def.Handedness != Handedness.None)
                    AddLine("Handedness", def.Handedness.ToString());
            }

            if (def.Damage > 0f)
                AddLine("Damage", def.Damage.ToString("0.##"));
            if (def.Defence > 0f)
                AddLine("Defence", def.Defence.ToString("0.##"));
            if (def.SwingSpeed > 0f)
                AddLine("Swing Speed", def.SwingSpeed.ToString("0.##"));

            if (def.ToolTags != null && def.ToolTags.Length > 0)
                AddLine("Tool Tags", string.Join(", ", def.ToolTags));

            if (!string.IsNullOrWhiteSpace(def.PropertiesText))
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine(def.PropertiesText.Trim());
            }

            return sb.ToString().TrimEnd();
        }
    }
}
