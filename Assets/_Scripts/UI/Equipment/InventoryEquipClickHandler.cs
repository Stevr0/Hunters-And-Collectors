using HuntersAndCollectors.Items;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// Attach to an inventory UI slot/row to support "click to equip".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryEquipClickHandler : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private EquipmentWindowUI equipmentWindow;
        [SerializeField] private ItemDatabase itemDatabase;

        private string itemId;
        private int slotIndex = -1;

        private void Reset()
        {
            button = GetComponent<Button>();
        }

        private void Awake()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
                button.onClick.AddListener(HandleClick);
            }
        }

        public void SetItemId(string newItemId)
        {
            itemId = newItemId;
        }

        public void SetSlotIndex(int index)
        {
            slotIndex = index;
        }

        private void HandleClick()
        {
            if (equipmentWindow == null || itemDatabase == null)
                return;

            if (string.IsNullOrWhiteSpace(itemId))
                return;

            if (!itemDatabase.TryGet(itemId, out ItemDef _))
                return;

            if (slotIndex >= 0)
                equipmentWindow.RequestEquipFromInventorySlot(slotIndex);
            else
                equipmentWindow.RequestEquipFromInventory(itemId);
        }
    }
}

