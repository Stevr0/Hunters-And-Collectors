using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.Players
{
    /// <summary>
    /// Enforces first-pass scene isolation for players while the project still uses shared additive scene loading.
    ///
    /// Responsibilities:
    /// - SERVER: ignore collisions between this player and gameplay scenes they are not currently assigned to.
    /// - LOCAL OWNER CLIENT: hide visual/collision content for gameplay scenes that are not active.
    ///
    /// Why this exists:
    /// - The transfer system already tracked where a player should be, but additive scene loading left old scenes rendered.
    /// - Different players can be in different gameplay scenes at the same time, so we cannot globally unload scenes on transfer.
    /// - We therefore keep scenes loaded when needed, but isolate each player by active scene membership.
    ///
    /// First-pass scope:
    /// - This intentionally targets the current gameplay route only: SCN_Village <-> SCN_BeastCaverns.
    /// - We use scene-root enumeration as the isolation unit, then toggle the scene-owned components under those roots.
    ///   This keeps bootstrap and DontDestroyOnLoad services alone while still hiding the full inactive gameplay scene locally.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerNetworkRoot))]
    public sealed class PlayerSceneIsolationRuntime : MonoBehaviour
    {
        [Header("Gameplay Scenes")]
        [Tooltip("Gameplay scenes that should be isolated from each other for this first pass.")]
        [SerializeField] private string[] isolatedGameplayScenes = { "SCN_Village", "SCN_BeastCaverns" };

        [Tooltip("Minimum time between repeated summary logs for the same local isolation pass.")]
        [SerializeField] private float isolationLogCooldownSeconds = 0.5f;

        private readonly HashSet<string> isolatedSceneSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Renderer, bool> originalRendererForceOff = new();
        private readonly Dictionary<Light, bool> originalLightEnabled = new();
        private readonly Dictionary<Canvas, bool> originalCanvasEnabled = new();
        private readonly Dictionary<Collider, bool> originalColliderEnabled = new();
        private readonly Dictionary<Terrain, bool> originalTerrainEnabled = new();

        private PlayerNetworkRoot playerRoot;
        private Collider[] ownedColliders = Array.Empty<Collider>();
        private double nextIsolationLogTime;
        private bool networkHooksActive;

        private struct SceneIsolationCounts
        {
            public int RootCount;
            public int RendererCount;
            public int TerrainCount;
            public int LightCount;
            public int CanvasCount;
            public int ColliderCount;
        }

        private void Awake()
        {
            playerRoot = GetComponent<PlayerNetworkRoot>();
            RebuildSceneSet();
            CacheOwnedColliders();
        }

        public void HandleNetworkSpawn()
        {
            if (networkHooksActive)
                return;

            if (playerRoot == null)
                playerRoot = GetComponent<PlayerNetworkRoot>();

            RebuildSceneSet();
            CacheOwnedColliders();

            if (playerRoot != null)
                playerRoot.ActiveWorldSceneChanged += HandleActiveWorldSceneChanged;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            networkHooksActive = true;

            ApplyIsolation(ResolveActiveSceneName(), logResult: true, triggerReason: "network spawn");
        }

        public void HandleNetworkDespawn()
        {
            if (!networkHooksActive)
                return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;

            if (playerRoot != null)
                playerRoot.ActiveWorldSceneChanged -= HandleActiveWorldSceneChanged;

            if (IsOwnerClient())
                RestorePresentationVisibility();

            if (ShouldApplyCollisionIsolation())
                ResetCollisionIsolation();

            networkHooksActive = false;
        }

        private void OnDisable()
        {
            if (networkHooksActive)
                HandleNetworkDespawn();
        }

        private void HandleActiveWorldSceneChanged(string previousScene, string currentScene)
        {
            ApplyIsolation(currentScene, logResult: true, triggerReason: "active scene changed");
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyIsolation(ResolveActiveSceneName(), logResult: true, triggerReason: $"scene loaded '{scene.name}'");
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            ApplyIsolation(ResolveActiveSceneName(), logResult: true, triggerReason: $"scene unloaded '{scene.name}'");
        }

        private void ApplyIsolation(string activeSceneName, bool logResult, string triggerReason)
        {
            if (!networkHooksActive || string.IsNullOrWhiteSpace(activeSceneName))
                return;

            CacheOwnedColliders();

            if (logResult)
                Debug.Log($"[PlayerSceneIsolationRuntime] Local active gameplay scene='{activeSceneName}' trigger='{triggerReason}'.", this);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !IsIsolatedGameplayScene(scene.name))
                    continue;

                bool isActiveScene = string.Equals(scene.name, activeSceneName, StringComparison.Ordinal);
                SceneIsolationCounts counts = CollectSceneCounts(scene);

                if (ShouldApplyCollisionIsolation())
                    ApplyCollisionIsolationForScene(scene, isActiveScene);

                if (IsOwnerClient())
                    ApplyPresentationIsolationForScene(scene, isActiveScene);

                if (logResult && Time.unscaledTimeAsDouble >= nextIsolationLogTime)
                {
                    string action = isActiveScene ? "shown" : "hidden";
                    Debug.Log(
                        $"[PlayerSceneIsolationRuntime] Scene '{scene.name}' {action} for local player. roots={counts.RootCount} renderers={counts.RendererCount} terrains={counts.TerrainCount} lights={counts.LightCount} canvases={counts.CanvasCount} colliders={counts.ColliderCount}.",
                        this);

                    if (counts.RootCount == 0)
                        Debug.LogWarning($"[PlayerSceneIsolationRuntime] Gameplay scene '{scene.name}' has no root objects to isolate.", this);
                }
            }

            if (logResult && Time.unscaledTimeAsDouble >= nextIsolationLogTime)
            {
                nextIsolationLogTime = Time.unscaledTimeAsDouble + Mathf.Max(0.1f, isolationLogCooldownSeconds);
                Debug.Log($"[PlayerSceneIsolationRuntime] Visibility/isolation step completed for player '{playerRoot?.PlayerKey}' activeScene='{activeSceneName}'.", this);
            }
        }

        private void ApplyCollisionIsolationForScene(Scene scene, bool isActiveScene)
        {
            if (ownedColliders == null || ownedColliders.Length == 0)
                return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                Collider[] sceneColliders = root.GetComponentsInChildren<Collider>(true);
                for (int sceneColliderIndex = 0; sceneColliderIndex < sceneColliders.Length; sceneColliderIndex++)
                {
                    Collider sceneCollider = sceneColliders[sceneColliderIndex];
                    if (sceneCollider == null || sceneCollider.transform.IsChildOf(transform))
                        continue;

                    for (int ownedColliderIndex = 0; ownedColliderIndex < ownedColliders.Length; ownedColliderIndex++)
                    {
                        Collider ownedCollider = ownedColliders[ownedColliderIndex];
                        if (ownedCollider == null || ownedCollider == sceneCollider)
                            continue;

                        Physics.IgnoreCollision(ownedCollider, sceneCollider, !isActiveScene);
                    }
                }
            }
        }

        private void ApplyPresentationIsolationForScene(Scene scene, bool isActiveScene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    if (!originalRendererForceOff.ContainsKey(renderer))
                        originalRendererForceOff[renderer] = renderer.forceRenderingOff;

                    renderer.forceRenderingOff = isActiveScene ? originalRendererForceOff[renderer] : true;
                }

                Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true);
                for (int terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
                {
                    Terrain terrain = terrains[terrainIndex];
                    if (terrain == null)
                        continue;

                    if (!originalTerrainEnabled.ContainsKey(terrain))
                        originalTerrainEnabled[terrain] = terrain.enabled;

                    terrain.enabled = isActiveScene ? originalTerrainEnabled[terrain] : false;
                }

                Light[] lights = root.GetComponentsInChildren<Light>(true);
                for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
                {
                    Light lightComponent = lights[lightIndex];
                    if (lightComponent == null)
                        continue;

                    if (!originalLightEnabled.ContainsKey(lightComponent))
                        originalLightEnabled[lightComponent] = lightComponent.enabled;

                    lightComponent.enabled = isActiveScene ? originalLightEnabled[lightComponent] : false;
                }

                Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
                for (int canvasIndex = 0; canvasIndex < canvases.Length; canvasIndex++)
                {
                    Canvas canvas = canvases[canvasIndex];
                    if (canvas == null)
                        continue;

                    if (!originalCanvasEnabled.ContainsKey(canvas))
                        originalCanvasEnabled[canvas] = canvas.enabled;

                    canvas.enabled = isActiveScene ? originalCanvasEnabled[canvas] : false;
                }

                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    Collider sceneCollider = colliders[colliderIndex];
                    if (sceneCollider == null || sceneCollider.transform.IsChildOf(transform))
                        continue;

                    if (!originalColliderEnabled.ContainsKey(sceneCollider))
                        originalColliderEnabled[sceneCollider] = sceneCollider.enabled;

                    sceneCollider.enabled = isActiveScene ? originalColliderEnabled[sceneCollider] : false;
                }
            }
        }

        private void RestorePresentationVisibility()
        {
            foreach (var pair in originalRendererForceOff)
            {
                if (pair.Key != null)
                    pair.Key.forceRenderingOff = pair.Value;
            }

            foreach (var pair in originalTerrainEnabled)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }

            foreach (var pair in originalLightEnabled)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }

            foreach (var pair in originalCanvasEnabled)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }

            foreach (var pair in originalColliderEnabled)
            {
                if (pair.Key != null)
                    pair.Key.enabled = pair.Value;
            }
        }

        private void ResetCollisionIsolation()
        {
            CacheOwnedColliders();
            if (ownedColliders == null || ownedColliders.Length == 0)
                return;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || !IsIsolatedGameplayScene(scene.name))
                    continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    GameObject root = roots[rootIndex];
                    if (root == null)
                        continue;

                    Collider[] sceneColliders = root.GetComponentsInChildren<Collider>(true);
                    for (int sceneColliderIndex = 0; sceneColliderIndex < sceneColliders.Length; sceneColliderIndex++)
                    {
                        Collider sceneCollider = sceneColliders[sceneColliderIndex];
                        if (sceneCollider == null || sceneCollider.transform.IsChildOf(transform))
                            continue;

                        for (int ownedColliderIndex = 0; ownedColliderIndex < ownedColliders.Length; ownedColliderIndex++)
                        {
                            Collider ownedCollider = ownedColliders[ownedColliderIndex];
                            if (ownedCollider == null || ownedCollider == sceneCollider)
                                continue;

                            Physics.IgnoreCollision(ownedCollider, sceneCollider, false);
                        }
                    }
                }
            }
        }

        private SceneIsolationCounts CollectSceneCounts(Scene scene)
        {
            SceneIsolationCounts counts = default;
            GameObject[] roots = scene.GetRootGameObjects();
            counts.RootCount = roots.Length;

            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                counts.RendererCount += root.GetComponentsInChildren<Renderer>(true).Length;
                counts.TerrainCount += root.GetComponentsInChildren<Terrain>(true).Length;
                counts.LightCount += root.GetComponentsInChildren<Light>(true).Length;
                counts.CanvasCount += root.GetComponentsInChildren<Canvas>(true).Length;
                counts.ColliderCount += root.GetComponentsInChildren<Collider>(true).Length;
            }

            return counts;
        }

        private void CacheOwnedColliders()
        {
            ownedColliders = GetComponentsInChildren<Collider>(true);
        }

        private void RebuildSceneSet()
        {
            isolatedSceneSet.Clear();
            if (isolatedGameplayScenes == null)
                return;

            for (int i = 0; i < isolatedGameplayScenes.Length; i++)
            {
                string sceneName = isolatedGameplayScenes[i];
                if (string.IsNullOrWhiteSpace(sceneName))
                    continue;

                isolatedSceneSet.Add(sceneName.Trim());
            }
        }

        private string ResolveActiveSceneName()
        {
            if (playerRoot == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(playerRoot.CurrentWorldSceneName))
                return playerRoot.CurrentWorldSceneName.Trim();

            return playerRoot.gameObject.scene.IsValid()
                ? playerRoot.gameObject.scene.name
                : string.Empty;
        }

        private bool IsIsolatedGameplayScene(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName) && isolatedSceneSet.Contains(sceneName.Trim());
        }

        private bool IsOwnerClient()
        {
            return playerRoot != null && playerRoot.IsOwner && playerRoot.IsClient;
        }

        private bool ShouldApplyCollisionIsolation()
        {
            return playerRoot != null && (playerRoot.IsServer || (playerRoot.IsOwner && playerRoot.IsClient));
        }
    }
}
