using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// ItemDef
    /// -------------------------------------------------------
    /// Static item type definition (ScriptableObject).
    ///
    /// IMPORTANT:
    /// - This is STATIC data: "what this item type is".
    /// - Runtime-changing data (durability remaining, rolled stats, quality, etc.)
    ///   should eventually live in a runtime "ItemInstance" (future version),
    ///   NOT in this ScriptableObject.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Items/Item Definition", fileName = "ItemDef")]
    public sealed class ItemDef : ScriptableObject
    {
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

        [Header("Equip Visual")]
        [Tooltip("Optional prefab to spawn when equipped (visual-only; should NOT have NetworkObject).")]
        [SerializeField] private GameObject visualPrefab;

        [Tooltip("Local position offset applied after parenting to the hand anchor.")]
        [SerializeField] private Vector3 equipLocalPosition;

        [Tooltip("Local rotation offset (Euler degrees) applied after parenting to the hand anchor.")]
        [SerializeField] private Vector3 equipLocalEuler;

        [Tooltip("Local scale applied after parenting. Usually (1,1,1).")]
        [SerializeField] private Vector3 equipLocalScale = Vector3.one;

        public GameObject VisualPrefab => visualPrefab;
        public Vector3 EquipLocalPosition => equipLocalPosition;
        public Vector3 EquipLocalEuler => equipLocalEuler;
        public Vector3 EquipLocalScale => equipLocalScale;

        [Header("UI Text")]
        [TextArea(2, 6)]
        [Tooltip("Short description shown in UI.")]
        public string Description;

        [TextArea(2, 10)]
        [Tooltip("Extra lines shown in UI (eg: 'Damage: 12\\nSpeed: 1.2').")]
        public string PropertiesText;

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

        [Header("Quality / Durability")]
        [Tooltip("Base quality (1.0 = normal). Future: crafting may roll this per instance.")]
        [Min(0f)]
        public float BaseQuality = 1f;

        [Tooltip("Max durability limit for this item type. 0 = indestructible.")]
        [Min(0)]
        public int MaxDurability = 0;

        [Header("Combat")]
        [Tooltip("Base damage (weapons/tools).")]
        [Min(0f)]
        public float Damage = 0f;

        [Tooltip("Base defence (armor/shields).")]
        [Min(0f)]
        public float Defence = 0f;

        [Tooltip("Flat attack bonus added to d20 attack rolls when this item is used to attack.")]
        [Min(0)]
        public int AttackBonus = 0;

        [Tooltip("Attacks per second (or swings per second).")]
        [Min(0.01f)]
        public float SwingSpeed = 1f;

        [Header("Movement")]
        [Tooltip("Movement speed multiplier. 1 = normal speed, 0.9 = -10% speed.")]
        [Min(0f)]
        public float MovementSpeed = 1f;

        [Header("Attributes")]
        [Tooltip("Primary strength attribute bonus.")]
        [Min(0)]
        public int Strength = 0;

        [FormerlySerializedAs("Deterity")]
        [FormerlySerializedAs("Stamina")]
        [Tooltip("Primary dexterity attribute bonus.")]
        [Min(0)]
        public int Dexterity = 0;

        // Hidden legacy float value used by very old assets before int Dexterity existed.
        [FormerlySerializedAs("Deterity")]
        [FormerlySerializedAs("Stamina")]
        [HideInInspector] public float LegacyDexterity;

        [Tooltip("Primary intelligence attribute bonus.")]
        [Min(0)]
        public int Intelligence = 0;

        [Header("Equipment")]
        [Tooltip("If true, this item can be equipped into an equipment slot.")]
        public bool IsEquippable = false;

        [Tooltip("Which slot this item equips into.")]
        public EquipSlot EquipSlot = EquipSlot.None;

        [Tooltip("Hand usage rule for weapons/tools.")]
        public Handedness Handedness = Handedness.None;

        [Tooltip("Tags used for tool checks (eg, Axe for Woodcutting).")]
        public ToolTag[] ToolTags;

        [Header("Consumable Food (First Pass)")]
        [Tooltip("If true, this item can be consumed as one of up to 3 active food buffs.")]
        public bool IsFood = false;

        [Tooltip("Temporary max health bonus granted while this food buff is active.")]
        [Min(0)]
        public int FoodMaxHealthBonus = 0;

        [Tooltip("Temporary max stamina bonus granted while this food buff is active.")]
        [Min(0)]
        public int FoodMaxStaminaBonus = 0;

        [Tooltip("Flat health regeneration bonus per second while this food buff is active.")]
        [Min(0f)]
        public float FoodHealthRegenBonus = 0f;

        [Tooltip("Flat stamina regeneration bonus per second while this food buff is active.")]
        [Min(0f)]
        public float FoodStaminaRegenBonus = 0f;

        [Tooltip("How long this food buff remains active after consumption (seconds).")]
        [Min(0f)]
        public float FoodDurationSeconds = 0f;

        [Header("Placeable Building (Unified Item Model)")]
        [Tooltip("If true, this item can be placed into the world as a structure.")]
        public bool IsPlaceable = false;

        [Tooltip("Networked world prefab spawned when this placeable item is used.")]
        public NetworkObject PlaceablePrefab;

        [Tooltip("Optional local-only ghost prefab used for placement preview visuals.")]
        public GameObject GhostPrefab;

        [Tooltip("Optional spawn offset applied from requested placement position.")]
        public Vector3 PlacementOffset = Vector3.zero;

        [Tooltip("If false, placement ignores requested yaw and uses 0 rotation on Y.")]
        public bool AllowYawRotation = true;

        [Tooltip("Max health used by the placed world structure for this placeable item.")]
        [Min(1)]
        public int StructureMaxHealth = 100;

        [Tooltip("If true, placed structure despawns when health reaches zero.")]
        public bool DestroyOnZeroHealth = true;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (MaxStack < 1) MaxStack = 1;
            if (SwingSpeed <= 0f) SwingSpeed = 0.01f;
            if (MovementSpeed < 0f) MovementSpeed = 0f;
            if (Strength < 0) Strength = 0;
            if (Dexterity < 0) Dexterity = 0;
            if (Intelligence < 0) Intelligence = 0;
            if (AttackBonus < 0) AttackBonus = 0;
            if (BaseValue < 0) BaseValue = 0;
            if (BaseQuality < 0f) BaseQuality = 0f;
            if (MaxDurability < 0) MaxDurability = 0;

            // One-time migration fallback for old float dexterity assets.
            if (Dexterity <= 0 && LegacyDexterity > 0f)
                Dexterity = Mathf.RoundToInt(LegacyDexterity);

            if (!IsEquippable)
            {
                EquipSlot = EquipSlot.None;
                Handedness = Handedness.None;
            }

            if (equipLocalScale.x == 0f && equipLocalScale.y == 0f && equipLocalScale.z == 0f)
                equipLocalScale = Vector3.one;

            if (!IsFood)
            {
                FoodMaxHealthBonus = 0;
                FoodMaxStaminaBonus = 0;
                FoodHealthRegenBonus = 0f;
                FoodStaminaRegenBonus = 0f;
                FoodDurationSeconds = 0f;
            }
            else
            {
                if (FoodMaxHealthBonus < 0) FoodMaxHealthBonus = 0;
                if (FoodMaxStaminaBonus < 0) FoodMaxStaminaBonus = 0;
                if (FoodHealthRegenBonus < 0f) FoodHealthRegenBonus = 0f;
                if (FoodStaminaRegenBonus < 0f) FoodStaminaRegenBonus = 0f;
                if (FoodDurationSeconds <= 0f) FoodDurationSeconds = 1f;

            }
            // Unified model rule:
            // Placeable build pieces are still regular items, but require a world prefab.
            if (IsPlaceable && PlaceablePrefab == null)
                Debug.LogWarning($"[ItemDef] Item '{ItemId}' is marked IsPlaceable but PlaceablePrefab is missing.", this);

            // Structure durability for placed world objects.
            if (StructureMaxHealth < 1) StructureMaxHealth = 1;
        }
