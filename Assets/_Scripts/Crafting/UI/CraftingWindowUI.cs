using System.Collections.Generic;
using HuntersAndCollectors.Crafting;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.UI;
using HuntersAndCollectors.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// CraftingWindowUI
    /// ------------------------------------------------------------
    /// Client-side crafting window.
    ///
    /// Authority:
    /// - UI NEVER crafts directly.
    /// - UI calls CraftingNet ServerRpc to request crafting.
    ///
    /// UI Requirements in Unity:
    /// - Root panel GameObject assigned (to show/hide).
    /// - 4 tab buttons (Tools/Equipment/Building/Consumables).
    /// - A container transform for recipe buttons.
    /// - A "recipe details" panel (icon, name, description, properties, ingredients grid, craft button).
    ///
    /// Ingredients UI:
    /// - Uses a fixed 1x5 grid controller (IngredientRowUI) that fills 5 slots.
    /// - No instantiation at runtime for ingredient UI.
    /// </summary>
    public sealed class CraftingWindowUI : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject root;
        [SerializeField] private bool startHidden = true;

        [Header("Tabs")]
        [SerializeField] private Button toolsButton;
        [SerializeField] private Button equipmentButton;
        [SerializeField] private Button buildingButton;
        [SerializeField] private Button consumablesButton;

        [Header("Recipe List")]
        [SerializeField] private Transform recipeListRoot;
        [SerializeField] private RecipeListItemUI recipeListItemPrefab;

        [Header("Details")]
        [SerializeField] private TMP_Text recipeNameText;
        [SerializeField] private Image detailsIconImage;
        [SerializeField] private TMP_Text detailsDescriptionText;
        [SerializeField] private TMP_Text detailsPropertiesText;
        [SerializeField] private Button craftButton;

        [Header("Ingredients Grid (1x5)")]
        [SerializeField] private IngredientRowUI ingredientsGrid;

        [Header("Data")]
        [SerializeField] private CraftingDatabase craftingDatabase;

        [Header("Debug")]
        [SerializeField] private bool debugCraftAvailability = true;

        private CraftingCategory _activeCategory = CraftingCategory.Tools;
        private CraftingRecipeDef _selectedRecipe;

        // Owner-only references (set when local player spawns)
        private PlayerInventoryNet _inventoryNet;
        private CraftingNet _craftingNet;
        private KnownItemsNet _knownItemsNet;

        private readonly List<RecipeListItemUI> _spawnedRecipeItems = new();
        private bool _bound;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            if (root == null) root = gameObject;
            if (startHidden) root.SetActive(false);

            // Backward-compatible scene repair:
            // older scene instances predate the serialized consumables button field,
            // so we resolve it by name when the inspector reference is missing.
            TryResolveMissingTabButtons();

            // Tabs
            if (toolsButton != null) toolsButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Tools));
            if (equipmentButton != null) equipmentButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Equipment));
            if (buildingButton != null) buildingButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Building));
            if (consumablesButton != null) consumablesButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Consumables));

            // Craft
            if (craftButton != null) craftButton.onClick.AddListener(OnCraftClicked);
        }

        private void OnEnable()
        {
            // Centralized UIWindowController may open this panel by toggling the root GameObject
            // directly, which does NOT call our Open() method.
            // To keep behavior consistent and avoid stale disabled Craft button states, we perform
            // the same local-player binding + recipe/detail refresh here whenever the panel becomes active.
            RefreshWindowForCurrentBindings();

            // If inventory snapshot changes, refresh craft availability while open
            if (_inventoryNet != null)
                _inventoryNet.OnSnapshotChanged += OnInventorySnapshotChanged;

            if (_knownItemsNet != null)
                _knownItemsNet.Entries.OnListChanged += OnKnownItemsChanged;
        }

        private void OnDisable()
        {
            if (_inventoryNet != null)
                _inventoryNet.OnSnapshotChanged -= OnInventorySnapshotChanged;

            if (_knownItemsNet != null)
                _knownItemsNet.Entries.OnListChanged -= OnKnownItemsChanged;

            InputState.UnlockGameplay();
        }

        /// <summary>
        /// Called by your local player setup (or a UI root) after player spawns.
        /// </summary>
        public void BindToLocalPlayer(NetworkObject playerNetObj)
        {
            if (playerNetObj == null) return;

            _inventoryNet = playerNetObj.GetComponent<PlayerInventoryNet>();
            _craftingNet = playerNetObj.GetComponent<CraftingNet>();
            _knownItemsNet = playerNetObj.GetComponent<KnownItemsNet>();

            // Mark binding state immediately so nested refresh/availability checks do not
            // re-enter EnsureBoundToLocalPlayer and recursively rebuild recipe UI.
            _bound = _inventoryNet != null && _craftingNet != null && _knownItemsNet != null;

            if (_inventoryNet != null)
            {
                _inventoryNet.OnSnapshotChanged -= OnInventorySnapshotChanged;
                _inventoryNet.OnSnapshotChanged += OnInventorySnapshotChanged;
            }

            if (_knownItemsNet != null)
            {
                _knownItemsNet.Entries.OnListChanged -= OnKnownItemsChanged;
                _knownItemsNet.Entries.OnListChanged += OnKnownItemsChanged;
            }

            // Build initial UI
            SelectCategory(_activeCategory);
        }

        public void Open()
        {
            // Keep Open() behavior aligned with OnEnable initialization.
            RefreshWindowForCurrentBindings();

            if (root != null)
                root.SetActive(true);

            // Match other windows
            InputState.LockGameplay();
        }

        public void Close()
        {
            if (root != null)
                root.SetActive(false);

            // Match other windows
            InputState.UnlockGameplay();
        }

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        private void SelectCategory(CraftingCategory cat)
        {
            _activeCategory = cat;
            RebuildRecipeList();
            AutoSelectFirstRecipeInCategory();
        }

        private void RebuildRecipeList()
        {
            // Clear existing list items
            for (int i = 0; i < _spawnedRecipeItems.Count; i++)
            {
                if (_spawnedRecipeItems[i] != null)
                    Destroy(_spawnedRecipeItems[i].gameObject);
            }
            _spawnedRecipeItems.Clear();

            if (craftingDatabase == null || recipeListRoot == null || recipeListItemPrefab == null)
                return;

            var all = craftingDatabase.AllRecipes;
            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (r.Category != _activeCategory) continue;
                if (!CraftingRecipeUnlockUtility.IsUnlocked(r, _knownItemsNet)) continue;

                var item = Instantiate(recipeListItemPrefab, recipeListRoot);
                item.Bind(r, () =>
                {
                    _selectedRecipe = r;
                    RefreshDetailsPanel();
                });

                _spawnedRecipeItems.Add(item);
            }
        }

        /// <summary>
        /// Rebinds to the local player if needed and refreshes list/details.
        ///
        /// Why this exists:
        /// UIWindowController can show this window by setting the root active directly.
        /// In that path, Open() is bypassed, so we need one centralized refresh routine
        /// that can be called from both OnEnable and Open.
        /// </summary>
        private void RefreshWindowForCurrentBindings()
        {
            EnsureBoundToLocalPlayer();
            RebuildRecipeList();

            // If there is no current recipe for this category, auto-select one
            // so the details panel and craft button can evaluate availability.
            if (_selectedRecipe == null || _selectedRecipe.Category != _activeCategory)
                AutoSelectFirstRecipeInCategory();
            else
                RefreshDetailsPanel();
        }
        private void AutoSelectFirstRecipeInCategory()
        {
            _selectedRecipe = null;

            if (craftingDatabase == null) return;

            var all = craftingDatabase.AllRecipes;
            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (r.Category != _activeCategory) continue;
                if (!CraftingRecipeUnlockUtility.IsUnlocked(r, _knownItemsNet)) continue;

                _selectedRecipe = r;
                break;
            }

            RefreshDetailsPanel();
        }

        private void RefreshDetailsPanel()
        {
            // No recipe selected -> clear details
            if (_selectedRecipe == null)
            {
                if (recipeNameText != null) recipeNameText.text = "(No recipe)";

                if (detailsIconImage != null)
                {
                    detailsIconImage.sprite = null;
                    detailsIconImage.enabled = false;
                }

                if (detailsDescriptionText != null) detailsDescriptionText.text = string.Empty;
                if (detailsPropertiesText != null) detailsPropertiesText.text = string.Empty;

                // Clear the 1x5 grid
                if (ingredientsGrid != null)
                    ingredientsGrid.Bind(null);

                if (craftButton != null) craftButton.interactable = false;
                return;
            }

            // Output item convenience
            var outItem = _selectedRecipe.OutputItem;

            // Title
            if (recipeNameText != null)
                recipeNameText.text = outItem != null ? outItem.DisplayName : _selectedRecipe.name;

            // Icon
            if (detailsIconImage != null)
            {
                var icon = outItem != null ? outItem.Icon : null;
                detailsIconImage.sprite = icon;
                detailsIconImage.enabled = icon != null;
            }

            // Description + Properties
            if (detailsDescriptionText != null)
                detailsDescriptionText.text = outItem != null ? outItem.Description : string.Empty;

            if (detailsPropertiesText != null)
            {
                // Auto-generate the properties block directly from ItemDef stats.
                // Set appendManualPropertiesText to true if you still want to include any extra notes typed into PropertiesText.
                detailsPropertiesText.text = outItem != null
                    ? outItem.BuildPropertiesText(appendManualPropertiesText: true)
                    : string.Empty;
            }

            // Fill the 1x5 ingredient grid (icon + name + required)
            if (ingredientsGrid != null)
                ingredientsGrid.Bind(_selectedRecipe.Ingredients);

            bool canCraft = EvaluateClientCraftAvailability(_selectedRecipe, out string availabilityReason);

            if (debugCraftAvailability)
                Debug.Log($"[Craft][UI] Availability recipeId={_selectedRecipe.RecipeId} canCraft={canCraft} reason={availabilityReason}");

            if (craftButton != null)
                craftButton.interactable = canCraft;
        }

        /// <summary>
        /// UI-side availability check for craft button state.
        ///
        /// Important:
        /// - This is presentation-only and does not replace server validation.
        /// - It follows the tagged slot model so stack and instance payloads are counted correctly.
        /// </summary>
        private bool EvaluateClientCraftAvailability(CraftingRecipeDef recipe, out string reason)
        {
            reason = string.Empty;

            if (recipe == null)
            {
                reason = "NoRecipeSelected";
                return false;
            }

            EnsureBoundToLocalPlayer();
            if (_craftingNet == null)
            {
                reason = "CraftingNetMissing";
                return false;
            }

            if (!ValidateStationClient(recipe, out reason))
                return false;

            if (!HasIngredientsClient(recipe, out reason))
                return false;

            if (!CanFitOutputClient(recipe, out reason))
                return false;

            reason = "Ready";
            return true;
        }

        private bool ValidateStationClient(CraftingRecipeDef recipe, out string reason)
        {
            // Hook for future station requirements once recipe defs carry that metadata.
            reason = "StationOK";
            return true;
        }

        private bool HasIngredientsClient(CraftingRecipeDef recipe, out string reason)
        {
            reason = string.Empty;

            if (_inventoryNet == null || _inventoryNet.LastSnapshot.Slots == null)
            {
                reason = "InventorySnapshotMissing";
                return false;
            }

            for (int i = 0; i < recipe.Ingredients.Count; i++)
            {
                var ing = recipe.Ingredients[i];
                if (ing.Item == null || string.IsNullOrWhiteSpace(ing.Item.ItemId))
                    continue;

                bool requiresInstanceIngredient = ing.Item.UsesItemInstance;
                int required = Mathf.Max(1, ing.Quantity);
                int owned = GetOwnedCountByIngredientMode(ing.Item.ItemId, requiresInstanceIngredient);

                if (debugCraftAvailability)
                    Debug.Log($"[Craft][UI] Ingredient itemId={ing.Item.ItemId} requiresInstance={requiresInstanceIngredient} need={required} owned={owned}");

                if (owned < required)
                {
                    reason = $"MissingIngredients:{ing.Item.ItemId}";
                    return false;
                }
            }

            reason = "IngredientsOK";
            return true;
        }

        private int GetOwnedCountByIngredientMode(string itemId, bool requiresInstanceIngredient)
        {
            var snap = _inventoryNet.LastSnapshot;
            if (snap.Slots == null)
                return 0;

            int count = 0;
            for (int i = 0; i < snap.Slots.Length; i++)
            {
                var s = snap.Slots[i];
                if (s.IsEmpty)
                    continue;

                if (!string.Equals(s.ItemId.ToString(), itemId, System.StringComparison.Ordinal))
                    continue;

                if (requiresInstanceIngredient)
                {
                    if (s.ContentType == InventorySlotContentType.Instance)
                        count += 1;
                }
                else
                {
                    if (s.ContentType == InventorySlotContentType.Stack)
                        count += Mathf.Max(0, s.Quantity);
                }
            }

            return count;
        }

        private bool CanFitOutputClient(CraftingRecipeDef recipe, out string reason)
        {
            reason = string.Empty;

            if (_inventoryNet == null || _inventoryNet.LastSnapshot.Slots == null || recipe.OutputItem == null)
            {
                reason = "OutputFitCheckUnavailable";
                return false;
            }

            var snap = _inventoryNet.LastSnapshot;
            int outputQty = Mathf.Max(1, recipe.OutputQuantity);
            bool outputIsInstance = recipe.OutputItem.UsesItemInstance;

            if (outputIsInstance)
            {
                int empty = 0;
                for (int i = 0; i < snap.Slots.Length; i++)
                {
                    if (snap.Slots[i].IsEmpty || snap.Slots[i].ContentType == InventorySlotContentType.Empty)
                        empty++;
                }

                bool canFitInstances = empty >= outputQty;
                reason = canFitInstances ? "OutputFitOK" : "NotEnoughInventorySpace";

                if (debugCraftAvailability)
                    Debug.Log($"[Craft][UI] OutputFit instance itemId={recipe.OutputItem.ItemId} needSlots={outputQty} empty={empty} result={canFitInstances}");

                return canFitInstances;
            }

            int maxStack = Mathf.Max(1, recipe.OutputItem.MaxStack);
            int remaining = outputQty;
            for (int i = 0; i < snap.Slots.Length && remaining > 0; i++)
            {
                var slot = snap.Slots[i];
                if (slot.IsEmpty || slot.ContentType == InventorySlotContentType.Empty)
                {
                    remaining -= maxStack;
                    continue;
                }

                if (slot.ContentType == InventorySlotContentType.Stack && string.Equals(slot.ItemId.ToString(), recipe.OutputItem.ItemId, System.StringComparison.Ordinal))
                {
                    remaining -= Mathf.Max(0, maxStack - slot.Quantity);
                }
            }

            bool canFitStacks = remaining <= 0;
            reason = canFitStacks ? "OutputFitOK" : "NotEnoughInventorySpace";

            if (debugCraftAvailability)
                Debug.Log($"[Craft][UI] OutputFit stack itemId={recipe.OutputItem.ItemId} qty={outputQty} remaining={remaining} result={canFitStacks}");

            return canFitStacks;
        }

        private void OnCraftClicked()
        {
            if (_selectedRecipe == null) return;
            if (_craftingNet == null) return;

            // Server-authoritative craft request (MVP single craft)
            _craftingNet.RequestCraftServerRpc(_selectedRecipe.RecipeId, 1);
        }

        private void OnInventorySnapshotChanged(HuntersAndCollectors.Networking.DTO.InventorySnapshot snapshot)
        {
            if (IsOpen)
                RefreshDetailsPanel();
        }

        private void OnKnownItemsChanged(Unity.Netcode.NetworkListEvent<KnownItemEntry> _)
        {
            if (IsOpen)
                RefreshWindowForCurrentBindings();
        }

        private void EnsureBoundToLocalPlayer()
        {
            if (_bound) return;

            if (NetworkManager.Singleton == null)
                return;

            foreach (var kvp in NetworkManager.Singleton.SpawnManager.SpawnedObjects)
            {
                var netObj = kvp.Value;
                if (netObj == null) continue;
                if (!netObj.IsOwner) continue;

                var craftingNet = netObj.GetComponent<CraftingNet>();
                var inv = netObj.GetComponent<PlayerInventoryNet>();

                var knownItems = netObj.GetComponent<KnownItemsNet>();
                if (craftingNet != null && inv != null && knownItems != null)
                {
                    // Set _bound before binding to prevent re-entrant EnsureBound calls
                    // during SelectCategory -> RefreshDetailsPanel availability evaluation.
                    _bound = true;
                    BindToLocalPlayer(netObj);
                    return;
                }
            }
        }

        private void TryResolveMissingTabButtons()
        {
            if (consumablesButton != null)
                return;

            Button[] buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button candidate = buttons[i];
                if (candidate == null)
                    continue;

                if (candidate.name.IndexOf("consum", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    consumablesButton = candidate;
                    break;
                }
            }

            if (consumablesButton == null)
                Debug.LogWarning("[CraftingWindowUI] Consumables tab button is not assigned and could not be auto-resolved.", this);
        }
    }
}

