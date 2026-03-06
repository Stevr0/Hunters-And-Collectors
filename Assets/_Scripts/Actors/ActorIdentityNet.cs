using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Authoritative social identity for any combat-capable network actor.
    ///
    /// Server writes these values. Clients read only.
    /// ActorDefBinder is responsible for applying authored defaults on server spawn.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ActorIdentityNet : NetworkBehaviour
    {
        // Server authoritative replicated identity fields.
        public readonly NetworkVariable<FixedString64Bytes> ActorId =
            new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> FactionId =
            new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> PvpEnabled =
            new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;

            // Fallback identifier for server logs when no ActorDef-provided id is present.
            if (ActorId.Value.Length == 0 && NetworkObject != null && NetworkObject.IsPlayerObject)
                ActorId.Value = new FixedString64Bytes($"Player_{OwnerClientId}");
        }

        public int GetFactionId() => FactionId.Value;

        public bool GetPvpEnabled() => PvpEnabled.Value;

        /// <summary>
        /// SERVER ONLY: sets PvP opt-in state for this actor.
        /// UI should call a server RPC component (see PvpToggleNet), which then calls this.
        /// </summary>
        public void ServerSetPvpEnabled(bool enabled)
        {
            if (!IsServer)
                return;

            PvpEnabled.Value = enabled;
        }
    }
}
