using UnityEngine;
using UnityEngine.InputSystem;
using HuntersAndCollectors.UI;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// InventoryInputController
    /// ---------------------------------------------------------
    /// Handles the "I" key to toggle the CharacterWindowRoot (Inventory + Paperdoll).
    /// </summary>
    public sealed class InventoryInputController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterWindowRootUI characterWindowRoot;

        [Header("Input")]
        [SerializeField] private InputActionReference toggleInventoryAction;

        private void OnEnable()
        {
            if (toggleInventoryAction == null)
            {
                Debug.LogError("[InventoryInputController] toggleInventoryAction is NULL.");
                return;
            }

            // Enable the whole map so the action is guaranteed live.
            toggleInventoryAction.action.actionMap.Enable();

            toggleInventoryAction.action.performed += OnTogglePerformed;

            Debug.Log($"[InventoryInputController] Enabled. Map={toggleInventoryAction.action.actionMap.name} " +
                      $"mapEnabled={toggleInventoryAction.action.actionMap.enabled} actionEnabled={toggleInventoryAction.action.enabled}");
        }

        private void OnDisable()
        {
            if (toggleInventoryAction == null) return;

            toggleInventoryAction.action.performed -= OnTogglePerformed;

            // Optional: DON'T disable the map here if other controllers share it (like Skills).
            // If you *do* disable, both scripts can fight each other.
            // toggleInventoryAction.action.actionMap.Disable();
        }

        private void OnTogglePerformed(InputAction.CallbackContext ctx)
        {
            Debug.Log("[InventoryInputController] ToggleInventory performed.");

            if (characterWindowRoot == null)
            {
                Debug.LogError("[InventoryInputController] characterWindowRoot is NULL.");
                return;
            }

            characterWindowRoot.Toggle();
        }
    }
}