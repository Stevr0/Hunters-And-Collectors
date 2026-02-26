using HuntersAndCollectors.Inventory.UI;
using UnityEngine;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// CharacterWindowRootUI
    /// ---------------------------------------------------------
    /// Single root that opens/closes multiple character panels together.
    /// MVP: Inventory + Paperdoll.
    /// </summary>
    public sealed class CharacterWindowRootUI : MonoBehaviour
    {
        [Header("Root")]
        [Tooltip("If null, uses this GameObject.")]
        [SerializeField] private GameObject root;

        [Tooltip("Start hidden? Usually yes.")]
        [SerializeField] private bool startHidden = true;

        [Header("Children (optional refresh hooks)")]
        [SerializeField] private PaperdollWindowUI paperdollWindow;
        [SerializeField] private PlayerInventoryWindowUI inventoryWindow;

        private void Awake()
        {
            // If you didn't assign a root, default to THIS object.
            if (root == null)
                root = gameObject;

            // Start hidden so the character windows are closed on boot.
            if (startHidden)
                root.SetActive(false);
        }

        /// <summary>Open the whole Character Window root.</summary>
        public void Open()
        {
            if (root == null) return;

            root.SetActive(true);

            // When opening, refresh children so they reflect latest network state.
            RefreshChildren();
        }

        /// <summary>Close the whole Character Window root.</summary>
        public void Close()
        {
            if (root == null) return;
            root.SetActive(false);
        }

        /// <summary>Toggle the Character Window root open/closed.</summary>
        public void Toggle()
        {
            if (root == null) return;

            bool newState = !root.activeSelf;
            Debug.Log($"[CharacterWindowRootUI] Toggle() {root.name} -> {newState}");

            root.SetActive(newState);

            if (newState)
                RefreshChildren();
        }

        /// <summary>True if the root is currently visible.</summary>
        public bool IsOpen => root != null && root.activeSelf;

        /// <summary>
        /// Refresh child windows when the root becomes visible.
        /// This keeps UI in sync after equipment/inventory changed while closed.
        /// </summary>
        private void RefreshChildren()
        {
            // Paperdoll: ForceRefresh should call RefreshAll() internally.
            if (paperdollWindow != null)
                paperdollWindow.ForceRefresh();
        }
    }
}