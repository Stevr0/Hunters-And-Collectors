using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    /// - 3 tab buttons (Tools/Equipment/Building).
    /// - A container transform for recipe buttons.
    /// - A "recipe details" panel (name, ingredients list, craft button).
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

        [Header("Recipe List")]
        [SerializeField] private Transform recipeListRoot;
        [SerializeField] private RecipeListItemUI recipeListItemPrefab;

        [Header("Details")]
        [SerializeField] private TMP_Text recipeNameText;
        [SerializeField] private Transform ingredientsRoot;
        [SerializeField] private IngredientRowUI ingredientRowPrefab;
        [SerializeField] private Button craftButton;

        [Header("Data")]
        [SerializeField] private CraftingDatabase craftingDatabase;

        private CraftingCategory _activeCategory = CraftingCategory.Tools;
        private CraftingRecipeDef _selectedRecipe;

        // Owner-only references (set when local player spawns)
        private PlayerInventoryNet _inventoryNet;
        private CraftingNet _craftingNet;

        private readonly List<RecipeListItemUI> _spawnedRecipeItems = new();
        private readonly List<IngredientRowUI> _spawnedIngredientRows = new();

        private void Awake()
        {
            if (root == null) root = gameObject;
            if (startHidden) root.SetActive(false);

            // Wire up tab buttons
            if (toolsButton != null) toolsButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Tools));
            if (equipmentButton != null) equipmentButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Equipment));
            if (buildingButton != null) buildingButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Building));

            // Craft button
            if (craftButton != null) craftButton.onClick.AddListener(OnCraftClicked);
        }

        private void OnEnable()
        {
            // If inventory snapshot changes, we want to refresh "owned" counts.
            if (_inventoryNet != null)
                _inventoryNet.OnSnapshotChanged += OnInventorySnapshotChanged;
        }

        private void OnDisable()
        {
            if (_inventoryNet != null)
                _inventoryNet.OnSnapshotChanged -= OnInventorySnapshotChanged;
        }

        /// <summary>
        /// Called by your local player setup (or a UI root) after player spawns.
        /// </summary>
        public void BindToLocalPlayer(NetworkObject playerNetObj)
        {
            if (playerNetObj == null) return;

            _inventoryNet = playerNetObj.GetComponent<PlayerInventoryNet>();
            _craftingNet = playerNetObj.GetComponent<CraftingNet>();

            if (_inventoryNet != null)
            {
                _inventoryNet.OnSnapshotChanged -= OnInventorySnapshotChanged;
                _inventoryNet.OnSnapshotChanged += OnInventorySnapshotChanged;
            }

            // Build initial UI
            SelectCategory(_activeCategory);
        }

        public void Toggle()
        {
            bool next = !root.activeSelf;
            root.SetActive(next);

            if (next)
            {
                // Refresh every time we open so counts are correct.
                RebuildRecipeList();
                RefreshDetailsPanel();
            }
        }

        private void SelectCategory(CraftingCategory cat)
        {
            _activeCategory = cat;
            RebuildRecipeList();

            // Auto-select first recipe in category for MVP convenience.
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

                var item = Instantiate(recipeListItemPrefab, recipeListRoot);
                item.Bind(r, () =>
                {
                    _selectedRecipe = r;
                    RefreshDetailsPanel();
                });

                _spawnedRecipeItems.Add(item);
            }
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

                _selectedRecipe = r;
                break;
            }

            RefreshDetailsPanel();
        }

        private void RefreshDetailsPanel()
        {
            // Clear ingredient rows
            for (int i = 0; i < _spawnedIngredientRows.Count; i++)
            {
                if (_spawnedIngredientRows[i] != null)
                    Destroy(_spawnedIngredientRows[i].gameObject);
            }
            _spawnedIngredientRows.Clear();

            if (_selectedRecipe == null)
            {
                if (recipeNameText != null) recipeNameText.text = "(No recipe)";
                if (craftButton != null) craftButton.interactable = false;
                return;
            }

            // Name like your screenshot: "Axe"
            if (recipeNameText != null)
                recipeNameText.text = _selectedRecipe.OutputItem != null ? _selectedRecipe.OutputItem.DisplayName : _selectedRecipe.name;

            // Build ingredient lines: "1 Wood [owned]" etc
            bool canCraft = true;

            for (int i = 0; i < _selectedRecipe.Ingredients.Count; i++)
            {
                var ing = _selectedRecipe.Ingredients[i];
                if (ing.Item == null) continue;

                int required = Mathf.Max(1, ing.Quantity);
                int owned = GetOwnedCount(ing.Item.ItemId);

                var row = Instantiate(ingredientRowPrefab, ingredientsRoot);
                row.Bind(ing.Item.DisplayName, required, owned);
                _spawnedIngredientRows.Add(row);

                if (owned < required)
                    canCraft = false;
            }

            // Enable craft only if we have the player + crafting component and enough mats
            if (_craftingNet == null)
                canCraft = false;

            if (craftButton != null)
                craftButton.interactable = canCraft;
        }

        private int GetOwnedCount(string itemId)
        {
            if (_inventoryNet == null) return 0;

            // We have a snapshot; count items from it (MVP).
            // This avoids querying server. UI only reads local snapshot.
            var snap = _inventoryNet.LastSnapshot;
            if (snap.Slots == null) return 0;

            int count = 0;
            for (int i = 0; i < snap.Slots.Length; i++)
            {
                var s = snap.Slots[i];
                if (s.IsEmpty) continue;

                if (s.ItemId.ToString() == itemId)
                    count += s.Quantity;
            }
            return count;
        }

        private void OnCraftClicked()
        {
            if (_selectedRecipe == null) return;
            if (_craftingNet == null) return;

            // Server-authoritative craft request
            _craftingNet.RequestCraftServerRpc(_selectedRecipe.RecipeId);
        }

        private void OnInventorySnapshotChanged(HuntersAndCollectors.Networking.DTO.InventorySnapshot snapshot)
        {
            // Update ingredient owned counts + craft button availability
            if (root != null && root.activeSelf)
                RefreshDetailsPanel();
        }
    }
}
