using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;

namespace HuntersAndCollectors.Vendors
{
    /// <summary>
    /// Server-side atomic checkout validator and applier.
    /// </summary>
    public sealed class VendorTransactionService
    {
        public sealed class VendorContext
        {
            public VendorChestNet Chest;
            public PlayerNetworkRoot Seller;
        }

        /// <summary>
        /// Attempts an atomic checkout and returns success/failure details.
        /// </summary>
        public TransactionResult TryCheckout(PlayerNetworkRoot buyer, VendorContext vendor, CheckoutRequest request)
        {
            if (buyer == null || vendor?.Chest == null || request.Lines == null || request.Lines.Length == 0)
                return new TransactionResult { Success = false, Reason = FailureReason.InvalidRequest };

            var chestGrid = vendor.Chest.Grid;
            if (chestGrid == null) return new TransactionResult { Success = false, Reason = FailureReason.VendorNotFound };

            var totalPrice = 0;
            foreach (var line in request.Lines)
            {
                if (line.Quantity <= 0 || line.SlotIndex < 0 || line.SlotIndex >= chestGrid.Slots.Length) return new TransactionResult { Success = false, Reason = FailureReason.OutOfRange };
                var slot = chestGrid.Slots[line.SlotIndex];
                if (slot.IsEmpty || line.Quantity > slot.Stack.Quantity) return new TransactionResult { Success = false, Reason = FailureReason.OutOfStock };
                totalPrice += buyer.KnownItems.GetBasePriceOrDefault(slot.Stack.ItemId, 1) * line.Quantity;
            }

            if (buyer.Wallet.Coins < totalPrice) return new TransactionResult { Success = false, Reason = FailureReason.NotEnoughCoins, TotalPrice = totalPrice };

            // Validate inventory fit before mutation for atomicity.
            foreach (var line in request.Lines)
            {
                var slot = chestGrid.Slots[line.SlotIndex];
                if (!buyer.Inventory.Grid.CanAdd(slot.Stack.ItemId, line.Quantity, out _)) return new TransactionResult { Success = false, Reason = FailureReason.NotEnoughInventorySpace, TotalPrice = totalPrice };
            }

            if (!buyer.Wallet.TrySpend(totalPrice)) return new TransactionResult { Success = false, Reason = FailureReason.NotEnoughCoins, TotalPrice = totalPrice };
            vendor.Seller?.Wallet.AddCoins(totalPrice);

            foreach (var line in request.Lines)
            {
                var slot = chestGrid.Slots[line.SlotIndex];
                chestGrid.Remove(slot.Stack.ItemId, line.Quantity);
                buyer.Inventory.AddItemServer(slot.Stack.ItemId, line.Quantity);
                buyer.KnownItems.EnsureKnown(slot.Stack.ItemId);
            }

            buyer.Skills.AddXp(SkillId.Negotiation, 1);
            vendor.Seller?.Skills.AddXp(SkillId.Sales, 1);
            vendor.Chest.ForceBroadcastSnapshot();
            return new TransactionResult { Success = true, Reason = FailureReason.None, TotalPrice = totalPrice };
        }
    }
}
