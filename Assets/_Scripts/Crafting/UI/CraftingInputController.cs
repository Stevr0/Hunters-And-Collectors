using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// CraftingInputController
    /// ---------------------------------------------------------
    /// Handles the "C" key to toggle the CraftingWindowUI.
    /// Mirrors InventoryInputController pattern.
    /// </summary>
    public sealed class CraftingInputController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CraftingWindowUI craftingWindow;

        [Header("Input")]
        [SerializeField] private InputActionReference toggleCraftingAction;

        private void OnEnable()
        {
            if (toggleCraftingAction == null)
            {
                Debug.LogError("[CraftingInputController] toggleCraftingAction is NULL.");
                return;
            }

            // Ensure the action map is enabled so the key works.
            toggleCraftingAction.action.actionMap.Enable();

            toggleCraftingAction.action.performed += OnTogglePerformed;

            Debug.Log($"[CraftingInputController] Enabled. Map={toggleCraftingAction.action.actionMap.name} " +
                      $"mapEnabled={toggleCraftingAction.action.actionMap.enabled} actionEnabled={toggleCraftingAction.action.enabled}");
        }

        private void OnDisable()
        {
            if (toggleCraftingAction == null) return;
            toggleCraftingAction.action.performed -= OnTogglePerformed;
        }

        private void OnTogglePerformed(InputAction.CallbackContext ctx)
        {
            if (craftingWindow == null)
            {
                Debug.LogError("[CraftingInputController] craftingWindow is NULL.");
                return;
            }

            craftingWindow.Toggle();
        }
    }
}