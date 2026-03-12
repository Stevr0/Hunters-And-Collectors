using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// Static item definition asset.
    ///
    /// This ScriptableObject defines what an item TYPE is:
    /// - identity
    /// - UI text
    /// - economy values
    /// - equipment/tool metadata
    /// - harvesting capability
    /// - optional stat roll ranges for future instance-crafted items
    ///
    /// IMPORTANT:
    /// - This is STATIC template data only.
    /// - Runtime-changing data (current durability, actual rolled stats, etc.)
    ///   must NOT live here.
    /// - Runtime item state belongs in runtime inventory/item instance systems.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Items/Item Definition", fileName = "ItemDef")]
    public sealed class ItemDef : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable unique id (example: IT_Wood, IT_StoneAxe). Must never change once released.")]
        public string ItemId;

        [Tooltip("Name shown to players.")]
        public string DisplayName;

        [Tooltip("High-level category used by UI and gameplay rules.")]
        public ItemCategory Category;

        [Tooltip("UI icon used in inventory, crafting, tooltips, etc.")]
        public Sprite Icon;

        [Tooltip("Maximum quantity allowed in one stack. Resources are usually high. Tools/equipment are usually 1.")]
        [Min(1)]
        public int MaxStack = 1;

        [Header("UI Text")]
        [TextArea(2, 6)]
        [Tooltip("Short description shown in item tooltips and lists.")]
        public string Description;

        [TextArea(2, 10)]
        [Tooltip("Extra detail lines shown in the tooltip. Example: damage, durability, harvesting power.")]
        public string PropertiesText;

        [Header("Economy")]
        [Tooltip("Default base value before player-defined pricing is applied.")]
        [Min(0)]
        public int BaseValue = 1;

        [Tooltip("Weight of one unit of this item.")]
        [Min(0f)]
        public float Weight = 0f;

        [Tooltip("Display rarity / progression tier for UI and content authoring.")]
        public ItemRarity Rarity = ItemRarity.Common;

        [Header("Crafting")]
        [Tooltip("Base craft time in seconds before skill modifiers.")]
        [Min(0f)]
        public float CraftTime = 1f;

        [Tooltip("If true, crafted output for this item should become a non-stackable runtime instance rather than a simple stack item.")]
        public bool IsInstanceItem = false;

        [Header("Equip Visual")]
        [Tooltip("Optional visual prefab spawned when this item is equipped. Visual only. Should not contain a NetworkObject.")]
        [SerializeField] private GameObject visualPrefab;

        [Header("World Drop")]
        [Tooltip("Dedicated runtime world pickup prefab for drops and harvesting. Must use a root NetworkObject and must not contain nested child NetworkObjects.")]
        [SerializeField] private GameObject worldDropPrefab;

        [Header("Equip Visual")]
        [Tooltip("Local position offset applied after parenting to the equip anchor.")]
        [SerializeField] private Vector3 equipLocalPosition = Vector3.zero;

        [Tooltip("Local rotation offset in Euler angles applied after parenting to the equip anchor.")]
        [SerializeField] private Vector3 equipLocalEuler = Vector3.zero;

        [Tooltip("Local scale applied after parenting to the equip anchor.")]
        [SerializeField] private Vector3 equipLocalScale = Vector3.one;

        public GameObject VisualPrefab => visualPrefab;
        public GameObject WorldDropPrefab => worldDropPrefab;
        public Vector3 EquipLocalPosition => equipLocalPosition;
        public Vector3 EquipLocalEuler => equipLocalEuler;
        public Vector3 EquipLocalScale => equipLocalScale;

        [Header("Equipment")]
        [Tooltip("If true, this item can be equipped into an equipment slot.")]
        public bool IsEquippable = false;

        [Tooltip("Equipment slot used by this item.")]
        public EquipSlot EquipSlot = EquipSlot.None;

        [Tooltip("Hand usage rule for tools/weapons.")]
        public Handedness Handedness = Handedness.None;

        [Header("Harvesting / Tool Data")]
        [Tooltip("Tags used for harvest validation. Example: Axe, Pickaxe, Knife.")]
        public ToolTag[] ToolTags;

        [Tooltip("Static harvesting capability used for progression and node/tool requirement checks.")]
        [Min(0)]
        public int HarvestPower = 0;

        [Header("Instance Roll Template")]
        [Tooltip("If this item becomes an instance item, server rolls damage in this range during crafting.")]
        [Min(0f)]
        public float DamageMin = 0f;

        [Tooltip("Maximum roll value for damage.")]
        [Min(0f)]
        public float DamageMax = 0f;

        [Tooltip("If this item becomes an instance item, server rolls defence in this range during crafting.")]
        [Min(0f)]
        public float DefenceMin = 0f;

        [Tooltip("Maximum roll value for defence.")]
        [Min(0f)]
        public float DefenceMax = 0f;

        [Tooltip("If this item becomes an instance item, server rolls swing speed in this range during crafting.")]
        [Min(0.01f)]
        public float SwingSpeedMin = 1f;

        [Tooltip("Maximum roll value for swing speed.")]
        [Min(0.01f)]
        public float SwingSpeedMax = 1f;

        [Tooltip("If this item becomes an instance item, server rolls movement speed multiplier in this range during crafting.")]
        [Min(0f)]
        public float MovementSpeedMin = 1f;

        [Tooltip("Maximum roll value for movement speed multiplier.")]
        [Min(0f)]
        public float MovementSpeedMax = 1f;

        [Tooltip("If this item becomes an instance item, server rolls maximum durability in this range during crafting.")]
        [Min(0)]
        public int DurabilityMin = 0;

        [Tooltip("Maximum roll value for durability.")]
        [Min(0)]
        public int DurabilityMax = 0;

        [Header("Attributes")]
        [Tooltip("Flat strength bonus provided by this item type or rolled instance.")]
        public int Strength = 0;

        [Tooltip("Flat dexterity bonus provided by this item type or rolled instance.")]
        public int Dexterity = 0;

        [Tooltip("Flat intelligence bonus provided by this item type or rolled instance.")]
        public int Intelligence = 0;

        [Header("Consumable Food")]
        [Tooltip("If true, this item can be consumed as a food buff.")]
        public bool IsFood = false;

        [Tooltip("Temporary max health bonus while the food buff is active.")]
        [Min(0)]
        public int FoodMaxHealthBonus = 0;

        [Tooltip("Temporary max stamina bonus while the food buff is active.")]
        [Min(0)]
        public int FoodMaxStaminaBonus = 0;

        [Tooltip("Flat health regeneration bonus per second while active.")]
        [Min(0f)]
        public float FoodHealthRegenBonus = 0f;

        [Tooltip("Flat stamina regeneration bonus per second while active.")]
        [Min(0f)]
        public float FoodStaminaRegenBonus = 0f;

        [Tooltip("Food buff duration in seconds.")]
        [Min(0f)]
        public float FoodDurationSeconds = 0f;

        [Header("Legacy Building Compatibility")]
        [Tooltip("Temporary compatibility flag for existing building systems that still read ItemDef placeable data.")]
        public bool IsPlaceable = false;

        [Tooltip("Temporary compatibility prefab reference for existing building systems.")]
        public NetworkObject PlaceablePrefab;

        [Tooltip("Temporary compatibility ghost prefab for existing building placement visuals.")]
        public GameObject GhostPrefab;

        [Tooltip("Temporary compatibility placement offset for existing building placement code.")]
        public Vector3 PlacementOffset = Vector3.zero;

        [Tooltip("Temporary compatibility yaw rotation toggle for existing building placement code.")]
        public bool AllowYawRotation = true;

        [Tooltip("Temporary compatibility placed structure health for existing building runtime code.")]
        [Min(1)]
        public int StructureMaxHealth = 100;

        [Tooltip("Temporary compatibility destroy-on-zero-health flag for existing building runtime code.")]
        public bool DestroyOnZeroHealth = true;

        /// <summary>
        /// Convenience helper for authoring/debug UI.
        /// Safe to use for tooltips and inspector summaries.
        /// </summary>
        public string BuildPropertiesText()
        {
            StringBuilder sb = new StringBuilder();

            if (HarvestPower > 0)
                sb.AppendLine($"Harvest Power: {HarvestPower}");

            if (IsInstanceItem)
            {
                if (DamageMax > 0f)
                    sb.AppendLine($"Damage: {DamageMin:0.##} - {DamageMax:0.##}");

                if (DefenceMax > 0f)
                    sb.AppendLine($"Defence: {DefenceMin:0.##} - {DefenceMax:0.##}");

                if (SwingSpeedMax > 0f)
                    sb.AppendLine($"Swing Speed: {SwingSpeedMin:0.##} - {SwingSpeedMax:0.##}");

                if (MovementSpeedMin != 1f || MovementSpeedMax != 1f)
                    sb.AppendLine($"Move Speed: {MovementSpeedMin:0.##} - {MovementSpeedMax:0.##}");

                if (DurabilityMax > 0)
                    sb.AppendLine($"Durability: {DurabilityMin} - {DurabilityMax}");
            }

            if (Strength != 0)
                sb.AppendLine($"Strength: {Strength:+#;-#;0}");

            if (Dexterity != 0)
                sb.AppendLine($"Dexterity: {Dexterity:+#;-#;0}");

            if (Intelligence != 0)
                sb.AppendLine($"Intelligence: {Intelligence:+#;-#;0}");

            if (IsFood)
            {
                if (FoodMaxHealthBonus > 0)
                    sb.AppendLine($"Food Health: +{FoodMaxHealthBonus}");

                if (FoodMaxStaminaBonus > 0)
                    sb.AppendLine($"Food Stamina: +{FoodMaxStaminaBonus}");

                if (FoodHealthRegenBonus > 0f)
                    sb.AppendLine($"Health Regen: +{FoodHealthRegenBonus:0.##}/s");

                if (FoodStaminaRegenBonus > 0f)
                    sb.AppendLine($"Stamina Regen: +{FoodStaminaRegenBonus:0.##}/s");

                if (FoodDurationSeconds > 0f)
                    sb.AppendLine($"Duration: {FoodDurationSeconds:0.#}s");
            }

            return sb.ToString().TrimEnd();
        }

        // Thin compatibility surface so the rest of the project can migrate to the new template model incrementally.
        public bool UsesItemInstance => IsInstanceItem;
        public float Damage => DamageMin;
        public float Defence => DefenceMin;
        public int AttackBonus => 0;
        public float SwingSpeed => SwingSpeedMin;
        public float MovementSpeed => MovementSpeedMin;
        public int MaxDurability => DurabilityMax;

        public float ResolveDamageMin() => DamageMin;
        public float ResolveDamageMax() => DamageMax;
        public float ResolveDefenceMin() => DefenceMin;
        public float ResolveDefenceMax() => DefenceMax;
        public float ResolveSwingSpeedMin() => SwingSpeedMin;
        public float ResolveSwingSpeedMax() => SwingSpeedMax;
        public float ResolveMovementSpeedMin() => MovementSpeedMin;
        public float ResolveMovementSpeedMax() => MovementSpeedMax;
        public int ResolveDurabilityMin() => DurabilityMin;
        public int ResolveDurabilityMax() => DurabilityMax;
        public string BuildPropertiesText(bool appendManualPropertiesText) => BuildPropertiesText();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (MaxStack < 1)
                MaxStack = 1;

            if (IsInstanceItem)
                MaxStack = 1;

            if (!IsEquippable)
            {
                EquipSlot = EquipSlot.None;
                Handedness = Handedness.None;
            }

            if (BaseValue < 0)
                BaseValue = 0;

            if (Weight < 0f)
                Weight = 0f;

            if (CraftTime < 0f)
                CraftTime = 0f;

            if (HarvestPower < 0)
                HarvestPower = 0;

            if (FoodMaxHealthBonus < 0)
                FoodMaxHealthBonus = 0;

            if (FoodMaxStaminaBonus < 0)
                FoodMaxStaminaBonus = 0;

            if (FoodHealthRegenBonus < 0f)
                FoodHealthRegenBonus = 0f;

            if (FoodStaminaRegenBonus < 0f)
                FoodStaminaRegenBonus = 0f;

            if (FoodDurationSeconds < 0f)
                FoodDurationSeconds = 0f;

            if (DamageMax < DamageMin)
                DamageMax = DamageMin;

            if (DefenceMax < DefenceMin)
                DefenceMax = DefenceMin;

            if (SwingSpeedMin < 0.01f)
                SwingSpeedMin = 0.01f;

            if (SwingSpeedMax < SwingSpeedMin)
                SwingSpeedMax = SwingSpeedMin;

            if (MovementSpeedMin < 0f)
                MovementSpeedMin = 0f;

            if (MovementSpeedMax < MovementSpeedMin)
                MovementSpeedMax = MovementSpeedMin;

            if (DurabilityMin < 0)
                DurabilityMin = 0;

            if (DurabilityMax < DurabilityMin)
                DurabilityMax = DurabilityMin;

            if (StructureMaxHealth < 1)
                StructureMaxHealth = 1;

            if (string.IsNullOrWhiteSpace(PropertiesText))
                PropertiesText = BuildPropertiesText();
        }
#endif
    }
}


