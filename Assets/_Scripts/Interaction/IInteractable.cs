using UnityEngine;

namespace HuntersAndCollectors.Interaction
{
    /// <summary>
    /// Interface for any object that can be interacted with.
    /// 
    /// Design:
    /// - Client detects it via raycast
    /// - Client requests interaction via ServerRpc
    /// - Server validates distance + authority
    /// </summary>
    public interface IInteractable
    {
        // Called on SERVER after validation
        void Interact(ulong playerId);

        // Optional: used for range checks
        float GetInteractionRange();
    }
}