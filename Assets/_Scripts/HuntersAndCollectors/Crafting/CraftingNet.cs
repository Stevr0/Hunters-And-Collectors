using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// Handles server-authoritative crafting request validation and inventory mutation.
    /// </summary>
    public sealed class CraftingNet : NetworkBehaviour
    {
        // Editor wiring checklist: attach to Player prefab and assign RecipeDatabase + PlayerInventoryNet + KnownItemsNet.
        [SerializeField] private RecipeDatabase recipeDatabase;
        [SerializeField] private PlayerInventoryNet inventory;
        [SerializeField] private KnownItemsNet knownItems;

        [ServerRpc(RequireOwnership = true)]
        public void RequestCraftServerRpc(string recipeId, int craftCount)
        {
            if (!IsServer || craftCount <= 0 || recipeDatabase == null) { CraftResultClientRpc(new CraftResult { Success = false, Reason = FailureReason.InvalidRequest }); return; }
            if (!recipeDatabase.TryGet(recipeId, out var recipe)) { CraftResultClientRpc(new CraftResult { Success = false, Reason = FailureReason.RecipeNotFound }); return; }

            foreach (var ingredient in recipe.Ingredients)
            {
                if (!inventory.Grid.CanRemove(ingredient.ItemId, ingredient.Quantity * craftCount)) { CraftResultClientRpc(new CraftResult { Success = false, Reason = FailureReason.MissingIngredients }); return; }
            }
            foreach (var output in recipe.Outputs)
            {
                if (!inventory.Grid.CanAdd(output.ItemId, output.Quantity * craftCount, out _)) { CraftResultClientRpc(new CraftResult { Success = false, Reason = FailureReason.NotEnoughInventorySpace }); return; }
            }

            foreach (var ingredient in recipe.Ingredients) inventory.Grid.Remove(ingredient.ItemId, ingredient.Quantity * craftCount);
            foreach (var output in recipe.Outputs)
            {
                inventory.AddItemServer(output.ItemId, output.Quantity * craftCount);
                knownItems.EnsureKnown(output.ItemId);
            }
            inventory.ForceSendSnapshotToOwner();
            CraftResultClientRpc(new CraftResult { Success = true, Reason = FailureReason.None });
        }

        [ClientRpc]
        private void CraftResultClientRpc(CraftResult result) { }
    }
}
