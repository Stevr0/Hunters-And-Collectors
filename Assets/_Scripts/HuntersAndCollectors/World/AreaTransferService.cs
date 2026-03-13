using System.Collections;
using System.Collections.Generic;
using HuntersAndCollectors.Inventory;
using HuntersAndCollectors.Persistence;
using HuntersAndCollectors.Players;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.World
{
    /// <summary>
    /// Server-side coordinator for authored scene transfers.
    /// This first pass uses shared additive scenes so the current bootstrap/session flow can remain intact.
    /// </summary>
    public sealed class AreaTransferService : MonoBehaviour
    {
        private const int SceneLoadTimeoutFrames = 600;

        private static AreaTransferService instance;

        private readonly HashSet<ulong> playersInTransit = new();
        private readonly HashSet<string> managedLoadedScenes = new(System.StringComparer.OrdinalIgnoreCase);

        public static AreaTransferService EnsureInstance()
        {
            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<AreaTransferService>();
            if (instance != null)
                return instance;

            GameObject serviceObject = new("AreaTransferService");
            instance = serviceObject.AddComponent<AreaTransferService>();
            DontDestroyOnLoad(serviceObject);
            return instance;
        }

        public IReadOnlyCollection<string> ManagedLoadedScenes => managedLoadedScenes;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void ServerRequestTransfer(PlayerNetworkRoot playerRoot, AreaTransferInteractable interactable)
        {
            if (!CanProcessServerRequest(playerRoot))
                return;

            if (interactable == null || interactable.TransferDef == null)
            {
                SendPlayerMessage(playerRoot, "The passage cannot be used.");
                return;
            }

            if (playersInTransit.Contains(playerRoot.OwnerClientId))
            {
                SendPlayerMessage(playerRoot, "Travel already in progress.");
                return;
            }

            if (!ValidateTransferRequest(playerRoot, interactable, out string failureMessage))
            {
                SendPlayerMessage(playerRoot, failureMessage);
                return;
            }

            StartCoroutine(ServerTransferRoutine(playerRoot, interactable.TransferDef));
        }

        public IEnumerator ServerRestoreSavedLocation(PlayerNetworkRoot playerRoot, string sceneName, Vector3 position, Quaternion rotation)
        {
            if (!CanProcessServerRequest(playerRoot))
                yield break;

            if (string.IsNullOrWhiteSpace(sceneName))
                yield break;

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogWarning($"[AreaTransferService] Saved scene '{sceneName}' cannot be loaded for player '{playerRoot.PlayerKey}'.");
                yield break;
            }

            yield return EnsureSceneLoaded(sceneName);
            TeleportPlayer(playerRoot.NetworkObject, position, rotation, sceneName);
            playerRoot.ServerSetCurrentWorldScene(sceneName);
        }

        private bool ValidateTransferRequest(PlayerNetworkRoot playerRoot, AreaTransferInteractable interactable, out string failureMessage)
        {
            failureMessage = "The passage is sealed.";

            if (playerRoot == null || playerRoot.NetworkObject == null || !playerRoot.NetworkObject.IsSpawned)
            {
                failureMessage = "Travel failed: player is not ready.";
                return false;
            }

            if (!interactable.IsPlayerWithinRange(playerRoot))
            {
                failureMessage = "You are too far away.";
                return false;
            }

            AreaTransferDef transferDef = interactable.TransferDef;
            if (transferDef == null)
            {
                failureMessage = "The passage cannot be used.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(transferDef.TransferId))
            {
                failureMessage = "Travel failed: transfer id missing.";
                Debug.LogWarning($"[AreaTransferService] Transfer on '{interactable.name}' is missing TransferId.", interactable);
                return false;
            }

            string currentSceneName = playerRoot.CurrentWorldSceneName;
            if (string.IsNullOrWhiteSpace(currentSceneName) && playerRoot.gameObject.scene.IsValid())
                currentSceneName = playerRoot.gameObject.scene.name;

            if (!string.IsNullOrWhiteSpace(transferDef.SourceSceneName) &&
                !string.Equals(transferDef.SourceSceneName, currentSceneName, System.StringComparison.Ordinal))
            {
                failureMessage = "This route does not open from here.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(transferDef.TargetSceneName) || !Application.CanStreamedLevelBeLoaded(transferDef.TargetSceneName))
            {
                failureMessage = "Travel failed: destination is unavailable.";
                Debug.LogError($"[AreaTransferService] Invalid target scene '{transferDef.TargetSceneName}' for transfer '{transferDef.TransferId}'.", interactable);
                return false;
            }

            if (string.IsNullOrWhiteSpace(transferDef.TargetSpawnPointId))
            {
                failureMessage = "Travel failed: destination spawn is missing.";
                Debug.LogError($"[AreaTransferService] Transfer '{transferDef.TransferId}' is missing TargetSpawnPointId.", interactable);
                return false;
            }

            if (!HasRequirements(playerRoot, transferDef, out failureMessage))
                return false;

            return true;
        }

        private bool HasRequirements(PlayerNetworkRoot playerRoot, AreaTransferDef transferDef, out string failureMessage)
        {
            failureMessage = string.IsNullOrWhiteSpace(transferDef.LockedMessage) ? "The passage is sealed." : transferDef.LockedMessage;

            PlayerInventoryNet inventory = playerRoot.Inventory;
            PlayerProgressionNet progression = playerRoot.Progression;

            bool needsItem = transferDef.RequirementType == AreaTransferRequirementType.Item || transferDef.RequirementType == AreaTransferRequirementType.ItemAndFlag;
            bool needsFlag = transferDef.RequirementType == AreaTransferRequirementType.Flag || transferDef.RequirementType == AreaTransferRequirementType.ItemAndFlag;

            if (needsItem)
            {
                if (inventory == null || !inventory.ServerHasItem(transferDef.RequiredItemId, 1))
                {
                    failureMessage = BuildMissingRequirementMessage(playerRoot, transferDef.RequiredItemId, transferDef.LockedMessage);
                    return false;
                }
            }

            if (needsFlag)
            {
                if (progression == null || !progression.HasFlag(transferDef.RequiredFlagId))
                {
                    failureMessage = BuildMissingRequirementMessage(playerRoot, transferDef.RequiredFlagId, transferDef.LockedMessage);
                    return false;
                }
            }

            return true;
        }

        private IEnumerator ServerTransferRoutine(PlayerNetworkRoot playerRoot, AreaTransferDef transferDef)
        {
            if (!playersInTransit.Add(playerRoot.OwnerClientId))
                yield break;

            SendPlayerMessage(playerRoot, BuildSuccessMessage(transferDef));

            bool shouldConsumeItem = transferDef.ConsumeRequiredItemOnUse &&
                                     (transferDef.RequirementType == AreaTransferRequirementType.Item || transferDef.RequirementType == AreaTransferRequirementType.ItemAndFlag);

            if (shouldConsumeItem && (playerRoot.Inventory == null || !playerRoot.Inventory.ServerRemoveItem(transferDef.RequiredItemId, 1)))
            {
                SendPlayerMessage(playerRoot, BuildMissingRequirementMessage(playerRoot, transferDef.RequiredItemId, transferDef.LockedMessage));
                playersInTransit.Remove(playerRoot.OwnerClientId);
                yield break;
            }

            yield return EnsureSceneLoaded(transferDef.TargetSceneName);

            if (!SceneSpawnRegistry.TryGetSpawnPoint(transferDef.TargetSceneName, transferDef.TargetSpawnPointId, out SceneSpawnPoint spawnPoint))
            {
                Debug.LogError($"[AreaTransferService] Missing spawn point '{transferDef.TargetSpawnPointId}' in scene '{transferDef.TargetSceneName}' for transfer '{transferDef.TransferId}'.");
                SendPlayerMessage(playerRoot, "Travel failed: destination spawn not found.");
                playersInTransit.Remove(playerRoot.OwnerClientId);
                yield break;
            }

            TeleportPlayer(playerRoot.NetworkObject, spawnPoint.transform.position, spawnPoint.transform.rotation, transferDef.TargetSpawnPointId);
            playerRoot.ServerSetCurrentWorldScene(transferDef.TargetSceneName);
            playerRoot.ServerSetLoadedWorldPosition(spawnPoint.transform.position, spawnPoint.transform.eulerAngles.y, transferDef.TargetSceneName);

            if (transferDef.UnlockFlagOnSuccess && playerRoot.Progression != null && !string.IsNullOrWhiteSpace(transferDef.FlagToUnlockOnSuccess))
                playerRoot.Progression.TryUnlockFlag(transferDef.FlagToUnlockOnSuccess);

            SaveManager.NotifyPlayerProgressChanged(playerRoot);
            playersInTransit.Remove(playerRoot.OwnerClientId);
        }

        private IEnumerator EnsureSceneLoaded(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                managedLoadedScenes.Add(sceneName);
                yield break;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                yield break;

            SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[AreaTransferService] Failed to start load for scene '{sceneName}'. Status={status}.");
                yield break;
            }

            for (int frame = 0; frame < SceneLoadTimeoutFrames; frame++)
            {
                scene = SceneManager.GetSceneByName(sceneName);
                if (scene.IsValid() && scene.isLoaded)
                {
                    managedLoadedScenes.Add(sceneName);
                    yield break;
                }

                yield return null;
            }

            Debug.LogError($"[AreaTransferService] Timed out waiting for scene '{sceneName}' to load.");
        }

        public IEnumerator UnloadManagedScenes()
        {
            string[] scenes = new string[managedLoadedScenes.Count];
            managedLoadedScenes.CopyTo(scenes);
            managedLoadedScenes.Clear();

            for (int i = 0; i < scenes.Length; i++)
            {
                string sceneName = scenes[i];
                Scene scene = SceneManager.GetSceneByName(sceneName);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                AsyncOperation unload = SceneManager.UnloadSceneAsync(sceneName);
                if (unload == null)
                    continue;

                while (!unload.isDone)
                    yield return null;
            }
        }

        private bool CanProcessServerRequest(PlayerNetworkRoot playerRoot)
        {
            return playerRoot != null &&
                   NetworkManager.Singleton != null &&
                   NetworkManager.Singleton.IsServer &&
                   NetworkManager.Singleton.IsListening;
        }

        private static void TeleportPlayer(NetworkObject playerObject, Vector3 position, Quaternion rotation, string debugLabel)
        {
            if (playerObject == null)
                return;

            CharacterController controller = playerObject.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;

            playerObject.transform.SetPositionAndRotation(position, rotation);

            if (controller != null)
                controller.enabled = true;

            Debug.Log($"[AreaTransferService] Teleported player '{playerObject.NetworkObjectId}' to '{debugLabel}'.");
        }

        private static string BuildSuccessMessage(AreaTransferDef transferDef)
        {
            if (transferDef == null)
                return "Traveling...";

            if (!string.IsNullOrWhiteSpace(transferDef.SuccessMessage))
                return transferDef.SuccessMessage;

            if (!string.IsNullOrWhiteSpace(transferDef.DisplayName))
                return $"Entering {transferDef.DisplayName.Trim()}...";

            return "Traveling...";
        }

        private static string BuildMissingRequirementMessage(PlayerNetworkRoot playerRoot, string requirementId, string lockedMessage)
        {
            if (!string.IsNullOrWhiteSpace(lockedMessage))
                return lockedMessage;

            if (playerRoot != null &&
                playerRoot.Inventory != null &&
                playerRoot.Inventory.ServerTryGetItemDef(requirementId, out var itemDef) &&
                itemDef != null)
            {
                string displayName = string.IsNullOrWhiteSpace(itemDef.DisplayName) ? itemDef.ItemId : itemDef.DisplayName;
                return $"Requires {displayName}.";
            }

            return $"Requires {requirementId}.";
        }

        private static void SendPlayerMessage(PlayerNetworkRoot playerRoot, string message)
        {
            if (playerRoot?.Progression == null || string.IsNullOrWhiteSpace(message))
                return;

            playerRoot.Progression.SendFeedbackToOwner(message);
        }
    }
}
