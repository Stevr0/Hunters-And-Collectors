using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// CraftingInputController
    /// ------------------------------------------------------------
    /// UIRoot-level input hook for crafting window.
    ///
    /// Pattern matches your InventoryInputController / SkillsInputController:
    /// - Uses an InputActionReference (so you can bind in inspector)
    /// - Only toggles the window (UI remains client-side)
    /// </summary>
    public sealed class CraftingInputController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CraftingWindowUI craftingWindow;

        [Header("Input")]
        [Tooltip("Input Action Reference: Player/ToggleCrafting (bound to C).")]
        [SerializeField] private InputActionReference toggleCraftingAction;

        private void OnEnable()
        {
            if (toggleCraftingAction != null)
            {
                toggleCraftingAction.action.performed += OnToggleCrafting;
                toggleCraftingAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            if (toggleCraftingAction != null)
            {
                toggleCraftingAction.action.performed -= OnToggleCrafting;
                toggleCraftingAction.action.Disable();
            }
        }

        private void OnToggleCrafting(InputAction.CallbackContext ctx)
        {
            if (craftingWindow == null)
            {
                Debug.LogWarning("[CraftingInputController] CraftingWindowUI reference not set.");
                return;
            }

            craftingWindow.Toggle();
        }
    }
}
