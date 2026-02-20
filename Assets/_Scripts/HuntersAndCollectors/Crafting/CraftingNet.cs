using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// Server-authoritative crafting validation + inventory mutation.
    /// </summary>
    public sealed class CraftingNet : NetworkBehaviour
    {
        [Header("Data")]
        [SerializeField] private RecipeDatabase recipeDatabase;

        private PlayerInventoryNet inventory;
        private KnownItemsNet knownItems;

        public override void OnNetworkSpawn()
        {
            // These live on the Player prefab
            inventory = GetComponent<PlayerInventoryNet>();
            knownItems = GetComponent<KnownItemsNet>();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestCraftServerRpc(string recipeId, int craftCount)
        {
            if (!IsServer)
                return;

            // Send result only to the requesting player (owner)
            var toOwner = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            // Basic validation
            if (craftCount <= 0 || recipeDatabase == null || inventory == null || knownItems == null)
            {
                CraftResultClientRpc(Fail(FailureReason.InvalidRequest), toOwner);
                return;
            }

            if (string.IsNullOrWhiteSpace(recipeId) || !recipeDatabase.TryGet(recipeId, out var recipe) || recipe == null)
            {
                CraftResultClientRpc(Fail(FailureReason.RecipeNotFound), toOwner);
                return;
            }

            // Validate ingredients exist
            foreach (var ingredient in recipe.Ingredients)
            {
                var need = ingredient.Quantity * craftCount;

                if (need <= 0 || !inventory.Grid.CanRemove(ingredient.ItemId, need))
                {
                    CraftResultClientRpc(Fail(FailureReason.MissingIngredients), toOwner);
                    return;
                }
            }

            // Validate outputs can fit
            foreach (var output in recipe.Outputs)
            {
                var give = output.Quantity * craftCount;

                if (give <= 0 || !inventory.Grid.CanAdd(output.ItemId, give, out _))
                {
                    CraftResultClientRpc(Fail(FailureReason.NotEnoughInventorySpace), toOwner);
                    return;
                }
            }

            // Apply: remove ingredients
            foreach (var ingredient in recipe.Ingredients)
            {
                var need = ingredient.Quantity * craftCount;
                inventory.Grid.Remove(ingredient.ItemId, need);
            }

            // Apply: add outputs
            foreach (var output in recipe.Outputs)
            {
                var give = output.Quantity * craftCount;

                inventory.AddItemServer(output.ItemId, give);
                knownItems.EnsureKnown(output.ItemId);
            }

            // Ensure owner UI updates
            inventory.ForceSendSnapshotToOwner();

            CraftResultClientRpc(Ok(), toOwner);
        }

        [ClientRpc]
        private void CraftResultClientRpc(CraftResult result, ClientRpcParams rpcParams = default)
        {
            // TODO: UI hook. Use:
            // result.Result.Success
            // result.Result.Reason
        }

        private static CraftResult Fail(FailureReason reason)
        {
            return new CraftResult
            {
                Result = new ActionResult { Success = false, Reason = reason }
            };
        }

        private static CraftResult Ok()
        {
            return new CraftResult
            {
                Result = new ActionResult { Success = true, Reason = FailureReason.None }
            };
        }
    }
}
