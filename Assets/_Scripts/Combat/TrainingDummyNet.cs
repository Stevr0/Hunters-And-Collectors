using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    /// <summary>
    /// Optional identity/tag component for training-dummy specific content.
    ///
    /// Health and damage authority now live in HealthNet + DamageableNet.
    /// This component intentionally contains no gameplay health logic.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class TrainingDummyNet : NetworkBehaviour
    {
    }
}
