using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Items
{
    /// <summary>
    /// WorldPickup
    /// --------------------------------------------------------------------
    /// Attach this to any world item that can be picked up (Stone, Stick, etc).
    ///
    /// IMPORTANT CHANGE (Option 3):
    /// - Instead of typing a string itemId (error-prone),
    ///   you assign an ItemDefinition asset in the inspector.
    ///
    /// Why this is safer:
    /// - No typos / casing issues
    /// - You can rename display names freely while the stable id remains consistent
    /// - You can expand ItemDefinition with icon, weight, stack size, etc.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class WorldPickup : NetworkBehaviour
    {
        [Header("Item Data")]
        [Tooltip("Drag an ItemDefinition asset here (e.g. it_stone, it_stick).")]
        [SerializeField] private ItemDef itemDefinition;

        [Tooltip("How many items are granted when picked up.")]
        [Min(1)]
        [SerializeField] private int quantity = 1;

        /// <summary>
        /// The assigned ItemDefinition.
        /// </summary>
        public ItemDef ItemDefinition => itemDefinition;

        /// <summary>
        /// Quantity granted.
        /// </summary>
        public int Quantity => quantity;

        /// <summary>
        /// Convenience: stable item id string used by the inventory system.
        /// Returns empty if the definition isn't assigned (we guard against this).
        /// </summary>
        public string ItemId => itemDefinition != null ? itemDefinition.ItemId : string.Empty;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Editor-time safety: warn immediately if someone forgets to assign the definition.
            if (itemDefinition == null)
                Debug.LogWarning($"[WorldPickup] '{name}' has no ItemDefinition assigned.", this);
        }
#endif
    }
}
