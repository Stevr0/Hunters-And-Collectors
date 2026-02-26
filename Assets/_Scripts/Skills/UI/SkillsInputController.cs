using HuntersAndCollectors.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HuntersAndCollectors.Skills.UI
{
    /// <summary>
    /// SkillsInputController
    /// ---------------------------------------------------------
    /// Handles the "K" key to toggle Player Skills UI.
    ///
    /// Rules:
    /// - Local client only.
    /// - Uses Unity Input System (InputActionReference).
    /// - UI-level controller (does NOT require a Player reference).
    /// </summary>
    public sealed class SkillsInputController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Assign your Skills window UI root script here (the one that has Toggle()).")]
        [SerializeField] private SkillsWindowUI skillsWindow;

        [Header("Input")]
        [Tooltip("Input Action reference for toggling the Skills window (bind this to K).")]
        [SerializeField] private InputActionReference toggleSkillsAction;

        private void OnEnable()
        {
            // Safely register input callback and enable action
            if (toggleSkillsAction != null)
            {
                toggleSkillsAction.action.performed += OnTogglePerformed;
                toggleSkillsAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            // Always unregister to avoid duplicate subscriptions / leaks
            if (toggleSkillsAction != null)
            {
                toggleSkillsAction.action.performed -= OnTogglePerformed;
                toggleSkillsAction.action.Disable();
            }
        }

        private void OnTogglePerformed(InputAction.CallbackContext ctx)
        {
            if (skillsWindow == null)
                return;

            skillsWindow.Toggle();
        }
    }
}