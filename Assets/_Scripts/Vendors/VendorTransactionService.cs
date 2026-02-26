using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;

namespace HuntersAndCollectors.Vendors
{
    public sealed class VendorTransactionService
    {
        public sealed class VendorContext
        {
            public VendorChestNet Chest;
            public PlayerNetworkRoot Seller; // optional (online)
        }

        // A deterministic “what we will buy from a slot” record.
        private readonly struct PlannedLine
        {
            public readonly int SlotIndex;
            public readonly string ItemId;
            public readonly int Qty;
            public readonly int UnitPrice;
            public readonly int LineTotal;

            public PlannedLine(int slotIndex, string itemId, int qty, int unitPrice, int lineTotal)
            {
                SlotIndex = slotIndex;
                ItemId = itemId;
                Qty = qty;
                UnitPrice = unitPrice;
                LineTotal = lineTotal;
            }
        }

        public TransactionResult TryCheckout(PlayerNetworkRoot buyer, VendorContext vendor, CheckoutRequest request)
        {
            // Helper DTO builders (keeps call sites consistent)
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
            // 0) Basic validation (NO allocations beyond small structures)
            // ---------------------------------------------------------
            if (buyer == null || vendor?.Chest == null || request.Lines == null || request.Lines.Length == 0)
                return Fail(FailureReason.InvalidRequest);

            // Production rule: server authoritative only.
            // If PlayerNetworkRoot isn't a NetworkBehaviour, replace with NetworkManager.Singleton.IsServer.
            if (!buyer.IsServer)
                return Fail(FailureReason.InvalidRequest);

            if (buyer.Inventory == null || buyer.Inventory.Grid == null || buyer.Wallet == null)
                return Fail(FailureReason.InvalidRequest);

            var chestGrid = vendor.Chest.Grid;
            if (chestGrid == null || chestGrid.Slots == null)
                return Fail(FailureReason.VendorNotFound);

            // ---------------------------------------------------------
            // 1) Aggregate request lines by SlotIndex (anti-exploit)
            //
            // Prevents:
            // - duplicate SlotIndex double-removing
            // - duplicate SlotIndex double-pricing
            // ---------------------------------------------------------
            var qtyBySlot = new Dictionary<int, int>(request.Lines.Length);

            for (int i = 0; i < request.Lines.Length; i++)
            {
                var line = request.Lines[i];

                if (line.Quantity <= 0)
                    return Fail(FailureReason.InvalidRequest);

                if (line.SlotIndex < 0 || line.SlotIndex >= chestGrid.Slots.Length)
                    return Fail(FailureReason.OutOfRange);

                if (qtyBySlot.TryGetValue(line.SlotIndex, out int existing))
                {
                    // Overflow-safe accumulate
                    long sum = (long)existing + line.Quantity;
                    if (sum > int.MaxValue)
                        return Fail(FailureReason.InvalidRequest);

                    qtyBySlot[line.SlotIndex] = (int)sum;
                }
                else
                {
                    qtyBySlot.Add(line.SlotIndex, line.Quantity);
                }
            }

            // ---------------------------------------------------------
            // 2) Deterministic commit order: sort slot indices
            //
            // Makes behavior stable regardless of client request line order.
            // ---------------------------------------------------------
            var slotIndices = new List<int>(qtyBySlot.Count);
            foreach (var kvp in qtyBySlot)
                slotIndices.Add(kvp.Key);

            slotIndices.Sort();

            // ---------------------------------------------------------
            // 3) Build plan + compute total (validate-only)
            //
            // IMPORTANT:
            // - Pricing is from vendor chest persistent table (seller may be offline).
            // - We store itemId observed at validate-time and enforce it at commit-time.
            // ---------------------------------------------------------
            var plan = new List<PlannedLine>(slotIndices.Count);
            int totalPrice = 0;

            for (int i = 0; i < slotIndices.Count; i++)
            {
                int slotIndex = slotIndices[i];
                int qty = qtyBySlot[slotIndex];

                var slot = chestGrid.Slots[slotIndex];

                if (slot.IsEmpty || qty > slot.Stack.Quantity)
                    return Fail(FailureReason.OutOfStock);

                string itemId = slot.Stack.ItemId;

                int unitPrice = vendor.Chest.GetBasePriceOrDefault(itemId, 1);
                if (unitPrice < 0)
                    return Fail(FailureReason.InvalidRequest);

                // Overflow-safe multiply and add
                if (!TryMulInt(unitPrice, qty, out int lineTotal))
                    return Fail(FailureReason.InvalidRequest);

                if (!TryAddInt(totalPrice, lineTotal, out totalPrice))
                    return Fail(FailureReason.InvalidRequest);

                plan.Add(new PlannedLine(slotIndex, itemId, qty, unitPrice, lineTotal));
            }

            // ---------------------------------------------------------
            // 4) Validate funds (server authoritative)
            // ---------------------------------------------------------
            if (buyer.Wallet.Coins < totalPrice)
                return Fail(FailureReason.NotEnoughCoins, totalPrice);

            // ---------------------------------------------------------
            // 5) Validate buyer inventory capacity for the entire plan
            // ---------------------------------------------------------
            for (int i = 0; i < plan.Count; i++)
            {
                var line = plan[i];

                if (!buyer.Inventory.Grid.CanAdd(line.ItemId, line.Qty, out _))
                    return Fail(FailureReason.NotEnoughInventorySpace, totalPrice);
            }

            // ---------------------------------------------------------
            // 6) COMMIT PHASE (mutations)
            //
            // Commit order chosen for your current APIs and “single snapshot” goal:
            // A) Remove chest stock from exact slots (re-check itemId + qty)
            // B) Debit buyer coins (TrySpend server-only)
            // C) Pay seller (or pend payout)
            // D) Add items to buyer inventory (batched snapshot)
            //
            // Why debit before Add?
            // - AddItemServer returns remainder but you have no shown Remove API.
            // - We already validated CanAdd, so Add should not fail.
            // - If coins fail (rare) we can abort before giving items.
            // ---------------------------------------------------------
            buyer.Inventory.BeginServerBatch();

            bool committed = false;

            try
            {
                // A) Remove stock slot-by-slot (never remove by itemId!)
                for (int i = 0; i < plan.Count; i++)
                {
                    var line = plan[i];

                    // Re-read slot at apply time (defensive against changes)
                    var slot = chestGrid.Slots[line.SlotIndex];

                    if (slot.IsEmpty)
                        return Fail(FailureReason.OutOfStock, totalPrice);

                    // Enforce the same item is still in that slot.
                    if (slot.Stack.ItemId != line.ItemId)
                        return Fail(FailureReason.OutOfStock, totalPrice);

                    // Enforce enough quantity still exists.
                    if (line.Qty > slot.Stack.Quantity)
                        return Fail(FailureReason.OutOfStock, totalPrice);

                    // Apply decrement
                    slot.Stack.Quantity -= line.Qty;

                    if (slot.Stack.Quantity <= 0)
                        chestGrid.Slots[line.SlotIndex] = new InventorySlot { IsEmpty = true };
                    else
                        chestGrid.Slots[line.SlotIndex] = slot;
                }

                // B) Debit coins server-side only
                if (!buyer.Wallet.TrySpend(totalPrice))
                    return Fail(FailureReason.NotEnoughCoins, totalPrice);

                // C) Pay seller (or pend)
                if (vendor.Seller != null && vendor.Seller.Wallet != null)
                    vendor.Seller.Wallet.AddCoins(totalPrice);
                else
                    vendor.Chest.AddPendingPayoutCoins(totalPrice);

                // D) Add items to buyer inventory (batch-aware)
                for (int i = 0; i < plan.Count; i++)
                {
                    var line = plan[i];

                    // With CanAdd validated, remainder should be 0.
                    // If non-zero, treat as atomicity failure.
                    int remainder = buyer.Inventory.AddItemServer(line.ItemId, line.Qty);
                    if (remainder != 0)
                        return Fail(FailureReason.NotEnoughInventorySpace, totalPrice);

                    buyer.KnownItems.EnsureKnown(line.ItemId);
                }

                // XP awards after commit so they cannot be exploited on failures
                buyer.Skills.AddXp(SkillId.Negotiation, 1);
                vendor.Seller?.Skills.AddXp(SkillId.Sales, 1);

                committed = true;

                // -----------------------------------------------------
                // 7) Broadcast snapshots EXACTLY ONCE each
                // -----------------------------------------------------
                vendor.Chest.ForceBroadcastSnapshot();               // one vendor snapshot
                buyer.Inventory.EndServerBatchAndSendSnapshotToOwner(); // one inventory snapshot

                return Ok(totalPrice);
            }
            finally
            {
                // If we failed before committed=true, we must end the batch without sending.
                // This prevents “failure paths” from sending snapshots.
                if (!committed)
                    buyer.Inventory.EndServerBatchWithoutSending();
            }
        }

        // Overflow-safe int math helpers
        private static bool TryMulInt(int a, int b, out int result)
        {
            long t = (long)a * (long)b;
            if (t < int.MinValue || t > int.MaxValue)
            {
                result = 0;
                return false;
            }
            result = (int)t;
            return true;
        }

        private static bool TryAddInt(int a, int b, out int result)
        {
            long t = (long)a + (long)b;
            if (t < int.MinValue || t > int.MaxValue)
            {
                result = 0;
                return false;
            }
            result = (int)t;
            return true;
        }
    }
}