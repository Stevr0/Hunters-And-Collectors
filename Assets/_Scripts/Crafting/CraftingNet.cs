using System;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Networking.DTO;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.Persistence;
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
    ///
    /// Important for item quality progression:
    /// - Crafted equippable/tool items can receive per-instance attribute bonuses.
    /// - Bonuses are rolled on the server only and stored in inventory slot instance data.
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

        [Header("Debug")]
        [SerializeField] private bool debugCraftTrace = true;

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

            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] Request recipeId={recipeId} craftCount={craftCount}");

            CacheComponents();

            if (inventoryNet == null || craftingDatabase == null || skillsNet == null)
            {
                if (debugCraftTrace)
                    Debug.Log("[Craft][SERVER] Craft failed reason=MissingDependencies");
                return;
            }

            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] InventoryShape width={inventoryNet.Grid?.Width ?? 0} height={inventoryNet.Grid?.Height ?? 0} slots={inventoryNet.Grid?.Slots?.Length ?? 0}");

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
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Craft failed reason=MissingIngredients recipeId={recipeId}");
                SendImmediateFailure(recipeId, recipe.Category, FailureReason.MissingIngredients, craftCount);
                return;
            }

            if (!CanFitOutputs(recipe, craftCount))
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Craft failed reason=NotEnoughInventorySpace recipeId={recipeId}");
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
                        BuildCraftingBonuses(recipe.OutputItem, level, out int bonusStr, out int bonusDex, out int bonusInt);
                        FixedString64Bytes craftedBy = ResolveCrafterName();

                        int remainder = inventoryNet.ServerAddItem(
                            recipe.OutputItem.ItemId,
                            Mathf.Max(1, recipe.OutputQuantity),
                            -1,
                            bonusStr,
                            bonusDex,
                            bonusInt,
                            craftedBy);
                        if (debugCraftTrace)
                            Debug.Log($"[Craft][SERVER] OutputAdd itemId={recipe.OutputItem.ItemId} qty={Mathf.Max(1, recipe.OutputQuantity)} remainder={remainder}");

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

                    if (debugCraftTrace)
                        Debug.Log($"[Craft][SERVER] Craft {(success ? "succeeded" : "failed")} recipeId={recipeId} attempt={attemptIndex}");

                    SendCraftResultToOwner(result);
                }
            }
            finally
            {
                inventoryNet.EndServerBatchAndSendSnapshotToOwner();
            }
        }

        private void BuildCraftingBonuses(Items.ItemDef outputItem, int skillLevel, out int bonusStrength, out int bonusDexterity, out int bonusIntelligence)
        {
            bonusStrength = 0;
            bonusDexterity = 0;
            bonusIntelligence = 0;

            if (outputItem == null)
                return;

            // Non-equippables/non-tools remain stackable and do not receive instance bonuses.
            bool canReceiveBonus = outputItem.IsEquippable || (outputItem.ToolTags != null && outputItem.ToolTags.Length > 0);
            if (!canReceiveBonus)
                return;

            int bonusPoints = Mathf.FloorToInt(Mathf.Clamp(skillLevel, 0, MaxSkillLevel) / 10f);
            if (bonusPoints <= 0)
                return;

            bool prefersStrength = HasToolTag(outputItem.ToolTags, Items.ToolTag.Axe)
                                  || HasToolTag(outputItem.ToolTags, Items.ToolTag.Pickaxe)
                                  || HasToolTag(outputItem.ToolTags, Items.ToolTag.Club);

            bool prefersDexterity = HasToolTag(outputItem.ToolTags, Items.ToolTag.Knife)
                                   || HasToolTag(outputItem.ToolTags, Items.ToolTag.Sickle);

            if (prefersStrength)
            {
                bonusStrength = bonusPoints;
                return;
            }

            if (prefersDexterity)
            {
                bonusDexterity = bonusPoints;
                return;
            }

            // Even split across STR/DEX/INT; any remainder goes to Strength.
            int per = bonusPoints / 3;
            int rem = bonusPoints % 3;

            bonusStrength = per + rem;
            bonusDexterity = per;
            bonusIntelligence = per;
        }

        private static bool HasToolTag(Items.ToolTag[] tags, Items.ToolTag wanted)
        {
            if (tags == null || tags.Length == 0)
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i] == wanted)
                    return true;
            }

            return false;
        }

        private FixedString64Bytes ResolveCrafterName()
        {
            // Replace with real display-name source when available.
            if (playerRoot != null && !string.IsNullOrWhiteSpace(playerRoot.PlayerKey))
                return new FixedString64Bytes(playerRoot.PlayerKey);

            return new FixedString64Bytes($"Player_{OwnerClientId}");
        }

        private void SendImmediateFailure(string recipeId, CraftingCategory category, FailureReason reason, int attemptsRequested,
            int attemptIndex = 0)
        {
            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] Craft failed reason={reason} recipeId={recipeId} attemptIndex={attemptIndex}");

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
            if (recipe == null || inventoryNet == null || inventoryNet.Grid == null)
                return false;

            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                var ing = recipe.Ingredients[i];
                if (ing.Item == null || string.IsNullOrWhiteSpace(ing.Item.ItemId))
                    return false;

                int required = Mathf.Max(1, ing.Quantity) * attemptCount;
                int found = CountItemInInventory(ing.Item.ItemId, out int foundInRow3);

                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] IngredientCheck itemId={ing.Item.ItemId} need={required} found={found} foundInRow3={foundInRow3}");

                if (found < required)
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
                bool removed = inventoryNet.ServerRemoveItem(ing.Item.ItemId, qty);

                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] IngredientRemove itemId={ing.Item.ItemId} qty={qty} success={removed}");

                if (!removed)
                    return false;
            }

            return true;
        }

        private bool CanFitOutputs(CraftingRecipeDef recipe, int attemptCount)
        {
            if (recipe?.OutputItem == null || inventoryNet?.Grid == null)
                return false;

            int totalQuantity = Mathf.Max(1, recipe.OutputQuantity) * attemptCount;
            bool result = inventoryNet.Grid.CanAdd(recipe.OutputItem.ItemId, totalQuantity, out var remainder) && remainder == 0;

            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] OutputFit itemId={recipe.OutputItem.ItemId} qty={totalQuantity} result={result} remainder={remainder}");

            return result;
        }


        private int CountItemInInventory(string itemId, out int foundInRow3)
        {
            foundInRow3 = 0;

            if (inventoryNet == null || inventoryNet.Grid == null || string.IsNullOrWhiteSpace(itemId))
                return 0;

            int count = 0;
            int width = Mathf.Max(1, inventoryNet.Grid.Width);
            int row3StartIndex = width * 3;
            InventorySlot[] slots = inventoryNet.Grid.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty)
                    continue;

                if (!string.Equals(slot.Stack.ItemId, itemId, StringComparison.Ordinal))
                    continue;

                count += slot.Stack.Quantity;
                if (i >= row3StartIndex)
                    foundInRow3 += slot.Stack.Quantity;
            }

            return count;
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



























