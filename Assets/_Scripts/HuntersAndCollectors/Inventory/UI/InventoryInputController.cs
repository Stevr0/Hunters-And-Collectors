using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// InventoryInputController
    /// ---------------------------------------------------------
    /// Handles the "I" key to toggle Player Inventory UI.
    ///
    /// Rules:
    /// - Local client only.
    /// - Respects InputState so gameplay is properly locked/unlocked.
    /// - Does NOT depend on Player object (UI-level controller).
    /// </summary>
    public sealed class InventoryInputController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInventoryWindowUI inventoryWindow;

        [Header("Input")]
        [SerializeField] private InputActionReference toggleInventoryAction;

        private void OnEnable()
        {
            if (toggleInventoryAction != null)
            {
                toggleInventoryAction.action.performed += OnTogglePerformed;
                toggleInventoryAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (toggleInventoryAction != null)
            {
                toggleInventoryAction.action.performed -= OnTogglePerformed;
                toggleInventoryAction.action.Disable();
            }
        }

        private void OnTogglePerformed(InputAction.CallbackContext ctx)
        {
            if (inventoryWindow == null)
                return;

            inventoryWindow.Toggle();
        }
    }
}
