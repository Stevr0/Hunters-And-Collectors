using System.Collections;
using System.Collections.Generic;
using HuntersAndCollectors.Bootstrap;
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
    ///
    /// Current multiplayer model:
    /// - The server may keep multiple gameplay scenes loaded at once.
    /// - Each player has one authoritative active gameplay scene.
    /// - Transfers move the player object into the destination Unity scene and update active scene state.
    /// - Empty managed gameplay scenes may be unloaded once no players remain assigned to them.
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

        public void ServerRequestTransferById(PlayerNetworkRoot playerRoot, string transferId, string sourceSceneName)
        {
            if (!CanProcessServerRequest(playerRoot))
            {
                Debug.LogWarning("[AreaTransferService] Transfer request ignored because the server is not ready.");
                return;
            }

            string resolvedSceneName = string.IsNullOrWhiteSpace(sourceSceneName)
                ? ResolvePlayerSceneName(playerRoot)
                : sourceSceneName.Trim();

            Debug.Log(
                $"[AreaTransferService] Transfer request received on server. player='{playerRoot.PlayerKey}' currentScene='{ResolvePlayerSceneName(playerRoot)}' transferId='{transferId}' sourceScene='{resolvedSceneName}'.",
                playerRoot);

            if (string.IsNullOrWhiteSpace(transferId))
            {
                Debug.LogWarning($"[AreaTransferService] Transfer request rejected for player '{playerRoot.PlayerKey}' because transferId was empty.", playerRoot);
                SendPlayerMessage(playerRoot, "The passage cannot be used.");
                return;
            }

            if (!AreaTransferInteractable.TryResolveRegistered(transferId, resolvedSceneName, out AreaTransferInteractable interactable) || interactable == null)
            {
                Debug.LogWarning(
                    $"[AreaTransferService] Transfer request rejected for player '{playerRoot.PlayerKey}' because TransferId '{transferId}' was not resolved in scene '{resolvedSceneName}'.",
                    playerRoot);
                SendPlayerMessage(playerRoot, "The passage cannot be used.");
                return;
            }

            Debug.Log(
                $"[AreaTransferService] Transfer interactable resolved for transferId='{transferId}' on object '{interactable.name}'.",
                interactable);

            ServerRequestTransfer(playerRoot, interactable);
        }

        public void ServerRequestTransfer(PlayerNetworkRoot playerRoot, AreaTransferInteractable interactable)
        {
            if (!CanProcessServerRequest(playerRoot))
            {
                Debug.LogWarning("[AreaTransferService] Transfer request ignored because the server is not ready.");
                return;
            }

            if (interactable == null || interactable.TransferDef == null)
            {
                Debug.LogWarning("[AreaTransferService] Transfer request rejected because the interactable or definition was missing.", interactable);
                SendPlayerMessage(playerRoot, "The passage cannot be used.");
                return;
            }

            if (playersInTransit.Contains(playerRoot.OwnerClientId))
            {
                Debug.LogWarning($"[AreaTransferService] Transfer request ignored because player '{playerRoot.PlayerKey}' is already in transit.", playerRoot);
                SendPlayerMessage(playerRoot, "Travel already in progress.");
                return;
            }

            if (!ValidateTransferRequest(playerRoot, interactable, out string failureMessage))
            {
                SendPlayerMessage(playerRoot, failureMessage);
                return;
            }

            Debug.Log(
                $"[AreaTransferService] Transfer validated. player='{playerRoot.PlayerKey}' transferId='{interactable.TransferDef.TransferId}' targetScene='{interactable.TransferDef.TargetSceneName}' spawn='{interactable.TransferDef.TargetSpawnPointId}'.",
                interactable);

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

            Scene targetScene = SceneManager.GetSceneByName(sceneName);
            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                Debug.LogError($"[AreaTransferService] Saved scene restore failed because scene '{sceneName}' is not loaded.", playerRoot);
                yield break;
            }

            MovePlayerToScene(playerRoot.NetworkObject, targetScene, sceneName);
            TeleportPlayer(playerRoot.NetworkObject, position, rotation, sceneName);
            playerRoot.ServerSetCurrentWorldScene(sceneName);
        }

        private bool ValidateTransferRequest(PlayerNetworkRoot playerRoot, AreaTransferInteractable interactable, out string failureMessage)
        {
            failureMessage = "The passage is sealed.";

            if (playerRoot == null || playerRoot.NetworkObject == null || !playerRoot.NetworkObject.IsSpawned)
            {
                failureMessage = "Travel failed: player is not ready.";
                Debug.LogWarning("[AreaTransferService] Transfer validation failed because the player object was missing or not spawned.", interactable);
                return false;
            }

            if (!interactable.IsPlayerWithinRange(playerRoot))
            {
                failureMessage = "You are too far away.";
                Debug.LogWarning(
                    $"[AreaTransferService] Transfer validation failed because player '{playerRoot.PlayerKey}' was out of range for '{interactable.name}'.",
                    interactable);
                return false;
            }

            AreaTransferDef transferDef = interactable.TransferDef;
            if (transferDef == null)
            {
                failureMessage = "The passage cannot be used.";
                Debug.LogWarning($"[AreaTransferService] Transfer validation failed because '{interactable.name}' is missing AreaTransferDef.", interactable);
                return false;
            }

            if (string.IsNullOrWhiteSpace(transferDef.TransferId))
            {
                failureMessage = "Travel failed: transfer id missing.";
                Debug.LogWarning($"[AreaTransferService] Transfer on '{interactable.name}' is missing TransferId.", interactable);
                return false;
            }

            string currentSceneName = ResolvePlayerSceneName(playerRoot);
            if (!string.IsNullOrWhiteSpace(transferDef.SourceSceneName) &&
                !string.Equals(transferDef.SourceSceneName, currentSceneName, System.StringComparison.Ordinal))
            {
                failureMessage = "This route does not open from here.";
                Debug.LogWarning(
                    $"[AreaTransferService] Transfer validation failed for player '{playerRoot.PlayerKey}'. Expected source scene '{transferDef.SourceSceneName}' but current authoritative scene was '{currentSceneName}'.",
                    interactable);
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
            {
                Debug.LogWarning(
                    $"[AreaTransferService] Transfer validation failed for player '{playerRoot.PlayerKey}' on transfer '{transferDef.TransferId}'. Reason='{failureMessage}'.",
                    interactable);
                return false;
            }

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

            string previousSceneName = ResolvePlayerSceneName(playerRoot);

            Debug.Log(
                $"[AreaTransferService] Beginning shared-scene transfer. player='{playerRoot.PlayerKey}' previousScene='{previousSceneName}' targetScene='{transferDef.TargetSceneName}'.",
                playerRoot);

            SendPlayerMessage(playerRoot, BuildSuccessMessage(transferDef));

            bool shouldConsumeItem = transferDef.ConsumeRequiredItemOnUse &&
                                     (transferDef.RequirementType == AreaTransferRequirementType.Item || transferDef.RequirementType == AreaTransferRequirementType.ItemAndFlag);

            if (shouldConsumeItem && (playerRoot.Inventory == null || !playerRoot.Inventory.ServerRemoveItem(transferDef.RequiredItemId, 1)))
            {
                Debug.LogWarning(
                    $"[AreaTransferService] Transfer '{transferDef.TransferId}' aborted because the required item could not be consumed for player '{playerRoot.PlayerKey}'.",
                    playerRoot);
                SendPlayerMessage(playerRoot, BuildMissingRequirementMessage(playerRoot, transferDef.RequiredItemId, transferDef.LockedMessage));
                playersInTransit.Remove(playerRoot.OwnerClientId);
                yield break;
            }

            yield return EnsureSceneLoaded(transferDef.TargetSceneName);

            Scene targetScene = SceneManager.GetSceneByName(transferDef.TargetSceneName);
            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                Debug.LogError($"[AreaTransferService] Target scene '{transferDef.TargetSceneName}' was not loaded after transfer request '{transferDef.TransferId}'.", playerRoot);
                SendPlayerMessage(playerRoot, "Travel failed: destination scene was not loaded.");
                playersInTransit.Remove(playerRoot.OwnerClientId);
                yield break;
            }

            Debug.Log($"[AreaTransferService] Target scene resolved: '{transferDef.TargetSceneName}'.", playerRoot);

            if (!SceneSpawnRegistry.TryGetSpawnPoint(transferDef.TargetSceneName, transferDef.TargetSpawnPointId, out SceneSpawnPoint spawnPoint))
            {
                Debug.LogError($"[AreaTransferService] Missing spawn point '{transferDef.TargetSpawnPointId}' in scene '{transferDef.TargetSceneName}' for transfer '{transferDef.TransferId}'.", playerRoot);
                SendPlayerMessage(playerRoot, "Travel failed: destination spawn not found.");
                playersInTransit.Remove(playerRoot.OwnerClientId);
                yield break;
            }

            Debug.Log(
                $"[AreaTransferService] Target spawn found. scene='{transferDef.TargetSceneName}' spawnId='{transferDef.TargetSpawnPointId}' position={spawnPoint.transform.position}.",
                spawnPoint);

            MovePlayerToScene(playerRoot.NetworkObject, targetScene, transferDef.TargetSceneName);
            TeleportPlayer(playerRoot.NetworkObject, spawnPoint.transform.position, spawnPoint.transform.rotation, transferDef.TargetSpawnPointId);
            playerRoot.ServerSetCurrentWorldScene(transferDef.TargetSceneName);
            playerRoot.ServerSetLoadedWorldPosition(spawnPoint.transform.position, spawnPoint.transform.eulerAngles.y, transferDef.TargetSceneName);

            if (transferDef.UnlockFlagOnSuccess && playerRoot.Progression != null && !string.IsNullOrWhiteSpace(transferDef.FlagToUnlockOnSuccess))
                playerRoot.Progression.TryUnlockFlag(transferDef.FlagToUnlockOnSuccess);

            Debug.Log(
                $"[AreaTransferService] Player moved successfully. player='{playerRoot.PlayerKey}' targetScene='{transferDef.TargetSceneName}' spawnId='{transferDef.TargetSpawnPointId}'.",
                playerRoot);

            LogSceneOccupancy(previousSceneName);
            TryUnloadSceneIfEmpty(previousSceneName);
            SaveManager.NotifyPlayerProgressChanged(playerRoot);
            playersInTransit.Remove(playerRoot.OwnerClientId);
        }

        private IEnumerator EnsureSceneLoaded(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                managedLoadedScenes.Add(sceneName);
                Debug.Log($"[AreaTransferService] Destination scene '{sceneName}' already loaded.");
                yield break;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                yield break;

            Debug.Log($"[AreaTransferService] Loading destination scene '{sceneName}' additively.");
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
                    Debug.Log($"[AreaTransferService] Destination scene '{sceneName}' finished loading.");
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

        private void MovePlayerToScene(NetworkObject playerObject, Scene targetScene, string sceneName)
        {
            if (playerObject == null)
            {
                Debug.LogError($"[AreaTransferService] Player scene membership change failed because player object was null for target scene '{sceneName}'.");
                return;
            }

            if (!targetScene.IsValid() || !targetScene.isLoaded)
            {
                Debug.LogError($"[AreaTransferService] Player scene membership change failed because destination scene '{sceneName}' is not loaded.", playerObject);
                return;
            }

            if (playerObject.gameObject.scene == targetScene)
            {
                Debug.Log($"[AreaTransferService] Player scene membership already set to '{sceneName}' for PlayerObjectId={playerObject.NetworkObjectId}.", playerObject);
                return;
            }

            SceneManager.MoveGameObjectToScene(playerObject.gameObject, targetScene);
            Debug.Log($"[AreaTransferService] Player scene membership changed to '{sceneName}' for PlayerObjectId={playerObject.NetworkObjectId}.", playerObject);
        }

        private void LogSceneOccupancy(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return;

            int occupants = CountPlayersAssignedToScene(sceneName);
            Debug.Log($"[AreaTransferService] Scene '{sceneName}' is still occupied by {occupants} player(s).");
        }

        private void TryUnloadSceneIfEmpty(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return;

            int occupants = CountPlayersAssignedToScene(sceneName);
            if (occupants > 0)
            {
                Debug.Log($"[AreaTransferService] Scene unload skipped for '{sceneName}' because it still has {occupants} player(s).", this);
                return;
            }

            if (Bootstrapper.Instance != null && string.Equals(sceneName, Bootstrapper.Instance.GameplaySceneName, System.StringComparison.Ordinal))
            {
                Debug.Log($"[AreaTransferService] Scene unload skipped for '{sceneName}' because it is the bootstrap gameplay scene.", this);
                return;
            }

            if (!managedLoadedScenes.Contains(sceneName))
            {
                Debug.Log($"[AreaTransferService] Scene unload skipped for '{sceneName}' because it is not managed by AreaTransferService.", this);
                return;
            }

            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                return;

            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                managedLoadedScenes.Remove(sceneName);
                return;
            }

            SceneEventProgressStatus unloadStatus = NetworkManager.Singleton.SceneManager.UnloadScene(scene);
            if (unloadStatus != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"[AreaTransferService] Scene unload failed to start for '{sceneName}'. Status={unloadStatus}.", this);
                return;
            }

            managedLoadedScenes.Remove(sceneName);
            Debug.Log($"[AreaTransferService] Scene unload performed for '{sceneName}' because it became empty.", this);
        }

        private int CountPlayersAssignedToScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || NetworkManager.Singleton == null)
                return 0;

            int count = 0;
            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client?.PlayerObject == null)
                    continue;

                PlayerNetworkRoot playerRoot = client.PlayerObject.GetComponent<PlayerNetworkRoot>();
                if (playerRoot == null)
                    continue;

                if (string.Equals(ResolvePlayerSceneName(playerRoot), sceneName, System.StringComparison.Ordinal))
                    count++;
            }

            return count;
        }

        private bool CanProcessServerRequest(PlayerNetworkRoot playerRoot)
        {
            return playerRoot != null &&
                   NetworkManager.Singleton != null &&
                   NetworkManager.Singleton.IsServer &&
                   NetworkManager.Singleton.IsListening;
        }

        private static string ResolvePlayerSceneName(PlayerNetworkRoot playerRoot)
        {
            if (playerRoot == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(playerRoot.CurrentWorldSceneName))
                return playerRoot.CurrentWorldSceneName;

            return playerRoot.gameObject.scene.IsValid()
                ? playerRoot.gameObject.scene.name
                : string.Empty;
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
