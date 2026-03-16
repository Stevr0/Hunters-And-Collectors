using System;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Items;
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
    /// Server-authoritative crafting endpoint.
    ///
    /// Updated behavior:
    /// - Crafting never fails due to RNG once validation passes.
    /// - Non-stackable crafted gear uses server-rolled ItemInstance stats/durability.
    /// - Skill influences roll quality distribution (biased RNG profile).
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

        [Header("RNG Tuning")]
        [SerializeField, Tooltip("Low-skill exponent (higher means stronger bias toward low rolls).")]
        private float lowSkillExponent = 2.2f;

        [SerializeField, Tooltip("High-skill exponent (lower means better average rolls with variance).")]
        private float highSkillExponent = 0.65f;

        [SerializeField, Range(0f, 0.25f), Tooltip("Per-stat variation around the shared craft profile score.")]
        private float perStatVariance = 0.08f;

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
            {
                if (debugCraftTrace)
                    Debug.Log("[Craft][SERVER] Reject reason=NotServerContext");
                return;
            }

            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] Request recipeId={recipeId} craftCount={craftCount}");

            CacheComponents();

            if (inventoryNet == null || craftingDatabase == null || skillsNet == null)
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Craft failed reason=MissingDependencies invNull={inventoryNet == null} dbNull={craftingDatabase == null} skillsNull={skillsNet == null}");

                SendImmediateFailure(recipeId, CraftingCategory.Tools, FailureReason.InvalidRequest, craftCount, 0, "MissingDependencies");
                return;
            }

            craftCount = Mathf.Clamp(craftCount, 1, maxCraftsPerRequest);

            if (!craftingDatabase.TryGet(recipeId, out var recipe) || recipe == null)
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Validation failed reason=RecipeNotFound recipeId={recipeId}");

                SendImmediateFailure(recipeId, CraftingCategory.Tools, FailureReason.RecipeNotFound, craftCount, 0, "RecipeNotFound");
                return;
            }

            if (recipe.OutputItem == null || string.IsNullOrWhiteSpace(recipe.OutputItem.ItemId))
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Validation failed reason=InvalidOutputItem recipeId={recipeId}");

                SendImmediateFailure(recipeId, recipe.Category, FailureReason.InvalidRequest, craftCount, 0, "InvalidOutputItem");
                return;
            }

            if (!inventoryNet.ServerTryGetItemDef(recipe.OutputItem.ItemId, out ItemDef outputDef) || outputDef == null)
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Validation failed reason=OutputItemNotInDatabase itemId={recipe.OutputItem.ItemId} recipeId={recipeId}");

                SendImmediateFailure(recipeId, recipe.Category, FailureReason.InvalidRequest, craftCount, 0, "OutputItemNotInDatabase");
                return;
            }

            if (!ValidateCraftingStation(recipe, out string stationFailureReason))
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Validation failed reason=StationValidationFailed details={stationFailureReason} recipeId={recipeId}");

                SendImmediateFailure(recipeId, recipe.Category, FailureReason.InvalidRequest, craftCount, 0, stationFailureReason);
                return;
            }

            var skillId = ResolveSkillId(recipe.Category);
            if (string.IsNullOrEmpty(skillId))
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Validation failed reason=UnknownSkillMapping category={recipe.Category} recipeId={recipeId}");

                SendImmediateFailure(recipeId, recipe.Category, FailureReason.InvalidRequest, craftCount, 0, "UnknownSkillMapping");
                return;
            }

            if (!HasIngredientsForAttempts(recipe, craftCount))
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Craft failed reason=MissingIngredients recipeId={recipeId}");
                SendImmediateFailure(recipeId, recipe.Category, FailureReason.MissingIngredients, craftCount, 0, "MissingIngredients");
                return;
            }

            if (!CanFitOutputs(recipe, craftCount))
            {
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] Craft failed reason=NotEnoughInventorySpace recipeId={recipeId}");
                SendImmediateFailure(recipeId, recipe.Category, FailureReason.NotEnoughInventorySpace, craftCount, 0, "NotEnoughInventorySpace");
                return;
            }

            inventoryNet.BeginServerBatch();

            try
            {
                int level = Mathf.Clamp(skillsNet.GetLevel(skillId), 0, MaxSkillLevel);
                for (int attemptIndex = 0; attemptIndex < craftCount; attemptIndex++)
                {
                    if (!ConsumeIngredients(recipe))
                    {
                        SendImmediateFailure(recipeId, recipe.Category, FailureReason.MissingIngredients, craftCount, attemptIndex, "ConsumeIngredientsFailed");
                        break;
                    }

                    bool added = AddCraftOutput(recipe, level, recipeId, attemptIndex);
                    if (!added)
                    {
                        SendImmediateFailure(recipeId, recipe.Category, FailureReason.NotEnoughInventorySpace, craftCount, attemptIndex, "AddOutputFailed");
                        break;
                    }

                    skillsNet.AddXp(skillId, xpPerAttempt);

                    if (debugCraftTrace)
                        Debug.Log($"[Craft][SERVER] Craft succeeded recipeId={recipeId} attempt={attemptIndex}");

                    var result = BuildAttemptResult(
                        recipeId,
                        recipe.Category,
                        craftCount,
                        attemptIndex,
                        (byte)level,
                        chance: 1f,
                        roll: 0f,
                        reason: FailureReason.None,
                        ingredientsConsumed: true,
                        success: true);

                    SendCraftResultToOwner(result);
                }
            }
            finally
            {
                inventoryNet.EndServerBatchAndSendSnapshotToOwner();
            }
        }

        private bool AddCraftOutput(CraftingRecipeDef recipe, int skillLevel, string recipeId, int attemptIndex)
        {
            if (recipe == null || recipe.OutputItem == null)
                return false;

            int quantity = Mathf.Max(1, recipe.OutputQuantity);
            bool isInstanceOutput = recipe.OutputItem.UsesItemInstance;

            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] OutputMode itemId={recipe.OutputItem.ItemId} usesInstance={isInstanceOutput} qty={quantity}");

            if (!isInstanceOutput)
            {
                int remainder = inventoryNet.ServerAddItem(recipe.OutputItem.ItemId, quantity);
                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] OutputAdd stack itemId={recipe.OutputItem.ItemId} qty={quantity} remainder={remainder}");

                return remainder == 0;
            }

            FixedString64Bytes craftedBy = ResolveCrafterName();
            for (int i = 0; i < quantity; i++)
            {
                ItemInstance instance = RollCraftedInstance(recipe.OutputItem, skillLevel, recipeId, attemptIndex, i, craftedBy, out ItemInstanceData data);

                if (!inventoryNet.ServerTryAddItemInstance(instance, data))
                {
                    if (debugCraftTrace)
                        Debug.Log($"[Craft][SERVER] OutputAdd failed mode=Instance itemId={instance.ItemId} instanceId={instance.InstanceId} ordinal={i}");
                    return false;
                }
            }

            return true;
        }

        private ItemInstance RollCraftedInstance(ItemDef outputItem, int skillLevel, string recipeId, int attemptIndex, int outputOrdinal, FixedString64Bytes craftedBy, out ItemInstanceData data)
        {
            // One coherent craft profile score keeps stats feeling consistently weak/average/strong.
            // Skill shifts the score distribution without removing variance entirely.
            System.Random rng = BuildRng(recipeId, attemptIndex, outputOrdinal);
            float skill01 = Mathf.Clamp01(skillLevel / (float)MaxSkillLevel);
            float exponent = Mathf.Lerp(lowSkillExponent, highSkillExponent, skill01);
            float profileScore = Mathf.Pow((float)rng.NextDouble(), exponent);

            float RollAroundProfile(float min, float max)
            {
                if (max < min)
                    max = min;

                if (Mathf.Approximately(min, max))
                    return min;

                float jitter = ((float)rng.NextDouble() * 2f - 1f) * perStatVariance;
                float t = Mathf.Clamp01(profileScore + jitter);
                return Mathf.Lerp(min, max, t);
            }

            int RollDurability(int min, int max)
            {
                if (max < min)
                    max = min;

                if (min <= 0 && max <= 0)
                    return 0;

                min = Mathf.Max(1, min);
                max = Mathf.Max(min, max);

                float jitter = ((float)rng.NextDouble() * 2f - 1f) * perStatVariance;
                float t = Mathf.Clamp01(profileScore + jitter);
                return Mathf.RoundToInt(Mathf.Lerp(min, max, t));
            }

            float rolledDamage = RollAroundProfile(outputItem.ResolveDamageMin(), outputItem.ResolveDamageMax());
            float rolledDefence = RollAroundProfile(outputItem.ResolveDefenceMin(), outputItem.ResolveDefenceMax());
            float rolledSwing = RollAroundProfile(outputItem.ResolveSwingSpeedMin(), outputItem.ResolveSwingSpeedMax());
            float rolledMove = RollAroundProfile(outputItem.ResolveMovementSpeedMin(), outputItem.ResolveMovementSpeedMax());
            float rolledCastSpeed = RollAroundProfile(outputItem.ResolveCastSpeedMin(), outputItem.ResolveCastSpeedMax());
            int rolledBlockValue = RollDurability(outputItem.ResolveBlockValueMin(), outputItem.ResolveBlockValueMax());
            int rolledMaxDurability = RollDurability(outputItem.ResolveDurabilityMin(), outputItem.ResolveDurabilityMax());

            ItemInstance instance = new ItemInstance
            {
                InstanceId = ComposeInstanceId(recipeId, attemptIndex, outputOrdinal),
                ItemId = outputItem.ItemId,
                RolledDamage = rolledDamage,
                RolledDefence = rolledDefence,
                RolledSwingSpeed = rolledSwing,
                RolledMovementSpeed = rolledMove,
                RolledCastSpeed = rolledCastSpeed,
                RolledBlockValue = rolledBlockValue,
                MaxDurability = rolledMaxDurability,
                CurrentDurability = rolledMaxDurability
            };

            data = ItemInstanceUtility.CreateFromInstance(instance, craftedBy);
            CraftedCombatRollService.ApplyCraftedModifiers(outputItem, profileScore, skill01, rng, ref instance, ref data);
            ItemInstanceUtility.MirrorRuntimeFields(ref data, instance);

            if (debugCraftTrace)
            {
                Debug.Log($"[Craft][SERVER] InstanceRoll itemId={outputItem.ItemId} skill={skillLevel} tier={outputItem.ItemTier} profile={profileScore:0.000} dmg={instance.RolledDamage:0.##} def={instance.RolledDefence:0.##} swing={instance.RolledSwingSpeed:0.##} move={instance.RolledMovementSpeed:0.##} cast={instance.RolledCastSpeed:0.##} block={instance.RolledBlockValue} maxDur={instance.MaxDurability} affixes={data.AffixA}/{data.AffixB}/{data.AffixC} resist={data.ResistanceAffix}");
            }

            return instance;
        }

        private System.Random BuildRng(string recipeId, int attemptIndex, int outputOrdinal)
        {
            long serverTick = NetworkManager != null
                ? (long)NetworkManager.ServerTime.Tick
                : DateTime.UtcNow.Ticks;

            int playerKeyHash = playerRoot != null && !string.IsNullOrEmpty(playerRoot.PlayerKey)
                ? playerRoot.PlayerKey.GetHashCode(StringComparison.Ordinal)
                : OwnerClientId.GetHashCode();

            int recipeHash = recipeId?.GetHashCode(StringComparison.Ordinal) ?? 0;
            int seed = HashCode.Combine(playerKeyHash, recipeHash, attemptIndex, outputOrdinal, (int)(serverTick & 0xFFFFFFFF));
            return new System.Random(seed);
        }

        private long ComposeInstanceId(string recipeId, int attemptIndex, int outputOrdinal)
        {
            int recipeHash = recipeId?.GetHashCode(StringComparison.Ordinal) ?? 0;
            int ownerHash = (int)(OwnerClientId & 0xFFFF);
            long ticks = DateTime.UtcNow.Ticks & 0x00FFFFFFFFFFFFFF;
            long low = ((long)(attemptIndex & 0xFF) << 56) | ((long)(outputOrdinal & 0xFF) << 48) | ((long)(ownerHash & 0xFFFF) << 32) | (uint)recipeHash;
            return ticks ^ low;
        }

        private FixedString64Bytes ResolveCrafterName()
        {
            if (playerRoot != null && !string.IsNullOrWhiteSpace(playerRoot.PlayerKey))
                return new FixedString64Bytes(playerRoot.PlayerKey);

            return new FixedString64Bytes($"Player_{OwnerClientId}");
        }

        private void SendImmediateFailure(string recipeId, CraftingCategory category, FailureReason reason, int attemptsRequested,
            int attemptIndex = 0, string detail = "")
        {
            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] Craft failed reason={reason} detail={detail} recipeId={recipeId} attemptIndex={attemptIndex}");

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
            {
                if (debugCraftTrace)
                    Debug.Log("[Craft][SERVER] IngredientCheck failed reason=MissingRecipeOrInventory");
                return false;
            }

            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                var ing = recipe.Ingredients[i];
                if (ing.Item == null || string.IsNullOrWhiteSpace(ing.Item.ItemId))
                {
                    if (debugCraftTrace)
                        Debug.Log($"[Craft][SERVER] IngredientCheck failed reason=InvalidIngredient index={i}");
                    return false;
                }

                bool requiresInstanceIngredient = ing.Item.UsesItemInstance;
                int required = Mathf.Max(1, ing.Quantity) * attemptCount;
                int found = CountItemInInventory(ing.Item.ItemId, requiresInstanceIngredient, out int foundInRow3);

                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] IngredientCheck itemId={ing.Item.ItemId} requiresInstance={requiresInstanceIngredient} need={required} found={found} foundInRow3={foundInRow3}");

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
                if (ing.Item == null || string.IsNullOrWhiteSpace(ing.Item.ItemId))
                    return false;

                int qty = Mathf.Max(1, ing.Quantity);
                bool requiresInstanceIngredient = ing.Item.UsesItemInstance;
                bool removed = requiresInstanceIngredient
                    ? RemoveInstanceIngredients(ing.Item.ItemId, qty)
                    : RemoveStackIngredients(ing.Item.ItemId, qty);

                if (debugCraftTrace)
                    Debug.Log($"[Craft][SERVER] IngredientRemove itemId={ing.Item.ItemId} requiresInstance={requiresInstanceIngredient} qty={qty} success={removed}");

                if (!removed)
                    return false;
            }

            return true;
        }

        private bool CanFitOutputs(CraftingRecipeDef recipe, int attemptCount)
        {
            if (recipe?.OutputItem == null || inventoryNet?.Grid == null)
            {
                if (debugCraftTrace)
                    Debug.Log("[Craft][SERVER] OutputFit failed reason=MissingOutputOrInventory");
                return false;
            }

            int totalQuantity = Mathf.Max(1, recipe.OutputQuantity) * attemptCount;
            bool result = inventoryNet.Grid.CanAdd(recipe.OutputItem.ItemId, totalQuantity, out var remainder) && remainder == 0;

            if (debugCraftTrace)
                Debug.Log($"[Craft][SERVER] OutputFit itemId={recipe.OutputItem.ItemId} qty={totalQuantity} result={result} remainder={remainder}");

            return result;
        }

        private int CountItemInInventory(string itemId, bool requiresInstanceIngredient, out int foundInRow3)
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

                if (requiresInstanceIngredient)
                {
                    if (slot.ContentType != InventorySlotContentType.Instance)
                        continue;

                    if (!string.Equals(slot.Instance.ItemId, itemId, StringComparison.Ordinal))
                        continue;

                    count += 1;
                    if (i >= row3StartIndex)
                        foundInRow3 += 1;
                }
                else
                {
                    if (slot.ContentType != InventorySlotContentType.Stack)
                        continue;

                    if (!string.Equals(slot.Stack.ItemId, itemId, StringComparison.Ordinal))
                        continue;

                    int qty = Mathf.Max(0, slot.Stack.Quantity);
                    count += qty;
                    if (i >= row3StartIndex)
                        foundInRow3 += qty;
                }
            }

            return count;
        }

        private bool RemoveStackIngredients(string itemId, int quantity)
        {
            if (quantity <= 0 || inventoryNet == null || inventoryNet.Grid == null)
                return false;

            int remaining = quantity;
            while (remaining > 0)
            {
                int stackSlotIndex = FindFirstMatchingStackSlot(itemId);
                if (stackSlotIndex < 0)
                    return false;

                if (!inventoryNet.ServerRemoveOneAtSlot(stackSlotIndex, out _, out _, out _))
                    return false;

                remaining--;
            }

            return true;
        }

        private bool RemoveInstanceIngredients(string itemId, int quantity)
        {
            if (quantity <= 0 || inventoryNet == null || inventoryNet.Grid == null)
                return false;

            int remaining = quantity;
            while (remaining > 0)
            {
                int instanceSlotIndex = FindFirstMatchingInstanceSlot(itemId);
                if (instanceSlotIndex < 0)
                    return false;

                if (!inventoryNet.ServerRemoveOneAtSlot(instanceSlotIndex, out _, out _, out _))
                    return false;

                remaining--;
            }

            return true;
        }

        private int FindFirstMatchingStackSlot(string itemId)
        {
            InventorySlot[] slots = inventoryNet.Grid.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty || slot.ContentType != InventorySlotContentType.Stack)
                    continue;

                if (!string.Equals(slot.Stack.ItemId, itemId, StringComparison.Ordinal))
                    continue;

                if (slot.Stack.Quantity <= 0)
                    continue;

                return i;
            }

            return -1;
        }

        private int FindFirstMatchingInstanceSlot(string itemId)
        {
            InventorySlot[] slots = inventoryNet.Grid.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                InventorySlot slot = slots[i];
                if (slot.IsEmpty || slot.ContentType != InventorySlotContentType.Instance)
                    continue;

                if (!string.Equals(slot.Instance.ItemId, itemId, StringComparison.Ordinal))
                    continue;

                return i;
            }

            return -1;
        }


        /// <summary>
        /// First-pass station validation hook.
        ///
        /// Current implementation is permissive because recipe defs do not yet encode
        /// station requirements in this project. Keeping this explicit hook prevents
        /// hidden assumptions and gives us a clear place to enforce station rules later.
        /// </summary>
        private bool ValidateCraftingStation(CraftingRecipeDef recipe, out string failureReason)
        {
            failureReason = string.Empty;
            return true;
        }
        private string ResolveSkillId(CraftingCategory category)
        {
            return category switch
            {
                CraftingCategory.Tools => SkillId.ToolCrafting,
                CraftingCategory.Equipment => SkillId.EquipmentCrafting,
                CraftingCategory.Building => SkillId.BuildingCrafting,
                // Consumables currently share the general hand-crafting progression track.
                CraftingCategory.Consumables => SkillId.ToolCrafting,
                _ => string.Empty
            };
        }
    }
}



