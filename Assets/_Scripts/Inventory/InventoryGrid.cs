using HuntersAndCollectors.Items;

namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Fixed-size slot grid with validated add/remove/move/split operations.
    ///
    /// Durable items (MaxDurability > 0) are treated as non-stackable slot items.
    /// Per-instance attribute bonuses are stored only on non-stackable crafted
    /// equippable/tool items. Regular stackables always keep zero bonuses.
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
                Slots[i].IsEmpty = true;
        }

        public bool CanAdd(string itemId, int quantity, out int remainder)
        {
            remainder = quantity;
            if (quantity <= 0 || !TryGetDef(itemId, out var def))
                return false;

            bool durable = IsDurable(def);
            int maxStack = GetMaxStack(def);
            int remaining = quantity;

            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                // Bonus instances never stack into normal stacks.
                if (!Slots[i].IsEmpty && Slots[i].Stack.ItemId == itemId && !durable && !Slots[i].InstanceData.HasAnyBonus)
                    remaining -= maxStack - Slots[i].Stack.Quantity;

                if (Slots[i].IsEmpty)
                    remaining -= maxStack;
            }

            remainder = remaining > 0 ? remaining : 0;
            return remainder == 0;
        }

        public int Add(string itemId, int quantity, int durability = -1, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0)
        {
            if (quantity <= 0 || !TryGetDef(itemId, out var def))
                return quantity;

            bool durable = IsDurable(def);
            int maxStack = GetMaxStack(def);
            int remaining = quantity;

            ItemInstanceData instanceData = BuildInstanceData(def, bonusStrength, bonusDexterity, bonusIntelligence);

            if (!durable)
            {
                for (var i = 0; i < Slots.Length && remaining > 0; i++)
                {
                    if (Slots[i].IsEmpty || Slots[i].Stack.ItemId != itemId)
                        continue;

                    // Never merge stacks that carry instance bonuses.
                    if (Slots[i].InstanceData.HasAnyBonus || instanceData.HasAnyBonus)
                        continue;

                    var room = maxStack - Slots[i].Stack.Quantity;
                    if (room <= 0)
                        continue;

                    var moved = remaining < room ? remaining : room;
                    Slots[i].Stack.Quantity += moved;
                    remaining -= moved;
                }
            }

            int defaultDurability = def.MaxDurability > 0 ? def.MaxDurability : 0;
            int clampedDurability = def.MaxDurability > 0
                ? (durability > 0 ? (durability > def.MaxDurability ? def.MaxDurability : durability) : defaultDurability)
                : 0;

            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (!Slots[i].IsEmpty)
                    continue;

                var moved = remaining < maxStack ? remaining : maxStack;
                Slots[i].IsEmpty = false;
                Slots[i].Stack = new ItemStack { ItemId = itemId, Quantity = moved };
                Slots[i].Durability = durable ? clampedDurability : 0;

                // Only non-stackable item instances should carry per-item bonuses.
                Slots[i].InstanceData = durable || moved == 1 ? instanceData : default;

                remaining -= moved;
            }

            return remaining;
        }

        public bool CanRemove(string itemId, int quantity)
        {
            if (quantity <= 0)
                return false;

            var have = 0;
            foreach (var slot in Slots)
            {
                if (!slot.IsEmpty && slot.Stack.ItemId == itemId)
                    have += slot.Stack.Quantity;
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
                if (Slots[i].IsEmpty || Slots[i].Stack.ItemId != itemId)
                    continue;

                var take = Slots[i].Stack.Quantity < remaining ? Slots[i].Stack.Quantity : remaining;
                Slots[i].Stack.Quantity -= take;
                remaining -= take;

                if (Slots[i].Stack.Quantity <= 0)
                    Slots[i] = new InventorySlot { IsEmpty = true };
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
                Slots[fromIndex] = new InventorySlot { IsEmpty = true };
                return true;
            }

            if (to.Stack.ItemId == from.Stack.ItemId &&
                TryGetDef(from.Stack.ItemId, out var def) &&
                !IsDurable(def) &&
                !to.InstanceData.HasAnyBonus &&
                !from.InstanceData.HasAnyBonus)
            {
                var room = GetMaxStack(def) - to.Stack.Quantity;
                var move = room < from.Stack.Quantity ? room : from.Stack.Quantity;
                if (move <= 0)
                    return false;

                to.Stack.Quantity += move;
                from.Stack.Quantity -= move;
                Slots[toIndex] = to;
                Slots[fromIndex] = from.Stack.Quantity <= 0 ? new InventorySlot { IsEmpty = true } : from;
                return true;
            }

            Slots[fromIndex] = to;
            Slots[toIndex] = from;
            return true;
        }

        public bool TrySplitStack(int index, int splitAmount, out int newSlotIndex)
        {
            newSlotIndex = -1;
            if (!IsValidIndex(index) || splitAmount <= 0 || Slots[index].IsEmpty)
                return false;

            if (!TryGetDef(Slots[index].Stack.ItemId, out var def))
                return false;

            if (IsDurable(def))
                return false;

            // Bonus instances are not stackables and should never split-merge behavior.
            if (Slots[index].InstanceData.HasAnyBonus)
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
                    Stack = new ItemStack { ItemId = Slots[index].Stack.ItemId, Quantity = splitAmount },
                    Durability = 0,
                    InstanceData = default
                };

                newSlotIndex = i;
                return true;
            }

            return false;
        }

        public bool TryAddOneToSlot(string itemId, int slotIndex, int durability = -1, int bonusStrength = 0, int bonusDexterity = 0, int bonusIntelligence = 0)
        {
            if (!IsValidIndex(slotIndex) || !TryGetDef(itemId, out var def))
                return false;

            ref var slot = ref Slots[slotIndex];
            bool durable = IsDurable(def);
            ItemInstanceData instanceData = BuildInstanceData(def, bonusStrength, bonusDexterity, bonusIntelligence);

            if (slot.IsEmpty)
            {
                slot.IsEmpty = false;
                slot.Stack = new ItemStack { ItemId = itemId, Quantity = 1 };
                if (durable)
                {
                    int max = def.MaxDurability;
                    slot.Durability = durability > 0 ? (durability > max ? max : durability) : max;
                    slot.InstanceData = instanceData;
                }
                else
                {
                    // Stackables must keep zero instance bonuses.
                    slot.Durability = 0;
                    slot.InstanceData = default;
                }
                return true;
            }

            if (durable)
                return false;

            if (instanceData.HasAnyBonus)
                return false;

            if (slot.InstanceData.HasAnyBonus)
                return false;

            if (slot.Stack.ItemId != itemId)
                return false;

            if (slot.Stack.Quantity >= GetMaxStack(def))
                return false;

            slot.Stack.Quantity += 1;
            return true;
        }

        private static ItemInstanceData BuildInstanceData(ItemDef def, int bonusStrength, int bonusDexterity, int bonusIntelligence)
        {
            if (def == null)
                return default;

            // Keep instance bonuses only for equippable or tagged tool-like items.
            bool allowBonus = def.IsEquippable || (def.ToolTags != null && def.ToolTags.Length > 0);
            if (!allowBonus)
                return default;

            ItemInstanceData data;
            data.BonusStrength = bonusStrength;
            data.BonusDexterity = bonusDexterity;
            data.BonusIntelligence = bonusIntelligence;
            return data;
        }

        private static bool IsDurable(ItemDef def) => def != null && def.MaxDurability > 0;

        private static int GetMaxStack(ItemDef def)
        {
            if (def == null)
                return 1;

            if (def.MaxDurability > 0)
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
