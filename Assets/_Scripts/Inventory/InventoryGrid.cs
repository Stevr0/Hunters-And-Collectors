using HuntersAndCollectors.Items;

namespace HuntersAndCollectors.Inventory
{
    /// <summary>
    /// Fixed-size slot grid with validated add/remove/move/split operations.
    /// </summary>
    public sealed class InventoryGrid
    {
        private readonly ItemDatabase itemDatabase;
        public int Width { get; }
        public int Height { get; }
        public InventorySlot[] Slots { get; }

        /// <summary>
        /// Creates a new grid with width*height slots initialized empty.
        /// </summary>
        public InventoryGrid(int width, int height, ItemDatabase database)
        {
            Width = width < 1 ? 1 : width;
            Height = height < 1 ? 1 : height;
            Slots = new InventorySlot[Width * Height];
            itemDatabase = database;
            for (var i = 0; i < Slots.Length; i++) Slots[i].IsEmpty = true;
        }

        /// <summary>
        /// Checks if quantity can be added and returns remainder if not all fits.
        /// </summary>
        public bool CanAdd(string itemId, int quantity, out int remainder)
        {
            remainder = quantity;
            if (quantity <= 0 || !itemDatabase.TryGet(itemId, out var def)) return false;
            var remaining = quantity;
            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (!Slots[i].IsEmpty && Slots[i].Stack.ItemId == itemId) remaining -= def.MaxStack - Slots[i].Stack.Quantity;
                if (Slots[i].IsEmpty) remaining -= def.MaxStack;
            }
            remainder = remaining > 0 ? remaining : 0;
            return remainder == 0;
        }

        /// <summary>
        /// Adds as much quantity as possible and returns leftover amount.
        /// </summary>
        public int Add(string itemId, int quantity)
        {
            if (quantity <= 0 || !itemDatabase.TryGet(itemId, out var def)) return quantity;
            var remaining = quantity;
            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (Slots[i].IsEmpty || Slots[i].Stack.ItemId != itemId) continue;
                var room = def.MaxStack - Slots[i].Stack.Quantity;
                if (room <= 0) continue;
                var moved = remaining < room ? remaining : room;
                Slots[i].Stack.Quantity += moved;
                remaining -= moved;
            }
            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (!Slots[i].IsEmpty) continue;
                var moved = remaining < def.MaxStack ? remaining : def.MaxStack;
                Slots[i].IsEmpty = false;
                Slots[i].Stack = new ItemStack { ItemId = itemId, Quantity = moved };
                remaining -= moved;
            }
            return remaining;
        }

        /// <summary>
        /// Returns whether the inventory contains at least quantity for item id.
        /// </summary>
        public bool CanRemove(string itemId, int quantity)
        {
            if (quantity <= 0) return false;
            var have = 0;
            foreach (var slot in Slots) if (!slot.IsEmpty && slot.Stack.ItemId == itemId) have += slot.Stack.Quantity;
            return have >= quantity;
        }

        /// <summary>
        /// Removes quantity for item id and returns true when successful.
        /// </summary>
        public bool Remove(string itemId, int quantity)
        {
            if (!CanRemove(itemId, quantity)) return false;
            var remaining = quantity;
            for (var i = 0; i < Slots.Length && remaining > 0; i++)
            {
                if (Slots[i].IsEmpty || Slots[i].Stack.ItemId != itemId) continue;
                var take = Slots[i].Stack.Quantity < remaining ? Slots[i].Stack.Quantity : remaining;
                Slots[i].Stack.Quantity -= take;
                remaining -= take;
                if (Slots[i].Stack.Quantity <= 0) Slots[i] = new InventorySlot { IsEmpty = true };
            }
            return true;
        }

        /// <summary>
        /// Moves, stacks, or swaps slots between two valid indices.
        /// </summary>
        public bool TryMoveSlot(int fromIndex, int toIndex)
        {
            if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex) || fromIndex == toIndex) return false;
            var from = Slots[fromIndex];
            var to = Slots[toIndex];
            if (from.IsEmpty) return false;
            if (to.IsEmpty) { Slots[toIndex] = from; Slots[fromIndex] = new InventorySlot { IsEmpty = true }; return true; }
            if (to.Stack.ItemId == from.Stack.ItemId && itemDatabase.TryGet(from.Stack.ItemId, out var def))
            {
                var room = def.MaxStack - to.Stack.Quantity;
                var move = room < from.Stack.Quantity ? room : from.Stack.Quantity;
                if (move <= 0) return false;
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

        /// <summary>
        /// Splits part of a stack into a free slot and returns new slot index.
        /// </summary>
        public bool TrySplitStack(int index, int splitAmount, out int newSlotIndex)
        {
            newSlotIndex = -1;
            if (!IsValidIndex(index) || splitAmount <= 0 || Slots[index].IsEmpty) return false;
            if (Slots[index].Stack.Quantity <= splitAmount) return false;
            for (var i = 0; i < Slots.Length; i++)
            {
                if (!Slots[i].IsEmpty) continue;
                Slots[index].Stack.Quantity -= splitAmount;
                Slots[i] = new InventorySlot { IsEmpty = false, Stack = new ItemStack { ItemId = Slots[index].Stack.ItemId, Quantity = splitAmount } };
                newSlotIndex = i;
                return true;
            }
            return false;
        }

        public bool TryAddOneToSlot(string itemId, int slotIndex)
        {
            if (!IsValidIndex(slotIndex)) return false;
            if (!itemDatabase.TryGet(itemId, out var def)) return false;

            ref var slot = ref Slots[slotIndex];

            if (slot.IsEmpty)
            {
                slot.IsEmpty = false;
                slot.Stack = new ItemStack { ItemId = itemId, Quantity = 1 };
                return true;
            }

            if (slot.Stack.ItemId != itemId) return false;
            if (slot.Stack.Quantity >= def.MaxStack) return false;

            slot.Stack.Quantity += 1;
            return true;
        }

        private bool IsValidIndex(int index) => index >= 0 && index < Slots.Length;
    }
}