using UnityEngine;

namespace HuntersAndCollectors.Inventory.UI
{
    /// <summary>
    /// Deprecated compatibility shim.
    /// Input handling was centralized into UIWindowController.
    /// This script intentionally performs no input wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryInputController : MonoBehaviour
    {
        private void OnEnable()
        {
            Debug.LogWarning("[InventoryInputController] Deprecated. Use UIWindowController for inventory toggle input.", this);
        }
    }
}
