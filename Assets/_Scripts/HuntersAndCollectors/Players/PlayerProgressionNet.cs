using System;
using System.Collections.Generic;
using HuntersAndCollectors.Persistence;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Server-authoritative per-player progression flag registry.
    /// Flags are simple unlock-only booleans for the first pass.
    /// </summary>
    public sealed class PlayerProgressionNet : NetworkBehaviour
    {
        private readonly NetworkList<PlayerProgressionEntry> progressionFlags = new();

        public NetworkList<PlayerProgressionEntry> Flags => progressionFlags;
        public event Action<string> OnServerFeedbackReceived;

        public bool HasFlag(string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId))
                return false;

            FixedString64Bytes target = new(flagId.Trim());
            for (int i = 0; i < progressionFlags.Count; i++)
            {
                if (progressionFlags[i].FlagId.Equals(target))
                    return true;
            }

            return false;
        }

        public bool TryUnlockFlag(string flagId)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(flagId))
                return false;

            string canonical = flagId.Trim();
            if (HasFlag(canonical))
                return false;

            progressionFlags.Add(new PlayerProgressionEntry
            {
                FlagId = new FixedString64Bytes(canonical)
            });

            SaveManager.NotifyPlayerProgressChanged(GetComponent<PlayerNetworkRoot>());
            return true;
        }

        public void LoadFromSave(IReadOnlyList<string> savedFlags)
        {
            if (!IsServer)
                return;

            progressionFlags.Clear();
            if (savedFlags == null)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < savedFlags.Count; i++)
            {
                string flagId = savedFlags[i];
                if (string.IsNullOrWhiteSpace(flagId))
                    continue;

                string canonical = flagId.Trim();
                if (!seen.Add(canonical))
                    continue;

                progressionFlags.Add(new PlayerProgressionEntry
                {
                    FlagId = new FixedString64Bytes(canonical)
                });
            }
        }

        public List<string> ExportToSave()
        {
            var exported = new List<string>(progressionFlags.Count);
            for (int i = 0; i < progressionFlags.Count; i++)
            {
                string flagId = progressionFlags[i].FlagId.ToString();
                if (!string.IsNullOrWhiteSpace(flagId))
                    exported.Add(flagId);
            }

            return exported;
        }

        public void SendFeedbackToOwner(string message)
        {
            if (!IsServer || string.IsNullOrWhiteSpace(message))
                return;

            ClientRpcParams rpcParams = new()
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };

            ReceiveFeedbackClientRpc(message.Trim(), rpcParams);
        }

        [ClientRpc]
        private void ReceiveFeedbackClientRpc(string message, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner)
                return;

            Debug.Log($"[PlayerProgressionNet] {message}");
            OnServerFeedbackReceived?.Invoke(message);
        }
    }
}

