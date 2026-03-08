using UnityEngine;

namespace HuntersAndCollectors.Skills.UI
{
    /// <summary>
    /// Deprecated compatibility shim.
    /// Input handling was centralized into UIWindowController.
    /// This script intentionally performs no input wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkillsInputController : MonoBehaviour
    {
        private void OnEnable()
        {
            Debug.LogWarning("[SkillsInputController] Deprecated. Use UIWindowController for skills toggle input.", this);
        }
    }
}
