using HuntersAndCollectors.Items;
using UnityEngine;
using UnityEngine.UI;

namespace HuntersAndCollectors.UI
{
    /// <summary>
    /// Attach to an inventory UI slot/row to support "click to equip".
    /// Your inventory UI must call SetItemId(itemId) when binding the cell.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryEquipClickHandler : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private PaperdollWindowUI paperdollWindow;
        [SerializeField] private ItemDatabase itemDatabase;

        private string itemId;

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

        private void HandleClick()
        {
            if (paperdollWindow == null || itemDatabase == null)
                return;

            if (string.IsNullOrWhiteSpace(itemId))
                return;

            // MVP equippable check:
            // Replace this with your actual rule if you have def.IsEquippable etc.
            if (itemDatabase.TryGet(itemId, out ItemDef def))
            {
                bool equippable = true; // TODO: replace with real check
                if (equippable)
                    paperdollWindow.RequestEquipFromInventory(itemId);
            }
        }
    }
}