using HuntersAndCollectors.Crafting;
using HuntersAndCollectors.Items;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Bootstrap
{
    /// <summary>
    /// Entry-point scene bootstrapper that wires core databases and network manager.
    /// </summary>
    public sealed class GameBootstrapper : MonoBehaviour
    {
        // Editor wiring checklist: place in first scene, assign NetworkManager, ItemDatabase, RecipeDatabase.
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private ItemDatabase itemDatabase;
        [SerializeField] private RecipeDatabase recipeDatabase;

        /// <summary>
        /// Loaded item database for other systems in MVP service-locator style.
        /// </summary>
        public ItemDatabase ItemDatabase => itemDatabase;
        /// <summary>
        /// Loaded recipe database for crafting systems.
        /// </summary>
        public RecipeDatabase RecipeDatabase => recipeDatabase;

        private void Awake()
        {
            if (networkManager == null) networkManager = FindObjectOfType<NetworkManager>();
            // TODO: Start host automatically here if startup flow requires it.
        }
    }
}
