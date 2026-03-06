using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Server-authoritative PvP toggle request path for player actors.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ActorIdentityNet))]
    public sealed class PvpToggleNet : NetworkBehaviour
    {
        private ActorIdentityNet actorIdentity;

        private void Awake()
        {
            actorIdentity = GetComponent<ActorIdentityNet>();
        }

        [ServerRpc(RequireOwnership = true)]
        public void RequestSetPvpEnabledServerRpc(bool enabled)
        {
            if (!IsServer)
                return;

            if (actorIdentity == null)
                actorIdentity = GetComponent<ActorIdentityNet>();

            if (actorIdentity == null)
                return;

            actorIdentity.ServerSetPvpEnabled(enabled);
        }
    }
}
