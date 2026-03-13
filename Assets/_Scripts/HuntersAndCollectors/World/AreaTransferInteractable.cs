using System;
using System.Collections.Generic;
using HuntersAndCollectors.Interaction;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;

namespace HuntersAndCollectors.World
{
    /// <summary>
    /// Scene-local world interaction component used for entrances, exits, doors, and passages.
    ///
    /// Important first-pass design choice:
    /// - This object is intentionally NOT a NetworkBehaviour.
    /// - The player already owns a spawned NetworkObject, so we send transfer requests through the player.
    /// - That lets a simple cave entrance work with only a collider + AreaTransferInteractable + AreaTransferDef.
    /// - The server then resolves this authored scene object by TransferId and validates distance itself.
    ///
    /// Why that matters:
    /// - Scene entrances are static world authoring objects, not gameplay state owners.
    /// - Requiring every entrance to be a spawned NetworkObject makes authoring brittle and was the main reason
    ///   transfers could appear targetable locally but fail to execute reliably.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransferInteractable : MonoBehaviour, IInteractable
    {
        private static readonly Dictionary<string, List<AreaTransferInteractable>> RegisteredByTransferId = new(StringComparer.OrdinalIgnoreCase);

        [Header("Transfer")]
        [Tooltip("Authored transfer definition that describes the destination, requirements, and optional unlocks.")]
        [SerializeField] private AreaTransferDef transferDef;

        [Header("Interaction")]
        [Tooltip("Maximum distance allowed between the player and this transfer when the server validates use.")]
        [Min(0.1f)]
        [SerializeField] private float interactionRange = 3f;

        [Tooltip("Optional prompt override. Leave blank to use the transfer display name.")]
        [SerializeField] private string promptOverride = string.Empty;

        public AreaTransferDef TransferDef => transferDef;
        public float InteractionRange => interactionRange;

        private void OnEnable()
        {
            Register(this);
            WarnIfAuthoringIsIncomplete();
        }

        private void OnDisable()
        {
            Unregister(this);
        }

        public string BuildPromptText()
        {
            if (!string.IsNullOrWhiteSpace(promptOverride))
                return promptOverride.Trim();

            if (transferDef != null && !string.IsNullOrWhiteSpace(transferDef.DisplayName))
                return $"E: Enter {transferDef.DisplayName.Trim()}";

            return "E: Travel";
        }

        public bool IsPlayerWithinRange(PlayerNetworkRoot playerRoot)
        {
            if (playerRoot == null)
                return false;

            Collider ownCollider = GetComponentInChildren<Collider>(true);
            if (ownCollider == null)
                return Vector3.Distance(playerRoot.transform.position, transform.position) <= interactionRange;

            Vector3 closestPoint = ownCollider.ClosestPoint(playerRoot.transform.position);
            return Vector3.Distance(playerRoot.transform.position, closestPoint) <= interactionRange;
        }

        public float GetInteractionRange()
        {
            return interactionRange;
        }

        public void Interact(ulong playerId)
        {
            if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
                return;

            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(playerId, out NetworkClient client) || client.PlayerObject == null)
            {
                Debug.LogWarning($"[AreaTransferInteractable] Interact failed for transfer '{GetTransferIdForLogs()}': no PlayerObject for clientId={playerId}.", this);
                return;
            }

            PlayerNetworkRoot playerRoot = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
            if (playerRoot == null)
            {
                Debug.LogWarning($"[AreaTransferInteractable] Interact failed for transfer '{GetTransferIdForLogs()}': PlayerObject is missing PlayerNetworkRoot.", this);
                return;
            }

            AreaTransferService.EnsureInstance().ServerRequestTransfer(playerRoot, this);
        }

