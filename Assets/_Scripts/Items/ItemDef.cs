using System.Text;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// ItemDef
    /// -------------------------------------------------------
    /// Static item type definition (ScriptableObject).
    ///
    /// IMPORTANT:
    /// - This is STATIC data: “what this item type is”.
    /// - Runtime-changing data (durability remaining, rolled stats, quality, etc.)
    ///   should eventually live in a runtime “ItemInstance” (future version),
    ///   NOT in this ScriptableObject.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Items/Item Definition", fileName = "ItemDef")]
    public sealed class ItemDef : ScriptableObject
    {
        // =========================================================
        // IDENTITY (core)
        // =========================================================

        [Header("Identity")]
        [Tooltip("Stable unique id (eg: IT_Wood, IT_StoneAxe). Must never change once released.")]
        public string ItemId;

        [Tooltip("Name shown to players.")]
        public string DisplayName;

        [Tooltip("UI icon.")]
        public Sprite Icon;

        [Tooltip("Max quantity per stack. Tools/weapons typically 1.")]
        [Min(1)]
        public int MaxStack = 1;

        [Tooltip("High-level category for UI + rules (Resource/Tool/Crafted/etc).")]
        public ItemCategory Category;

        // =========================================================
        // EQUIP VISUAL (hand alignment)
        // =========================================================
        // This is used by PlayerEquipmentVisual to spawn an item model in the
        // player's hand when equipped. This prefab is VISUAL ONLY (NO NetworkObject).
        // Alignment is data-driven per item (local offsets saved in this asset).

        [Header("Equip Visual")]
        [Tooltip("Optional prefab to spawn when equipped (visual-only; should NOT have NetworkObject).")]
        [SerializeField] private GameObject visualPrefab;

        [Tooltip("Local position offset applied after parenting to the hand anchor.")]
        [SerializeField] private Vector3 equipLocalPosition;

        [Tooltip("Local rotation offset (Euler degrees) applied after parenting to the hand anchor.")]
        [SerializeField] private Vector3 equipLocalEuler;

        [Tooltip("Local scale applied after parenting. Usually (1,1,1).")]
        [SerializeField] private Vector3 equipLocalScale = Vector3.one;

        /// <summary>Prefab to spawn when equipped. Can be null.</summary>
        public GameObject VisualPrefab => visualPrefab;

        /// <summary>Per-item hand alignment offset (local position).</summary>
        public Vector3 EquipLocalPosition => equipLocalPosition;

        /// <summary>Per-item hand alignment offset (local rotation euler).</summary>
        public Vector3 EquipLocalEuler => equipLocalEuler;

        /// <summary>Per-item hand alignment (local scale).</summary>
        public Vector3 EquipLocalScale => equipLocalScale;

        // =========================================================
        // TEXT / UI
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
        // EQUIPMENT (used by PlayerEquipmentNet)
        // =========================================================

        [Header("Equipment")]
        [Tooltip("If true, this item can be equipped into an equipment slot.")]
        public bool IsEquippable = false;

        [Tooltip("Which slot this item equips into.")]
        public EquipSlot EquipSlot = EquipSlot.None;

        [Tooltip("Hand usage rule for weapons/tools.")]
        public Handedness Handedness = Handedness.None;

        [Tooltip("Tags used for tool checks (eg, Axe for Woodcutting).")]
        public ToolTag[] ToolTags;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // ---------------------------------------------------------
            // Basic numeric safety
            // ---------------------------------------------------------
            if (MaxStack < 1) MaxStack = 1;
            if (SwingSpeed <= 0f) SwingSpeed = 0.01f;
            if (MovementSpeed < 0f) MovementSpeed = 0f;
            if (BaseValue < 0) BaseValue = 0;
            if (BaseQuality < 0f) BaseQuality = 0f;
            if (MaxDurability < 0) MaxDurability = 0;

            // Ensure we don't keep invalid equip configuration when not equippable
            if (!IsEquippable)
            {
                EquipSlot = EquipSlot.None;
                Handedness = Handedness.None;
            }

            // Optional: keep equip scale sensible (avoid accidental zero scale)
            if (equipLocalScale.x == 0f && equipLocalScale.y == 0f && equipLocalScale.z == 0f)
                equipLocalScale = Vector3.one;
        }

        /// <summary>
        /// Builds a UI-friendly "Properties" block from this item's stat fields.
        /// Generated at runtime so you don't have to maintain text manually.
        /// </summary>
        public string BuildPropertiesText(bool appendManualPropertiesText = true)
        {
            var sb = new StringBuilder(256);

            // Adds "Label: Value" lines, with automatic newline handling.
            void AddLine(string label, string value)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(label).Append(": ").Append(value);
            }

            // ECONOMY / META
            if (BaseValue > 0) AddLine("Base Value", BaseValue.ToString());
            if (Weight > 0f) AddLine("Weight", Weight.ToString("0.##"));
            AddLine("Rarity", Rarity.ToString());

            // CRAFTING
            if (CraftTime > 0f) AddLine("Craft Time", $"{CraftTime:0.##}s");

            // QUALITY / DURABILITY (base/limits only)
            if (BaseQuality != 1f) AddLine("Quality", BaseQuality.ToString("0.##"));
            if (MaxDurability > 0) AddLine("Durability", MaxDurability.ToString());

            // COMBAT / MOVEMENT
            if (Damage > 0f) AddLine("Damage", Damage.ToString("0.##"));
            if (Defence > 0f) AddLine("Defence", Defence.ToString("0.##"));
            if (SwingSpeed > 0f) AddLine("Swing Speed", SwingSpeed.ToString("0.##"));
            if (MovementSpeed != 1f) AddLine("Move Speed", $"{MovementSpeed:0.##}x");

            // EQUIPMENT
            if (IsEquippable)
            {
                AddLine("Equip Slot", EquipSlot.ToString());
                if (Handedness != Handedness.None)
                    AddLine("Handedness", Handedness.ToString());
            }

            // TOOL TAGS
            if (ToolTags != null && ToolTags.Length > 0)
                AddLine("Tool Tags", string.Join(", ", ToolTags));

            // OPTIONAL: manual notes appended after generated block
            if (appendManualPropertiesText && !string.IsNullOrWhiteSpace(PropertiesText))
            {
                if (sb.Length > 0)
                    sb.AppendLine().AppendLine();
                sb.Append(PropertiesText.Trim());
            }

            return sb.ToString();
        }
#endif
    }
}