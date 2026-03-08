using UnityEngine;

namespace HuntersAndCollectors.Crafting.UI
{
    /// <summary>
    /// Deprecated compatibility shim.
    /// Input handling was centralized into UIWindowController.
    /// This script intentionally performs no input wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CraftingInputController : MonoBehaviour
    {
        private void OnEnable()
        {
            Debug.LogWarning("[CraftingInputController] Deprecated. Use UIWindowController for crafting toggle input.", this);
        }
    }
}