        public static bool TryResolveRegistered(string transferId, string sourceSceneName, out AreaTransferInteractable interactable)
        {
            interactable = null;

            if (string.IsNullOrWhiteSpace(transferId))
            {
                Debug.LogWarning("[AreaTransferInteractable] Server resolution rejected because transferId was empty.");
                return false;
            }

            if (!RegisteredByTransferId.TryGetValue(transferId.Trim(), out List<AreaTransferInteractable> candidates) || candidates == null)
            {
                Debug.LogWarning($"[AreaTransferInteractable] No registered AreaTransferInteractable was found for TransferId '{transferId}'.");
                return false;
            }

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                AreaTransferInteractable candidate = candidates[i];
                if (candidate == null)
                {
                    candidates.RemoveAt(i);
                    continue;
                }

                if (!candidate.isActiveAndEnabled)
                    continue;

                if (!string.IsNullOrWhiteSpace(sourceSceneName) &&
                    !string.Equals(candidate.gameObject.scene.name, sourceSceneName.Trim(), StringComparison.Ordinal))
                {
                    continue;
                }

                interactable = candidate;
                return true;
            }

            Debug.LogWarning($"[AreaTransferInteractable] TransferId '{transferId}' is registered, but no active entrance was found in scene '{sourceSceneName}'.");
            return false;
        }

        private static void Register(AreaTransferInteractable interactable)
        {
            if (interactable == null || interactable.transferDef == null || string.IsNullOrWhiteSpace(interactable.transferDef.TransferId))
                return;

            string transferId = interactable.transferDef.TransferId.Trim();
            if (!RegisteredByTransferId.TryGetValue(transferId, out List<AreaTransferInteractable> list) || list == null)
            {
                list = new List<AreaTransferInteractable>();
                RegisteredByTransferId[transferId] = list;
            }

            if (!list.Contains(interactable))
                list.Add(interactable);

            int duplicatesInScene = 0;
            for (int i = 0; i < list.Count; i++)
            {
                AreaTransferInteractable other = list[i];
                if (other == null)
                    continue;

                if (string.Equals(other.gameObject.scene.name, interactable.gameObject.scene.name, StringComparison.OrdinalIgnoreCase))
                    duplicatesInScene++;
            }

            if (duplicatesInScene > 1)
            {
                Debug.LogWarning(
                    $"[AreaTransferInteractable] Duplicate TransferId '{transferId}' detected in scene '{interactable.gameObject.scene.name}'. Server resolution will use the first active match, so ids should stay unique per scene.",
                    interactable);
            }
        }

        private static void Unregister(AreaTransferInteractable interactable)
        {
            if (interactable == null || interactable.transferDef == null || string.IsNullOrWhiteSpace(interactable.transferDef.TransferId))
                return;

            string transferId = interactable.transferDef.TransferId.Trim();
            if (!RegisteredByTransferId.TryGetValue(transferId, out List<AreaTransferInteractable> list) || list == null)
                return;

            list.Remove(interactable);
            if (list.Count == 0)
                RegisteredByTransferId.Remove(transferId);
        }

        private void WarnIfAuthoringIsIncomplete()
        {
            if (transferDef == null)
            {
                Debug.LogWarning($"[AreaTransferInteractable] '{name}' is missing AreaTransferDef.", this);
                return;
            }

            if (GetComponentInChildren<Collider>(true) == null)
            {
                Debug.LogWarning(
                    $"[AreaTransferInteractable] '{name}' has no collider on itself or its children. PlayerInteract will not be able to raycast this transfer.",
                    this);
            }
        }

        private string GetTransferIdForLogs()
        {
            if (transferDef == null || string.IsNullOrWhiteSpace(transferDef.TransferId))
                return "<missing-transfer-id>";

            return transferDef.TransferId.Trim();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            interactionRange = Mathf.Max(0.1f, interactionRange);
            promptOverride = string.IsNullOrWhiteSpace(promptOverride) ? string.Empty : promptOverride.Trim();

            if (transferDef == null)
                Debug.LogWarning($"[AreaTransferInteractable] '{name}' is missing AreaTransferDef.", this);

            if (GetComponentInChildren<Collider>(true) == null)
            {
                Debug.LogWarning(
                    $"[AreaTransferInteractable] '{name}' needs a collider on itself or a child so PlayerInteract can detect it.",
                    this);
            }
        }
#endif
    }
}
