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
    ///
    /// OFFLINE-SELLER RULE:
    /// - Seller may be null (offline).
    /// - Base price must come from the VendorChest (persistent pricing), not from seller components.
    /// - Seller payout must still happen via chest "pending payout" if seller is offline.
    /// </summary>
    public sealed class VendorTransactionService
    {
        public sealed class VendorContext
        {
            public VendorChestNet Chest;

            // Optional: only present if seller is online and you resolved them.
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

            // ---------------------------------------------------------
            // 0) Basic validation (no allocations / no mutations)
            // ---------------------------------------------------------
            if (buyer == null || vendor?.Chest == null || request.Lines == null || request.Lines.Length == 0)
                return Fail(FailureReason.InvalidRequest);

            if (buyer.Inventory == null || buyer.Inventory.Grid == null)
                return Fail(FailureReason.InvalidRequest); // buyer not initialized properly

            if (buyer.Wallet == null)
                return Fail(FailureReason.InvalidRequest);

            var chestGrid = vendor.Chest.Grid;
            if (chestGrid == null)
                return Fail(FailureReason.VendorNotFound);

            // ---------------------------------------------------------
            // 1) Aggregate quantities by SlotIndex (anti-exploit)
            // ---------------------------------------------------------
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

            // ---------------------------------------------------------
            // 2) Validate stock + compute total price
            //
            // IMPORTANT CHANGE:
            // - Base price comes from the VENDOR CHEST (persistent table),
            //   NOT from seller.KnownItems (seller might be offline).
            // ---------------------------------------------------------
            var totalPrice = 0;

            foreach (var kvp in requestedBySlot)
            {
                var slotIndex = kvp.Key;
                var qty = kvp.Value;

                var slot = chestGrid.Slots[slotIndex];
                if (slot.IsEmpty || qty > slot.Stack.Quantity)
                    return Fail(FailureReason.OutOfStock);

                // Persistent base price owned by the vendor chest.
                // Fallback = 1 coin (tune later).
                var basePrice = vendor.Chest.GetBasePriceOrDefault(slot.Stack.ItemId, 1);

                totalPrice += basePrice * qty;
            }

            // ---------------------------------------------------------
            // 3) Validate buyer can afford
            // ---------------------------------------------------------
            if (buyer.Wallet.Coins < totalPrice)
                return Fail(FailureReason.NotEnoughCoins, totalPrice);

            // ---------------------------------------------------------
            // 4) Validate buyer inventory space (atomicity check)
            // ---------------------------------------------------------
            foreach (var kvp in requestedBySlot)
            {
                var slot = chestGrid.Slots[kvp.Key];
                var qty = kvp.Value;

                if (!buyer.Inventory.Grid.CanAdd(slot.Stack.ItemId, qty, out _))
                    return Fail(FailureReason.NotEnoughInventorySpace, totalPrice);
            }

            // ---------------------------------------------------------
            // 5) APPLY PHASE (mutations start here)
            // ---------------------------------------------------------

            // Spend first is OK because we already validated stock + space.
            if (!buyer.Wallet.TrySpend(totalPrice))
                return Fail(FailureReason.NotEnoughCoins, totalPrice);

            // Pay seller:
            // - If seller is online, credit their wallet immediately.
            // - If seller is offline, store pending payout on the chest (persisted on shard).
            if (vendor.Seller != null && vendor.Seller.Wallet != null)
            {
                vendor.Seller.Wallet.AddCoins(totalPrice);
            }
            else
            {
                vendor.Chest.AddPendingPayoutCoins(totalPrice);
            }

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

                // Add to buyer inventory + mark as known
                buyer.Inventory.AddItemServer(slot.Stack.ItemId, qty);
                buyer.KnownItems.EnsureKnown(slot.Stack.ItemId);
            }

            // ---------------------------------------------------------
            // 6) XP awards (MVP)
            // ---------------------------------------------------------
            buyer.Skills.AddXp(SkillId.Negotiation, 1);

            // Only award Sales XP if seller exists (online).
            // If you want offline sales XP too, that XP must be stored on vendor data (like pending payout).
            vendor.Seller?.Skills.AddXp(SkillId.Sales, 1);

            // ---------------------------------------------------------
            // 7) Broadcast vendor chest update
            // ---------------------------------------------------------
            vendor.Chest.ForceBroadcastSnapshot();

            return Ok(totalPrice);
        }
    }
}
