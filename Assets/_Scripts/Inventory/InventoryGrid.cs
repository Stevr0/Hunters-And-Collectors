using HuntersAndCollectors.Items;
using Unity.Collections;

namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Fixed-size slot grid with validated add/remove/move/split operations.
    ///
    /// This grid now supports two authoritative payload styles:
    /// - Stack payloads for normal stackable resources.
    /// - ItemInstance payloads for unique crafted gear.
    ///
    /// Note:
    /// Legacy bridge fields on InventorySlot (Durability/InstanceData) are kept in sync
    /// so existing equipment/building flows continue to work while the project migrates.
    /// </summary>
    public sealed class InventoryGrid
    {
        private readonly ItemDatabase itemDatabase;
        public int Width { get; }
        public int Height { get; }
        public InventorySlot[] Slots { get; }

        public InventoryGrid(int width, int height, ItemDatabase database)
        {
            Width = width < 1 ? 1 : width;
            Height = height < 1 ? 1 : height;
            Slots = new InventorySlot[Width * Height];
            itemDatabase = database;
            for (var i = 0; i < Slots.Length; i++)
                Slots[i] = MakeEmptySlot();
        }

        public bool CanAdd(string itemId, int quantity, out int remainder)
        {
            return CanAdd(itemId, quantity, out remainder, durability: -1, bonusStrength: 0, bonusDexterity: 0, bonusIntelligence: 0, craftedBy: default);
        }

        public bool CanAdd(
            string itemId,
            int quantity,
            out int remainder,
            int durability,
            int bonusStrength,
            int bonusDexterity,
            int bonusIntelligence,
            FixedString64Bytes craftedBy)
        {
            remainder = quantity;
            if (quantity <= 0 || !TryGetDef(itemId, out var def))
                return false;

            // Instance items are never stack-merged. They need one empty slot each.
            if (IsInstance(def))
            {
                int empty = CountEmptySlots();
                remainder = quantity > empty ? quantity - empty : 0;
                return remainder == 0;
            }

            int maxStack = GetMaxStack(def);
            int remaining = quantity;
            ItemInstanceData incomingInstance = BuildLegacyInstanceData(def, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy, durability);

            bool incomingHasInstanceData = incomingInstance.HasAnyBonus || incomingInstance.HasCrafter || incomingInstance.HasRolledStats;

            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                var slot = Slots[i];

                if (slot.ContentType == InventorySlotContentType.Stack &&
                    !slot.IsEmpty &&
                    slot.Stack.ItemId == itemId)
                {
                    // Only plain stacks can merge.
                    bool slotHasInstanceData = slot.InstanceData.HasAnyBonus || slot.InstanceData.HasCrafter || slot.InstanceData.HasRolledStats;
                    if (!slotHasInstanceData && !incomingHasInstanceData)
                        remaining -= maxStack - slot.Stack.Quantity;
                }

                if (slot.IsEmpty || slot.ContentType == InventorySlotContentType.Empty)
                    remaining -= maxStack;
            }

            remainder = remaining > 0 ? remaining : 0;
            return remainder == 0;
        }

        public int Add(string itemId, int quantity, int durability = -1, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0, FixedString64Bytes craftedBy = default)
        {
            if (quantity <= 0 || !TryGetDef(itemId, out var def))
                return quantity;

            // For instance items, each quantity creates/uses one concrete slot payload.
            if (IsInstance(def))
            {
                int remainingInstances = quantity;
                for (int i = 0; i < Slots.Length && remainingInstances > 0; i++)
                {
                    if (!Slots[i].IsEmpty)
                        continue;

                    var instance = new ItemInstance
                    {
                        InstanceId = 0,
                        ItemId = itemId,
                        RolledDamage = 0f,
                        RolledDefence = 0f,
                        RolledSwingSpeed = 0f,
                        RolledMovementSpeed = 0f,
                        MaxDurability = durability > 0 ? durability : def.ResolveDurabilityMax(),
                        CurrentDurability = durability > 0 ? durability : def.ResolveDurabilityMax()
                    };

                    SetSlotToInstance(i, instance, BuildLegacyInstanceData(def, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy, instance.CurrentDurability));
                    remainingInstances--;
                }

                return remainingInstances;
            }

            int maxStack = GetMaxStack(def);
            int remaining = quantity;

            ItemInstanceData instanceData = BuildLegacyInstanceData(def, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy, durability);

            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (Slots[i].ContentType != InventorySlotContentType.Stack || Slots[i].IsEmpty || Slots[i].Stack.ItemId != itemId)
                    continue;

                if (Slots[i].InstanceData.HasAnyBonus || Slots[i].InstanceData.HasCrafter || Slots[i].InstanceData.HasRolledStats || instanceData.HasAnyBonus || instanceData.HasCrafter || instanceData.HasRolledStats)
                    continue;

                var room = maxStack - Slots[i].Stack.Quantity;
                if (room <= 0)
                    continue;

                var moved = remaining < room ? remaining : room;
                Slots[i].Stack.Quantity += moved;
                remaining -= moved;
            }

            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (!Slots[i].IsEmpty)
                    continue;

                var moved = remaining < maxStack ? remaining : maxStack;
                Slots[i] = new InventorySlot
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Stack,
                    Stack = new ItemStack { ItemId = itemId, Quantity = moved },
                    Instance = default,
                    Durability = 0,
                    InstanceData = default
                };
                remaining -= moved;
            }

            return remaining;
        }

        /// <summary>
        /// Validates whether one concrete ItemInstance can fit in any empty slot.
        /// </summary>
        public bool CanAddInstance(in ItemInstance instance)
        {
            if (!instance.IsValid)
                return false;

            return CountEmptySlots() > 0;
        }

        /// <summary>
        /// Adds one concrete ItemInstance into first empty slot.
        /// </summary>
        public bool TryAddInstance(in ItemInstance instance, in ItemInstanceData instanceData)
        {
            if (!instance.IsValid)
                return false;

            for (int i = 0; i < Slots.Length; i++)
            {
                if (!Slots[i].IsEmpty)
                    continue;

                SetSlotToInstance(i, instance, instanceData);
                return true;
            }

            return false;
        }

        public bool CanRemove(string itemId, int quantity)
        {
            if (quantity <= 0)
                return false;

            var have = 0;
            foreach (var slot in Slots)
            {
                if (slot.IsEmpty)
                    continue;

                if (slot.ContentType == InventorySlotContentType.Stack && slot.Stack.ItemId == itemId)
                    have += slot.Stack.Quantity;
                else if (slot.ContentType == InventorySlotContentType.Instance && slot.Instance.ItemId == itemId)
                    have += 1;
            }

            return have >= quantity;
        }

        public bool Remove(string itemId, int quantity)
        {
            if (!CanRemove(itemId, quantity))
                return false;

            var remaining = quantity;
            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (Slots[i].IsEmpty)
                    continue;

                if (Slots[i].ContentType == InventorySlotContentType.Instance)
                {
                    if (Slots[i].Instance.ItemId != itemId)
                        continue;

                    Slots[i] = MakeEmptySlot();
                    remaining -= 1;
                    continue;
                }

                if (Slots[i].ContentType != InventorySlotContentType.Stack || Slots[i].Stack.ItemId != itemId)
                    continue;

                var take = Slots[i].Stack.Quantity < remaining ? Slots[i].Stack.Quantity : remaining;
                Slots[i].Stack.Quantity -= take;
                remaining -= take;

                if (Slots[i].Stack.Quantity <= 0)
                    Slots[i] = MakeEmptySlot();
            }

            return true;
        }

        public bool TryMoveSlot(int fromIndex, int toIndex)
        {
            if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex) || fromIndex == toIndex)
                return false;

            var from = Slots[fromIndex];
            var to = Slots[toIndex];
            if (from.IsEmpty)
                return false;

            if (to.IsEmpty)
            {
                Slots[toIndex] = from;
                Slots[fromIndex] = MakeEmptySlot();
                return true;
            }

            // Only stack slots of same item can merge.
            if (from.ContentType == InventorySlotContentType.Stack &&
                to.ContentType == InventorySlotContentType.Stack &&
                to.Stack.ItemId == from.Stack.ItemId &&
                TryGetDef(from.Stack.ItemId, out var def) &&
                !IsInstance(def) &&
                !to.InstanceData.HasAnyBonus &&
                !to.InstanceData.HasCrafter &&
                !to.InstanceData.HasRolledStats &&
                !from.InstanceData.HasAnyBonus &&
                !from.InstanceData.HasCrafter &&
                !from.InstanceData.HasRolledStats)
            {
                var room = GetMaxStack(def) - to.Stack.Quantity;
                var move = room < from.Stack.Quantity ? room : from.Stack.Quantity;
                if (move <= 0)
                    return false;

                to.Stack.Quantity += move;
                from.Stack.Quantity -= move;
                Slots[toIndex] = to;
                Slots[fromIndex] = from.Stack.Quantity <= 0 ? MakeEmptySlot() : from;
                return true;
            }

            // Swap for dissimilar stack items or any instance item pairings.
            Slots[fromIndex] = to;
            Slots[toIndex] = from;
            return true;
        }

        public bool TrySplitStack(int index, int splitAmount, out int newSlotIndex)
        {
            newSlotIndex = -1;
            if (!IsValidIndex(index) || splitAmount <= 0 || Slots[index].IsEmpty)
                return false;

            if (Slots[index].ContentType != InventorySlotContentType.Stack)
                return false;

            if (!TryGetDef(Slots[index].Stack.ItemId, out var def))
                return false;

            if (IsInstance(def))
                return false;

            if (Slots[index].InstanceData.HasAnyBonus || Slots[index].InstanceData.HasCrafter || Slots[index].InstanceData.HasRolledStats)
                return false;

            if (Slots[index].Stack.Quantity <= splitAmount)
                return false;

            for (var i = 0; i < Slots.Length; i++)
            {
                if (!Slots[i].IsEmpty)
                    continue;

                Slots[index].Stack.Quantity -= splitAmount;
                Slots[i] = new InventorySlot
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Stack,
                    Stack = new ItemStack { ItemId = Slots[index].Stack.ItemId, Quantity = splitAmount },
                    Instance = default,
                    Durability = 0,
                    InstanceData = default
                };

                newSlotIndex = i;
                return true;
            }

            return false;
        }

        public bool TryAddOneToSlot(string itemId, int slotIndex, int durability = -1, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0, FixedString64Bytes craftedBy = default)
        {
            if (!IsValidIndex(slotIndex) || !TryGetDef(itemId, out var def))
                return false;

            ref var slot = ref Slots[slotIndex];
            bool isInstance = IsInstance(def);
            ItemInstanceData legacyData = BuildLegacyInstanceData(def, bonusStrength, bonusDexterity, bonusIntelligence, craftedBy, durability);

            if (slot.IsEmpty)
            {
                if (isInstance)
                {
                    var instance = new ItemInstance
                    {
                        InstanceId = 0,
                        ItemId = itemId,
                        RolledDamage = 0f,
                        RolledDefence = 0f,
                        RolledSwingSpeed = 0f,
                        RolledMovementSpeed = 0f,
                        MaxDurability = durability > 0 ? durability : def.ResolveDurabilityMax(),
                        CurrentDurability = durability > 0 ? durability : def.ResolveDurabilityMax()
                    };

                    slot = BuildInstanceSlot(instance, legacyData);
                    return true;
                }

                slot = new InventorySlot
                {
                    IsEmpty = false,
                    ContentType = InventorySlotContentType.Stack,
                    Stack = new ItemStack { ItemId = itemId, Quantity = 1 },
                    Instance = default,
                    Durability = 0,
                    InstanceData = default
                };
                return true;
            }

            // Existing occupied slot can only receive another unit for stack payload.
            if (isInstance)
                return false;

            if (slot.ContentType != InventorySlotContentType.Stack)
                return false;

            if (legacyData.HasAnyBonus || legacyData.HasCrafter || legacyData.HasRolledStats)
                return false;

            if (slot.InstanceData.HasAnyBonus || slot.InstanceData.HasCrafter || slot.InstanceData.HasRolledStats)
                return false;

            if (slot.Stack.ItemId != itemId)
                return false;

            if (slot.Stack.Quantity >= GetMaxStack(def))
                return false;

            slot.Stack.Quantity += 1;
            return true;
        }

        private void SetSlotToInstance(int slotIndex, in ItemInstance instance, in ItemInstanceData legacyData)
        {
            Slots[slotIndex] = BuildInstanceSlot(instance, legacyData);
        }

        private static InventorySlot BuildInstanceSlot(in ItemInstance instance, in ItemInstanceData legacyData)
        {
            ItemInstanceData data = legacyData;
            if (data.InstanceId == 0)
                data.InstanceId = instance.InstanceId;

            if (data.RolledDamage <= 0f)
                data.RolledDamage = instance.RolledDamage;
            if (data.RolledDefence <= 0f)
                data.RolledDefence = instance.RolledDefence;
            if (data.RolledSwingSpeed <= 0f)
                data.RolledSwingSpeed = instance.RolledSwingSpeed;
            if (data.RolledMovementSpeed <= 0f)
                data.RolledMovementSpeed = instance.RolledMovementSpeed;

            data.MaxDurability = instance.MaxDurability;
            data.CurrentDurability = instance.CurrentDurability;

            return new InventorySlot
            {
                IsEmpty = false,
                ContentType = InventorySlotContentType.Instance,
                Stack = new ItemStack { ItemId = instance.ItemId, Quantity = 1 },
                Instance = instance,
                Durability = instance.CurrentDurability,
                InstanceData = data
            };
        }

        private static InventorySlot MakeEmptySlot()
        {
            return new InventorySlot
            {
                IsEmpty = true,
                ContentType = InventorySlotContentType.Empty,
                Stack = default,
                Instance = default,
                Durability = 0,
                InstanceData = default
            };
        }

        private int CountEmptySlots()
        {
            int count = 0;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].IsEmpty || Slots[i].ContentType == InventorySlotContentType.Empty)
                    count++;
            }

            return count;
        }

        private static ItemInstanceData BuildLegacyInstanceData(ItemDef def, int bonusStrength, int bonusDexterity, int bonusIntelligence, FixedString64Bytes craftedBy, int durability)
        {
            if (def == null)
                return default;

            bool allowInstance = def.UsesItemInstance || def.IsEquippable || (def.ToolTags != null && def.ToolTags.Length > 0);
            if (!allowInstance)
                return default;

            ItemInstanceData data;
            data.BonusStrength = bonusStrength;
            data.BonusDexterity = bonusDexterity;
            data.BonusIntelligence = bonusIntelligence;
            data.CraftedBy = craftedBy;

            data.InstanceId = 0;
            data.RolledDamage = 0f;
            data.RolledDefence = 0f;
            data.RolledSwingSpeed = 0f;
            data.RolledMovementSpeed = 0f;
            data.MaxDurability = durability > 0 ? durability : def.ResolveDurabilityMax();
            data.CurrentDurability = durability > 0 ? durability : def.ResolveDurabilityMax();
            return data;
        }

        private static bool IsInstance(ItemDef def) => def != null && def.UsesItemInstance;

        private static int GetMaxStack(ItemDef def)
        {
            if (def == null)
                return 1;

            if (IsInstance(def))
                return 1;

            return def.MaxStack < 1 ? 1 : def.MaxStack;
        }

        private bool TryGetDef(string itemId, out ItemDef def)
        {
            def = null;
            if (itemDatabase == null || string.IsNullOrWhiteSpace(itemId))
                return false;

            return itemDatabase.TryGet(itemId, out def) && def != null;
        }

        private bool IsValidIndex(int index) => index >= 0 && index < Slots.Length;
    }
}