#endif

        public string BuildPropertiesText(bool appendManualPropertiesText = true)
        {
            var sb = new StringBuilder(256);

            void AddLine(string label, string value)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(label).Append(": ").Append(value);
            }

            if (BaseValue > 0) AddLine("Base Value", BaseValue.ToString());
            if (Weight > 0f) AddLine("Weight", Weight.ToString("0.##"));
            AddLine("Rarity", Rarity.ToString());

            if (CraftTime > 0f) AddLine("Craft Time", $"{CraftTime:0.##}s");

            if (BaseQuality != 1f) AddLine("Quality", BaseQuality.ToString("0.##"));
            if (MaxDurability > 0) AddLine("Durability", MaxDurability.ToString());

            if (Strength > 0) AddLine("Strength", Strength.ToString());
            if (Dexterity > 0) AddLine("Dexterity", Dexterity.ToString());
            if (Intelligence > 0) AddLine("Intelligence", Intelligence.ToString());

            if (Damage > 0f) AddLine("Damage", Damage.ToString("0.##"));
            if (Defence > 0f) AddLine("Defence", Defence.ToString("0.##"));
            if (AttackBonus > 0) AddLine("Attack Bonus", AttackBonus.ToString());
            if (SwingSpeed > 0f) AddLine("Swing Speed", SwingSpeed.ToString("0.##"));
            if (MovementSpeed != 1f) AddLine("Move Speed", $"{MovementSpeed:0.##}x");

            if (IsEquippable)
            {
                AddLine("Equip Slot", EquipSlot.ToString());
                if (Handedness != Handedness.None)
                    AddLine("Handedness", Handedness.ToString());
            }

            if (ToolTags != null && ToolTags.Length > 0)
                AddLine("Tool Tags", string.Join(", ", ToolTags));

            if (appendManualPropertiesText && !string.IsNullOrWhiteSpace(PropertiesText))
            {
                if (sb.Length > 0)
                    sb.AppendLine().AppendLine();
                sb.Append(PropertiesText.Trim());
            }

            return sb.ToString();
        }
    }
}











