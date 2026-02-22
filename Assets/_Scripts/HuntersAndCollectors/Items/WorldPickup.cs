using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// WorldPickup
    /// ------------------------------------------------------------
    /// Attach this to any scene item you want players to pick up.
    /// Example: Stone, Stick.
    ///
    /// Requirements:
    /// - The GameObject must have a Collider (for raycast hit).
    /// - The GameObject must have a NetworkObject (so server can despawn it).
    ///
    /// What this script stores:
    /// - itemId: stable string ID used by your item system (e.g., "IT_Stone", "IT_Wood")
    /// - amount: stack size to add to inventory when picked up
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class WorldPickup : NetworkBehaviour
    {
        [Header("Item Data")]
        [Tooltip("Stable item id that will be added to inventory (e.g., IT_Stone, IT_Wood).")]
        [SerializeField] private string itemId = "IT_Stone";

        [Tooltip("How many items this pickup gives.")]
        [SerializeField] private int amount = 1;

        public string ItemId => itemId;
        public int Amount => amount;
    }
}
