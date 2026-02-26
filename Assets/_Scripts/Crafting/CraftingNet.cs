using System;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Skills;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Crafting
{
    /// <summary>
    /// CraftingNet
    /// ------------------------------------------------------------
    /// Server-authoritative crafting endpoint with skill-based success.
    /// </summary>
    public sealed class CraftingNet : NetworkBehaviour
    {
        private const int MaxSkillLevel = 100;

        [Header("Database")]
        [SerializeField] private CraftingDatabase craftingDatabase;

        [Header("Limits")]
        [SerializeField, Range(1, 10)] private int maxCraftsPerRequest = 10;

        [Header("Progression")]
        [SerializeField, Min(1)] private int xpPerAttempt = 1;

        private PlayerInventoryNet inventoryNet;
        private SkillsNet skillsNet;
        private PlayerNetworkRoot playerRoot;

        public override void OnNetworkSpawn()
        {
            CacheComponents();
        }

        private void CacheComponents()
        {
            if (inventoryNet == null)
                inventoryNet = GetComponent<PlayerInventoryNet>();

            if (skillsNet == null)
                skillsNet = GetComponent<SkillsNet>();

            if (playerRoot == null)
                playerRoot = GetComponent<PlayerNetworkRoot>();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestCraftServerRpc(string recipeId, int craftCount = 1)
        {
            if (!IsServer)
                return;

            CacheComponents();

            if (inventoryNet == null || craftingDatabase == null || skillsNet == null)
                return;

            craftCount = Mathf.Clamp(craftCount, 1, maxCraftsPerRequest);

            if (!craftingDatabase.TryGet(recipeId, out var recipe) || recipe == null)
            {
                SendImmediateFailure(recipeId, CraftingCategory.Tools, FailureReason.RecipeNotFound, craftCount);
                return;
            }

            if (recipe.OutputItem == null || string.IsNullOrWhiteSpace(recipe.OutputItem.ItemId))
            {
                SendImmediateFailure(recipeId, recipe.Category, FailureReason.InvalidRequest, craftCount);
                return;
            }

            var skillId = ResolveSkillId(recipe.Category);
            if (string.IsNullOrEmpty(skillId))
            {
                SendImmediateFailure(recipeId, recipe.Category, FailureReason.InvalidRequest, craftCount);
                return;
            }

            craftCount = Mathf.Clamp(craftCount, 1, maxCraftsPerRequest);

            if (!HasIngredientsForAttempts(recipe, craftCount))
            {
                SendImmediateFailure(recipeId, recipe.Category, FailureReason.MissingIngredients, craftCount);
                return;
            }

            if (!CanFitOutputs(recipe, craftCount))
            {
                SendImmediateFailure(recipeId, recipe.Category, FailureReason.NotEnoughInventorySpace, craftCount);
                return;
            }

            inventoryNet.BeginServerBatch();

            try
            {
                for (int attemptIndex = 0; attemptIndex < craftCount; attemptIndex++)
                {
                    if (!ConsumeIngredients(recipe))
                    {
                        SendImmediateFailure(recipeId, recipe.Category, FailureReason.MissingIngredients, craftCount,
                            attemptIndex);
                        break;
                    }

                    int level = Mathf.Clamp(skillsNet.GetLevel(skillId), 0, MaxSkillLevel);
                    float chance = level / (float)MaxSkillLevel;
                    float roll = RollForAttempt(recipeId, attemptIndex);
                    bool success = roll <= chance;

                    skillsNet.AddXp(skillId, xpPerAttempt);

                    if (success)
                    {
                        int remainder = inventoryNet.ServerAddItem(
                            recipe.OutputItem.ItemId,
                            Mathf.Max(1, recipe.OutputQuantity));

                        if (remainder > 0)
                        {
                            Debug.LogWarning(
                                $"[CraftingNet] Output item '{recipe.OutputItem.ItemId}' did not fully fit. remainder={remainder}",
                                this);
                            success = false;
                        }
                    }

                    var result = BuildAttemptResult(
                        recipeId,
                        recipe.Category,
                        craftCount,
                        attemptIndex,
                        (byte)level,
                        chance,
                        roll,
                        success ? FailureReason.None : FailureReason.CraftFailed,
                        ingredientsConsumed: true,
                        success);

                    SendCraftResultToOwner(result);
                }
            }
            finally
            {
                inventoryNet.EndServerBatchAndSendSnapshotToOwner();
            }
        }

        private void SendImmediateFailure(string recipeId, CraftingCategory category, FailureReason reason, int attemptsRequested,
            int attemptIndex = 0)
        {
            var result = BuildAttemptResult(
                recipeId,
                category,
                attemptsRequested,
                attemptIndex,
                0,
                0f,
                1f,
                reason,
                ingredientsConsumed: false,
                success: false);

            SendCraftResultToOwner(result);
        }

        private CraftResult BuildAttemptResult(
            string recipeId,
            CraftingCategory category,
            int attemptsRequested,
            int attemptIndex,
            byte skillLevel,
            float chance,
            float roll,
            FailureReason reason,
            bool ingredientsConsumed,
            bool success)
        {
            return new CraftResult
            {
                Result = new ActionResult { Success = success, Reason = reason },
                RecipeId = new FixedString64Bytes(recipeId ?? string.Empty),
                Category = category,
                AttemptIndex = (byte)Mathf.Clamp(attemptIndex, 0, byte.MaxValue),
                AttemptsRequested = (byte)Mathf.Clamp(attemptsRequested, 0, byte.MaxValue),
                SkillLevel = skillLevel,
                RolledChance = chance,
                RollValue = roll,
                IngredientsConsumed = ingredientsConsumed
            };
        }

        private void SendCraftResultToOwner(CraftResult payload)
        {
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            CraftResultClientRpc(payload, rpcParams);
        }

        [ClientRpc]
        private void CraftResultClientRpc(CraftResult result, ClientRpcParams rpcParams = default)
        {
            // UI hook lives in client systems (MVP no-op).
        }

        private bool HasIngredientsForAttempts(CraftingRecipeDef recipe, int attemptCount)
        {
            if (recipe == null || inventoryNet == null)
                return false;

            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                var ing = recipe.Ingredients[i];
                if (ing.Item == null || string.IsNullOrWhiteSpace(ing.Item.ItemId))
                    return false;

                int required = Mathf.Max(1, ing.Quantity) * attemptCount;
                if (!inventoryNet.ServerHasItem(ing.Item.ItemId, required))
                    return false;
            }

            return true;
        }

        private bool ConsumeIngredients(CraftingRecipeDef recipe)
        {
            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                var ing = recipe.Ingredients[i];
                int qty = Mathf.Max(1, ing.Quantity);

                if (!inventoryNet.ServerRemoveItem(ing.Item.ItemId, qty))
                    return false;
            }

            return true;
        }

        private bool CanFitOutputs(CraftingRecipeDef recipe, int attemptCount)
        {
            if (recipe?.OutputItem == null || inventoryNet?.Grid == null)
                return false;

            int totalQuantity = Mathf.Max(1, recipe.OutputQuantity) * attemptCount;
            return inventoryNet.Grid.CanAdd(recipe.OutputItem.ItemId, totalQuantity, out var remainder) && remainder == 0;
        }

        private string ResolveSkillId(CraftingCategory category)
        {
            return category switch
            {
                CraftingCategory.Tools => SkillId.ToolCrafting,
                CraftingCategory.Equipment => SkillId.EquipmentCrafting,
                CraftingCategory.Building => SkillId.BuildingCrafting,
                _ => string.Empty
            };
        }

        private float RollForAttempt(string recipeId, int attemptIndex)
        {
            long serverTick = NetworkManager != null
                ? (long)NetworkManager.ServerTime.Tick
                : DateTime.UtcNow.Ticks;
            int playerKeyHash = playerRoot != null && !string.IsNullOrEmpty(playerRoot.PlayerKey)
                ? playerRoot.PlayerKey.GetHashCode(StringComparison.Ordinal)
                : OwnerClientId.GetHashCode();

            int seed = HashCode.Combine(playerKeyHash, attemptIndex,
                recipeId?.GetHashCode(StringComparison.Ordinal) ?? 0, (int)(serverTick & 0xFFFFFFFF));

            var rng = new System.Random(seed);
            return (float)rng.NextDouble();
        }
    }
}
