using HuntersAndCollectors.Inventory;
using UnityEngine;

namespace HuntersAndCollectors.UI.Inventory
{
    /// <summary>
    /// Minimal inventory window presenter that routes drag actions to PlayerInventoryNet.
    /// </summary>
    public sealed class InventoryWindowUI : MonoBehaviour
    {
        // Editor wiring checklist: assign local player's PlayerInventoryNet when UI opens.
        [SerializeField] private PlayerInventoryNet playerInventory;

        /// <summary>
        /// Requests a server move operation for UI drag-and-drop interaction.
        /// </summary>
        public void RequestMove(int fromIndex, int toIndex)
        {
            playerInventory?.RequestMoveSlotServerRpc(fromIndex, toIndex);
        }
    }
}
