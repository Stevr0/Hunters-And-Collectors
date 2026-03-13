using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.World
{
    /// <summary>
    /// Minimal authored flag award helper for relics, shrines, boss rewards, and dungeon completion triggers.
    /// It keeps first-pass progression unlocks simple and server-authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ProgressionFlagAwarder : NetworkBehaviour
    {
        [Tooltip("Per-player progression flag unlocked when this award succeeds.")]
        [SerializeField] private string flagId = string.Empty;

        [Tooltip("Optional success message sent to the triggering player.")]
        [SerializeField] private string successMessage = string.Empty;

        [Tooltip("If enabled, this awarder can only unlock its flag once per player. First-pass flags are unlock-only, so this is normally enabled.")]
        [SerializeField] private bool unlockOnly = true;

        public string FlagId => flagId;

        public bool TryAward(PlayerNetworkRoot playerRoot)
        {
            if (!IsServer || playerRoot == null)
                return false;

            PlayerProgressionNet progression = playerRoot.Progression;
            if (progression == null || string.IsNullOrWhiteSpace(flagId))
                return false;

            string canonical = flagId.Trim();
            if (unlockOnly && progression.HasFlag(canonical))
                return false;

            if (!progression.TryUnlockFlag(canonical))
                return false;

            if (!string.IsNullOrWhiteSpace(successMessage))
                progression.SendFeedbackToOwner(successMessage.Trim());

            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestAwardFlagServerRpc(ServerRpcParams rpcParams = default)
        {
            if (!IsServer)
                return;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client) ||
                client.PlayerObject == null)
                return;

            PlayerNetworkRoot playerRoot = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
            TryAward(playerRoot);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            flagId = string.IsNullOrWhiteSpace(flagId) ? string.Empty : flagId.Trim();
            successMessage = string.IsNullOrWhiteSpace(successMessage) ? string.Empty : successMessage.Trim();
        }
#endif
    }
}

