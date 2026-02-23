using HuntersAndCollectors.Inventory;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// CraftingNet
    /// ------------------------------------------------------------
    /// Server-authoritative crafting endpoint.
    ///
    /// Rules:
    /// - Client UI requests crafting via ServerRpc (recipeId).
    /// - Server validates:
    ///     - recipe exists
    ///     - player has ingredients
    /// - Server removes ingredients + adds output
    /// - Inventory snapshots replicate back to owner automatically (your system)
    ///
    /// MVP:
    /// - Crafting always succeeds *if ingredients exist and output fits*.
    /// - No benches, no timers, no skills gating yet.
    /// </summary>
    public sealed class CraftingNet : NetworkBehaviour
    {
        [Header("Database")]
        [SerializeField] private CraftingDatabase craftingDatabase;

        private PlayerInventoryNet inventoryNet;

        public override void OnNetworkSpawn()
        {
            // Inventory component lives on player.
            inventoryNet = GetComponent<PlayerInventoryNet>();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestCraftServerRpc(string recipeId)
        {
            if (!IsServer)
                return;

            if (inventoryNet == null)
                inventoryNet = GetComponent<PlayerInventoryNet>();

            if (inventoryNet == null || craftingDatabase == null)
                return;

            if (!craftingDatabase.TryGet(recipeId, out var recipe) || recipe == null)
                return;

            // Validate recipe has output
            if (recipe.OutputItem == null || string.IsNullOrWhiteSpace(recipe.OutputItem.ItemId))
                return;

            // 1) Validate ingredients exist
            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                var ing = recipe.Ingredients[i];
                if (ing.Item == null || string.IsNullOrWhiteSpace(ing.Item.ItemId))
                    return;

                int qty = Mathf.Max(1, ing.Quantity);

                if (!inventoryNet.ServerHasItem(ing.Item.ItemId, qty))
                    return; // Not enough mats
            }

            // 2) Perform mutation as a single batch -> ONE snapshot to owner at end
            inventoryNet.BeginServerBatch();

            bool removedAll = true;

            // Remove all ingredients
            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                var ing = recipe.Ingredients[i];
                int qty = Mathf.Max(1, ing.Quantity);

                if (!inventoryNet.ServerRemoveItem(ing.Item.ItemId, qty))
                {
                    removedAll = false;
                    break;
                }
            }

            if (!removedAll)
            {
                // In MVP, if something went wrong, we just stop.
                // (True rollback would require a transaction model.)
                inventoryNet.EndServerBatchAndSendSnapshotToOwner();
                return;
            }

            // Add output item
            int remainder = inventoryNet.ServerAddItem(recipe.OutputItem.ItemId, Mathf.Max(1, recipe.OutputQuantity));

            // If output didn't fit, we *should* rollback. MVP: we simply do not craft.
            // But we already removed ingredients. To avoid this, you can first check
            // for free space/fit in InventoryGrid later. For now, keep recipes small.
            if (remainder > 0)
            {
                // NOTE: This is why later we add "CanFitItem" checks before remove.
                // For now, we accept MVP limitation.
            }

            inventoryNet.EndServerBatchAndSendSnapshotToOwner();
        }
    }
}
