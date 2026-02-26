using UnityEngine;
using System.Text;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// ItemDef
    /// -------------------------------------------------------
    /// Static item type definition (ScriptableObject).
    ///
    /// IMPORTANT (learning note):
    /// - This is STATIC data: “what this item type is”.
    /// - Runtime-changing data (current durability, rolled quality) should eventually live in
    ///   a runtime “ItemInstance” structure (future version), NOT in this ScriptableObject.
    ///
    /// For now, we keep *base* and *limits* here so existing UI/equipment can work.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Items/Item Definition", fileName = "ItemDef")]
    public sealed class ItemDef : ScriptableObject
    {
        // =========================================================
        // IDENTITY (core)
        // =========================================================

        [Header("Identity")]
        [Tooltip("Stable unique id (eg: IT_Wood, IT_StoneAxe). Must never change.")]
        public string ItemId;

        [Tooltip("Name shown to players.")]
        public string DisplayName;

        [Tooltip("UI icon.")]
        public Sprite Icon;

        [Tooltip("Max quantity per stack. Tools/weapons typically 1.")]
        [Min(1)]
        public int MaxStack = 1;

        public ItemCategory Category;

        // =========================================================
        // TEXT / UI (these were referenced by CraftingWindowUI)
        // =========================================================

        [Header("UI Text")]
        [TextArea(2, 6)]
        [Tooltip("Short description shown in UI.")]
        public string Description;

        [TextArea(2, 10)]
        [Tooltip("Extra lines shown in UI (eg: 'Damage: 12\\nSpeed: 1.2').")]
        public string PropertiesText;

        // =========================================================
        // ECONOMY / CRAFTING
        // =========================================================

        [Header("Economy")]
        [Tooltip("Default base value before player-defined pricing.")]
        [Min(0)]
        public int BaseValue = 1;

        [Tooltip("Weight of a single item unit.")]
        [Min(0f)]
        public float Weight = 0f;

        [Tooltip("Rarity tier for UI + future loot systems.")]
        public ItemRarity Rarity = ItemRarity.Common;

        [Header("Crafting")]
        [Tooltip("Base craft time in seconds (before skill modifiers).")]
        [Min(0f)]
        public float CraftTime = 1f;

        // =========================================================
        // QUALITY / DURABILITY (base + limits)
        // =========================================================

        [Header("Quality / Durability")]
        [Tooltip("Base quality (1.0 = normal). Future: crafting may roll this per instance.")]
        [Min(0f)]
        public float BaseQuality = 1f;

        [Tooltip("Max durability limit for this item type. 0 = indestructible.")]
        [Min(0)]
        public int MaxDurability = 0;

        // =========================================================
        // COMBAT / MOVEMENT STATS (base values)
        // =========================================================

        [Header("Combat")]
        [Tooltip("Base damage (weapons/tools).")]
        [Min(0f)]
        public float Damage = 0f;

        [Tooltip("Base defence (armor/shields).")]
        [Min(0f)]
        public float Defence = 0f;

        [Tooltip("Attacks per second (or swings per second).")]
        [Min(0.01f)]
        public float SwingSpeed = 1f;

        [Header("Movement")]
        [Tooltip("Movement speed multiplier. 1 = normal speed, 0.9 = -10% speed.")]
        [Min(0f)]
        public float MovementSpeed = 1f;

        // =========================================================
        // EQUIPMENT (these were referenced by PlayerEquipmentNet)
        // =========================================================

        [Header("Equipment")]
        [Tooltip("If true, this item can be equipped into an equipment slot.")]
        public bool IsEquippable = false;

        [Tooltip("Which slot this item equips into.")]
        public EquipSlot EquipSlot = EquipSlot.None;

        [Tooltip("Hand usage rule for weapons/tools.")]
        public Handedness Handedness = Handedness.None;

        [Tooltip("Tags used for tool checks (eg, Axe for Lumberjacking).")]
        public ToolTag[] ToolTags;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Keep basic values safe so you don’t create broken defs in the Inspector.
            if (MaxStack < 1) MaxStack = 1;
            if (SwingSpeed <= 0f) SwingSpeed = 0.01f;
            if (MovementSpeed < 0f) MovementSpeed = 0f;
            if (BaseValue < 0) BaseValue = 0;
            if (BaseQuality < 0f) BaseQuality = 0f;
            if (MaxDurability < 0) MaxDurability = 0;

            // Optional sanity: if it’s not equippable, force slot/handedness to “None”
            if (!IsEquippable)
            {
                EquipSlot = EquipSlot.None;
                Handedness = Handedness.None;
            }
        }

        /// <summary>
        /// Builds a UI-friendly "Properties" block from this item's stat fields.
        /// This is generated at runtime so you don't have to maintain text manually.
        /// 
        /// You can still keep manual notes in PropertiesText and choose whether to append them.
        /// </summary>
        public string BuildPropertiesText(bool appendManualPropertiesText = true)
        {
            // StringBuilder is efficient for building multi-line text.
            var sb = new StringBuilder(256);

            // Helper local function to add a line only when value is meaningful.
            // Keeps your UI clean (no "Damage: 0" spam).
            void AddLine(string label, string value)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(label).Append(": ").Append(value);
            }

            // ------------------------------------------------------------
            // ECONOMY / META
            // ------------------------------------------------------------
            if (BaseValue > 0) AddLine("Base Value", BaseValue.ToString());
            if (Weight > 0f) AddLine("Weight", Weight.ToString("0.##"));
            AddLine("Rarity", Rarity.ToString());

            // ------------------------------------------------------------
            // CRAFTING
            // ------------------------------------------------------------
            if (CraftTime > 0f) AddLine("Craft Time", $"{CraftTime:0.##}s");

            // ------------------------------------------------------------
            // QUALITY / DURABILITY (base/limits only)
            // ------------------------------------------------------------
            if (BaseQuality != 1f) AddLine("Quality", BaseQuality.ToString("0.##"));
            if (MaxDurability > 0) AddLine("Durability", MaxDurability.ToString()); // future: show current/max on instances

            // ------------------------------------------------------------
            // COMBAT / MOVEMENT
            // ------------------------------------------------------------
            if (Damage > 0f) AddLine("Damage", Damage.ToString("0.##"));
            if (Defence > 0f) AddLine("Defence", Defence.ToString("0.##"));

            // SwingSpeed: if you always want it shown, keep it unconditional.
            // If you only want it shown when not default, change to (SwingSpeed != 1f).
            if (SwingSpeed > 0f) AddLine("Swing Speed", SwingSpeed.ToString("0.##"));

            if (MovementSpeed != 1f) AddLine("Move Speed", $"{MovementSpeed:0.##}x");

            // ------------------------------------------------------------
            // EQUIPMENT (only show if equippable)
            // ------------------------------------------------------------
            if (IsEquippable)
            {
                AddLine("Equip Slot", EquipSlot.ToString());

                // Only show handedness if it’s meaningful.
                if (Handedness != Handedness.None)
                    AddLine("Handedness", Handedness.ToString());
            }

            // ------------------------------------------------------------
            // TOOL TAGS
            // ------------------------------------------------------------
            if (ToolTags != null && ToolTags.Length > 0)
            {
                // Join tags like: Axe, Pickaxe
                AddLine("Tool Tags", string.Join(", ", ToolTags));
            }

            // ------------------------------------------------------------
            // OPTIONAL: Append manual custom text at the end
            // ------------------------------------------------------------
            if (appendManualPropertiesText && !string.IsNullOrWhiteSpace(PropertiesText))
            {
                if (sb.Length > 0)
                    sb.AppendLine().AppendLine(); // blank line between generated + manual
                sb.Append(PropertiesText.Trim());
            }

            return sb.ToString();
        }
#endif
    }
}