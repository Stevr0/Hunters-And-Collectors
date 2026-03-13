using System.Collections;
using HuntersAndCollectors.CameraSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// LocalPlayerCameraBinder
    /// --------------------------------------------------------------------
    /// Hooks the one persistent runtime camera up to the local owning player.
    ///
    /// Why this exists:
    /// - NGO may spawn players later than the bootstrap camera.
    /// - We only want the local owner to control a camera.
    /// - Camera assignment is a presentation concern, not gameplay authority.
    ///
    /// Important transfer note:
    /// - Scene transfers move the player correctly on the server, but the camera still needs
    ///   a local presentation rebind after the teleport finishes.
    /// - If we do not reset the orbit camera after a large teleport, it can continue smoothing
    ///   from the old scene position and the Game view appears stuck or incorrect.
    /// - We therefore rebind on local owner spawn, on active gameplay scene changes, and after
    ///   relevant scene load events.
    ///
    /// Single gameplay camera rule:
    /// - The persistent bootstrap camera should be the only active gameplay camera at runtime.
    /// - Any authored gameplay-scene cameras found in SCN_Village / SCN_BeastCaverns are disabled
    ///   so they cannot override Camera.main or steal the Game view.
    ///
    /// Attach this to the one persistent runtime camera object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalPlayerCameraBinder : MonoBehaviour
    {
        [Tooltip("Preferred child transform used as the camera follow anchor. Example: CameraMount.")]
        [SerializeField] private string cameraMountName = "CameraMount";

        [Tooltip("Optional fallback child transform if the preferred camera mount does not exist.")]
        [SerializeField] private string fallbackMountName = "ViewOrigin";

        [Tooltip("Gameplay scenes that must never drive their own authored runtime camera while the persistent bootstrap camera exists.")]
        [SerializeField] private string[] gameplaySceneNames = { "SCN_Village", "SCN_BeastCaverns" };

        private ThirdPersonOrbitCamera orbitCamera;
        private Camera boundCamera;
        private AudioListener audioListener;
        private PlayerNetworkRoot currentLocalOwner;
        private Coroutine delayedRebindRoutine;

        private void Awake()
        {
            orbitCamera = GetComponent<ThirdPersonOrbitCamera>();
            boundCamera = GetComponent<Camera>();
            audioListener = GetComponent<AudioListener>();

            // Mark the persistent runtime camera as MainCamera immediately.
            // This avoids a spawn-order race where owner gameplay scripts ask for
            // Camera.main before the local owner bind event fires.
            gameObject.tag = "MainCamera";
        }

        private void OnEnable()
        {
            PlayerNetworkRoot.LocalOwnerSpawned += HandleLocalOwnerSpawned;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            TryBindExistingLocalOwner();
            EnsureSingleGameplayCamera("OnEnable");
        }

        private void OnDisable()
        {
            PlayerNetworkRoot.LocalOwnerSpawned -= HandleLocalOwnerSpawned;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnhookCurrentLocalOwner();

            if (delayedRebindRoutine != null)
            {
                StopCoroutine(delayedRebindRoutine);
                delayedRebindRoutine = null;
            }
        }

        private void HandleLocalOwnerSpawned(PlayerNetworkRoot localOwner)
        {
            if (localOwner == null)
                return;

            HookCurrentLocalOwner(localOwner);
            Debug.Log($"[LocalPlayerCameraBinder] Local player spawned. owner='{localOwner.name}' activeScene='{localOwner.CurrentWorldSceneName}'.", this);
            BindCameraToOwner("local player spawn");
            QueueDelayedRebind("post-spawn settle");
        }

        private void HandleActiveWorldSceneChanged(string previousScene, string currentScene)
        {
            Debug.Log($"[LocalPlayerCameraBinder] Local player active scene changed from '{previousScene}' to '{currentScene}'. Rebinding camera.", this);

            // Rebinding immediately resets the orbit camera's smoothing state so it does not spend
            // multiple frames drifting from the old scene. A delayed rebind then catches the frame
            // after teleport/network transform updates have settled.
            BindCameraToOwner("scene transfer immediate");
            QueueDelayedRebind("scene transfer settle");
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            DisableConflictingSceneCameras(scene, $"scene load '{scene.name}'");
            EnsureSingleGameplayCamera($"scene load '{scene.name}'");

            if (currentLocalOwner != null)
                QueueDelayedRebind($"scene load '{scene.name}'");
        }

        private void TryBindExistingLocalOwner()
        {
            PlayerNetworkRoot[] players = FindObjectsByType<PlayerNetworkRoot>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                PlayerNetworkRoot player = players[i];
                if (player == null || !player.IsOwner || !player.IsClient)
                    continue;

                HookCurrentLocalOwner(player);
                BindCameraToOwner("existing local owner");
                return;
            }
        }

        private void HookCurrentLocalOwner(PlayerNetworkRoot localOwner)
        {
            if (currentLocalOwner == localOwner)
                return;

            UnhookCurrentLocalOwner();
            currentLocalOwner = localOwner;
            currentLocalOwner.ActiveWorldSceneChanged += HandleActiveWorldSceneChanged;
        }

        private void UnhookCurrentLocalOwner()
        {
            if (currentLocalOwner != null)
                currentLocalOwner.ActiveWorldSceneChanged -= HandleActiveWorldSceneChanged;

            currentLocalOwner = null;
        }

        private void QueueDelayedRebind(string reason)
        {
            if (!isActiveAndEnabled)
                return;

            if (delayedRebindRoutine != null)
                StopCoroutine(delayedRebindRoutine);

            delayedRebindRoutine = StartCoroutine(CoDelayedRebind(reason));
        }

        private IEnumerator CoDelayedRebind(string reason)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            BindCameraToOwner(reason);
            delayedRebindRoutine = null;
        }

        private void BindCameraToOwner(string reason)
        {
            if (currentLocalOwner == null)
                return;

            Transform resolvedTarget = ResolveCameraTarget(currentLocalOwner.transform);
            EnsureSingleGameplayCamera(reason);

            // Keep Camera.main usage stable for systems that raycast from the main camera.
            gameObject.tag = "MainCamera";

            if (boundCamera != null && !boundCamera.enabled)
                boundCamera.enabled = true;

            if (audioListener != null && !audioListener.enabled)
                audioListener.enabled = true;

            if (orbitCamera != null)
            {
                // Third-person path: keep the camera unparented and let the orbit script drive it.
                transform.SetParent(null, worldPositionStays: true);
                orbitCamera.SetFollowTarget(resolvedTarget);
                Debug.Log($"[LocalPlayerCameraBinder] Camera bound via orbit camera. reason='{reason}' target='{resolvedTarget.name}' cameraPos={transform.position} targetPos={resolvedTarget.position}.", this);
                return;
            }

            // Legacy fallback path: preserve the old behaviour if the orbit component is not present.
            transform.SetParent(resolvedTarget, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            Debug.Log($"[LocalPlayerCameraBinder] Orbit camera not found. Falling back to parenting camera to '{resolvedTarget.name}' for reason='{reason}'.", this);
        }

        private void EnsureSingleGameplayCamera(string reason)
        {
            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int enabledCameraCount = 0;
            int mainCameraTaggedCount = 0;

            for (int i = 0; i < allCameras.Length; i++)
            {
                Camera cameraComponent = allCameras[i];
                if (cameraComponent == null)
                    continue;

                if (cameraComponent.enabled && cameraComponent.gameObject.activeInHierarchy)
                    enabledCameraCount++;

                if (cameraComponent.CompareTag("MainCamera"))
                    mainCameraTaggedCount++;
            }

            if (enabledCameraCount > 1)
                Debug.LogWarning($"[LocalPlayerCameraBinder] Duplicate camera detected during '{reason}'. EnabledCameraCount={enabledCameraCount} MainCameraTaggedCount={mainCameraTaggedCount}.", this);

            for (int i = 0; i < allCameras.Length; i++)
            {
                Camera cameraComponent = allCameras[i];
                if (cameraComponent == null)
                    continue;

                string sceneName = cameraComponent.gameObject.scene.IsValid() ? cameraComponent.gameObject.scene.name : "<invalid>";
                bool isSelfCamera = cameraComponent.gameObject == gameObject;

                Debug.Log($"[LocalPlayerCameraBinder] Camera audit reason='{reason}' name='{cameraComponent.name}' scene='{sceneName}' enabled={cameraComponent.enabled} active={cameraComponent.gameObject.activeInHierarchy} mainTag={cameraComponent.CompareTag("MainCamera")} self={isSelfCamera}.", cameraComponent);
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(i);
                DisableConflictingSceneCameras(loadedScene, reason);
            }
        }

        private void DisableConflictingSceneCameras(Scene scene, string reason)
        {
            if (!scene.IsValid() || !scene.isLoaded || !IsGameplayScene(scene.name))
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                GameObject root = roots[rootIndex];
                if (root == null)
                    continue;

                Camera[] sceneCameras = root.GetComponentsInChildren<Camera>(true);
                for (int cameraIndex = 0; cameraIndex < sceneCameras.Length; cameraIndex++)
                {
                    Camera sceneCamera = sceneCameras[cameraIndex];
                    if (sceneCamera == null || sceneCamera.gameObject == gameObject)
                        continue;

                    bool changed = false;
                    if (sceneCamera.enabled)
                    {
                        sceneCamera.enabled = false;
                        changed = true;
                    }

                    AudioListener listener = sceneCamera.GetComponent<AudioListener>();
                    if (listener != null && listener.enabled)
                    {
                        listener.enabled = false;
                        changed = true;
                    }

                    if (sceneCamera.CompareTag("MainCamera"))
                    {
                        sceneCamera.tag = "Untagged";
                        changed = true;
                    }

                    if (changed)
                    {
                        Debug.LogWarning($"[LocalPlayerCameraBinder] Scene-authored camera '{sceneCamera.name}' in scene '{scene.name}' was disabled at runtime during '{reason}' so the persistent gameplay camera keeps control.", sceneCamera);
                    }
                }
            }
        }

        private bool IsGameplayScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || gameplaySceneNames == null)
                return false;

            for (int i = 0; i < gameplaySceneNames.Length; i++)
            {
                if (string.Equals(gameplaySceneNames[i], sceneName, System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private Transform ResolveCameraTarget(Transform playerRoot)
        {
            if (playerRoot == null)
                return transform;

            if (!string.IsNullOrWhiteSpace(cameraMountName))
            {
                Transform preferred = playerRoot.Find(cameraMountName);
                if (preferred != null)
                    return preferred;
            }

            if (!string.IsNullOrWhiteSpace(fallbackMountName))
            {
                Transform fallback = playerRoot.Find(fallbackMountName);
                if (fallback != null)
                    return fallback;
            }

            Debug.LogWarning($"[LocalPlayerCameraBinder] Could not find '{cameraMountName}' or '{fallbackMountName}' under {playerRoot.name}. Falling back to player root.");
            return playerRoot;
        }
    }
}
