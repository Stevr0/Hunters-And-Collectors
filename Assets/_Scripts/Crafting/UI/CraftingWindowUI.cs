using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Players;
using HuntersAndCollectors.UI;
using HuntersAndCollectors.Input;
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

        private CraftingCategory _activeCategory = CraftingCategory.Tools;
        private CraftingRecipeDef _selectedRecipe;

        // Owner-only references (set when local player spawns)
        private PlayerInventoryNet _inventoryNet;
        private CraftingNet _craftingNet;

        private readonly List<RecipeListItemUI> _spawnedRecipeItems = new();
        private bool _bound;

        public bool IsOpen => root != null && root.activeSelf;

        private void Awake()
        {
            if (root == null) root = gameObject;
            if (startHidden) root.SetActive(false);

            // Tabs
            if (toolsButton != null) toolsButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Tools));
            if (equipmentButton != null) equipmentButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Equipment));
            if (buildingButton != null) buildingButton.onClick.AddListener(() => SelectCategory(CraftingCategory.Building));

            // Craft
            if (craftButton != null) craftButton.onClick.AddListener(OnCraftClicked);
        }

        private void OnEnable()
        {
            // If inventory snapshot changes, refresh craft availability while open
            if (_inventoryNet != null)
                _inventoryNet.OnSnapshotChanged += OnInventorySnapshotChanged;
        }

        private void OnDisable()
        {
            if (_inventoryNet != null)
                _inventoryNet.OnSnapshotChanged -= OnInventorySnapshotChanged;

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

            if (next)
            {
                EnsureBoundToLocalPlayer();
                RebuildRecipeList();
                RefreshDetailsPanel();

                root.SetActive(true);

                // Match other windows
                InputState.LockGameplay();

                return;
            }

            root.SetActive(false);

            // Match other windows
            InputState.UnlockGameplay();
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

            // Valheim-like craft gating: only enable if we have enough mats
            bool canCraft = (_craftingNet != null);

            if (canCraft)
            {
                for (int i = 0; i < _selectedRecipe.Ingredients.Count; i++)
                {
                    var ing = _selectedRecipe.Ingredients[i];
                    if (ing.Item == null) continue;

                    int required = Mathf.Max(1, ing.Quantity);
                    int owned = GetOwnedCount(ing.Item.ItemId);

                    if (owned < required)
                    {
                        canCraft = false;
                        break;
                    }
                }
            }

            if (craftButton != null)
                craftButton.interactable = canCraft;
        }

        private int GetOwnedCount(string itemId)
        {
            if (_inventoryNet == null) return 0;

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
            if (IsOpen)
                RefreshDetailsPanel();
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

                if (craftingNet != null && inv != null)
                {
                    BindToLocalPlayer(netObj);
                    _bound = true;
                    return;
                }
            }
        }
    }
}