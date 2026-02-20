using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// Server-side atomic checkout validator and applier.
    ///
    /// SECURITY / ATOMICITY RULES:
    /// - Validate everything before mutating anything.
    /// - Never remove stock by ItemId (must remove from the exact slot index requested).
    /// - Prevent duplicate SlotIndex lines from double-removing or double-pricing.
    /// </summary>
    public sealed class VendorTransactionService
    {
        public sealed class VendorContext
        {
            public VendorChestNet Chest;
            public PlayerNetworkRoot Seller;
        }

        public TransactionResult TryCheckout(PlayerNetworkRoot buyer, VendorContext vendor, CheckoutRequest request)
        {
            // Helper locals to build DTOs consistently
            TransactionResult Fail(FailureReason reason, int total = 0) => new TransactionResult
            {
                Result = new ActionResult { Success = false, Reason = reason },
                TotalPrice = total
            };

            TransactionResult Ok(int total) => new TransactionResult
            {
                Result = new ActionResult { Success = true, Reason = FailureReason.None },
                TotalPrice = total
            };

            if (buyer == null || vendor?.Chest == null || request.Lines == null || request.Lines.Length == 0)
                return Fail(FailureReason.InvalidRequest);

            var chestGrid = vendor.Chest.Grid;
            if (chestGrid == null)
                return Fail(FailureReason.VendorNotFound);

            // Aggregate quantities by SlotIndex to prevent duplicate-line exploits
            var requestedBySlot = new Dictionary<int, int>();
            foreach (var line in request.Lines)
            {
                if (line.Quantity <= 0)
                    return Fail(FailureReason.InvalidRequest);

                if (line.SlotIndex < 0 || line.SlotIndex >= chestGrid.Slots.Length)
                    return Fail(FailureReason.OutOfRange);

                if (requestedBySlot.TryGetValue(line.SlotIndex, out var existing))
                    requestedBySlot[line.SlotIndex] = existing + line.Quantity;
                else
                    requestedBySlot[line.SlotIndex] = line.Quantity;
            }

            // 1) Validate stock + compute total price (use SELLER base prices)
            var totalPrice = 0;

            foreach (var kvp in requestedBySlot)
            {
                var slotIndex = kvp.Key;
                var qty = kvp.Value;

                var slot = chestGrid.Slots[slotIndex];
                if (slot.IsEmpty || qty > slot.Stack.Quantity)
                    return Fail(FailureReason.OutOfStock);

                // Seller sets base price for items they sell (fallback = 1)
                var basePrice = vendor.Seller != null
                    ? vendor.Seller.KnownItems.GetBasePriceOrDefault(slot.Stack.ItemId, 1)
                    : 1;

                totalPrice += basePrice * qty;
            }

            // 2) Validate buyer can afford
            if (buyer.Wallet.Coins < totalPrice)
                return Fail(FailureReason.NotEnoughCoins, totalPrice);

            // 3) Validate buyer inventory space (again by aggregated quantities)
            foreach (var kvp in requestedBySlot)
            {
                var slot = chestGrid.Slots[kvp.Key];
                var qty = kvp.Value;

                if (!buyer.Inventory.Grid.CanAdd(slot.Stack.ItemId, qty, out _))
                    return Fail(FailureReason.NotEnoughInventorySpace, totalPrice);
            }

            // 4) Apply mutations (atomic apply phase)
            // Spend first is OK now that we validated space + stock.
            if (!buyer.Wallet.TrySpend(totalPrice))
                return Fail(FailureReason.NotEnoughCoins, totalPrice);

            vendor.Seller?.Wallet.AddCoins(totalPrice);

            // Remove stock from the exact slot index requested, then add to buyer
            foreach (var kvp in requestedBySlot)
            {
                var slotIndex = kvp.Key;
                var qty = kvp.Value;

                var slot = chestGrid.Slots[slotIndex];

                // Defensive: re-check slot is still valid at apply time
                if (slot.IsEmpty || qty > slot.Stack.Quantity)
                    return Fail(FailureReason.OutOfStock, totalPrice);

                // Slot-specific decrement (do NOT call Remove(itemId, qty)!)
                slot.Stack.Quantity -= qty;

                if (slot.Stack.Quantity <= 0)
                {
                    chestGrid.Slots[slotIndex] = new InventorySlot { IsEmpty = true };
                }
                else
                {
                    chestGrid.Slots[slotIndex] = slot;
                }

                buyer.Inventory.AddItemServer(slot.Stack.ItemId, qty);
                buyer.KnownItems.EnsureKnown(slot.Stack.ItemId);
            }

            // 5) XP awards (MVP)
            buyer.Skills.AddXp(SkillId.Negotiation, 1);
            vendor.Seller?.Skills.AddXp(SkillId.Sales, 1);

            // 6) Broadcast vendor chest update (so all shoppers see stock change)
            vendor.Chest.ForceBroadcastSnapshot();

            return Ok(totalPrice);
        }
    }
}
