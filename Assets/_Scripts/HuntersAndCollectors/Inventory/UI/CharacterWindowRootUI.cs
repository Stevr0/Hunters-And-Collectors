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
            if (root == null)
                root = gameObject;

            if (startHidden)
                root.SetActive(false);
        }

        public void Open()
        {
            if (root == null) return;

            root.SetActive(true);

            // Optional: refresh when opening so visuals are always current.
            if (paperdollWindow != null) paperdollWindow.Open();    // calls RefreshAll in your earlier setup
        }

        public void Close()
        {
            if (root == null) return;
            root.SetActive(false);
        }

        public void Toggle()
        {
            if (root == null) return;

            bool newState = !root.activeSelf;
            Debug.Log($"[CharacterWindowRootUI] Toggle() {root.name} -> {newState}");
            root.SetActive(newState);

            if (newState)
            {
                // Force a clean refresh when opening.
                if (inventoryWindow != null)

                if (paperdollWindow != null)
                    paperdollWindow.ForceRefresh(); // See below
            }
        }

        public bool IsOpen => root != null && root.activeSelf;
    }
}
