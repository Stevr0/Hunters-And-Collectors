using UnityEngine;
using HuntersAndCollectors.Items;
using HuntersAndCollectors.Inventory;

/// <summary>
/// InventoryGridSmokeTest
/// ------------------------------------------------------------
/// Validates InventoryGrid logic WITHOUT networking or UI.
/// 
/// What this tests:
/// - Add stacking
/// - Add overflow remainder
/// - Remove validation
/// - Slot move / stacking / swapping
/// - Stack splitting
/// 
/// This must work perfectly before networking touches inventory.
/// </summary>
public sealed class InventoryGridSmokeTest : MonoBehaviour
{
    [Header("Database")]
    [SerializeField] private ItemDatabase itemDatabase;

    [Header("Test Items")]
    [SerializeField] private ItemDef wood;
    [SerializeField] private ItemDef stone;

    [Header("Grid Size")]
    [SerializeField] private int width = 4;
    [SerializeField] private int height = 3;

    private InventoryGrid _grid;

    private void Start()
    {
        Debug.Log("===== INVENTORY GRID SMOKE TEST START =====");

        Debug.Log($"[SmokeTest] DB assigned? {(itemDatabase != null)}");

        if (wood != null)
        {
            Debug.Log($"[SmokeTest] wood.ItemId = '{wood.ItemId}'");
            Debug.Log($"[SmokeTest] DB.TryGet(wood) = {itemDatabase.TryGet(wood.ItemId, out var woodDefFromDb)}");
        }

        if (stone != null)
        {
            Debug.Log($"[SmokeTest] stone.ItemId = '{stone.ItemId}'");
            Debug.Log($"[SmokeTest] DB.TryGet(stone) = {itemDatabase.TryGet(stone.ItemId, out var stoneDefFromDb)}");
        }

        // Create grid with your constructor signature
        _grid = new InventoryGrid(width, height, itemDatabase);

        // 1) Add 30 wood
        TestAdd(wood.ItemId, 30);

        // 2) Add 50 more wood (should stack)
        TestAdd(wood.ItemId, 50);

        Dump("After Adding Wood");

        // 3) Add stone
        TestAdd(stone.ItemId, 15);

        Dump("After Adding Stone");

        // 4) Split stack (slot 0 split 10)
        if (_grid.TrySplitStack(0, 10, out int newIndex))
        {
            Debug.Log($"Split successful. New slot index: {newIndex}");
        }
        else
        {
            Debug.LogWarning("Split failed.");
        }

        Dump("After Split");

        // 5) Move slot 0 -> slot 5
        if (_grid.TryMoveSlot(0, 5))
            Debug.Log("Move successful.");
        else
            Debug.LogWarning("Move failed.");

        Dump("After Move");

        // 6) Remove some wood
        if (_grid.Remove(wood.ItemId, 20))
            Debug.Log("Remove successful.");
        else
            Debug.LogWarning("Remove failed.");

        Dump("After Remove 20 Wood");

        Debug.Log("===== INVENTORY GRID SMOKE TEST END =====");
    }

    private void TestAdd(string itemId, int qty)
    {
        int remainder = _grid.Add(itemId, qty);
        Debug.Log($"Add {qty} {itemId} => remainder: {remainder}");
    }

    private void Dump(string title)
    {
        Debug.Log($"--- {title} ---");

        for (int i = 0; i < _grid.Slots.Length; i++)
        {
            var slot = _grid.Slots[i];

            if (slot.IsEmpty)
                Debug.Log($"Slot {i:00}: EMPTY");
            else
                Debug.Log($"Slot {i:00}: {slot.Stack.ItemId} x{slot.Stack.Quantity}");
        }
    }
}