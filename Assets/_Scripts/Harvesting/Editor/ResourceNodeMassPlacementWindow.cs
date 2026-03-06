#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using HuntersAndCollectors.Harvesting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HuntersAndCollectors.EditorTools
{
    public sealed class ResourceNodeMassPlacementWindow : EditorWindow
    {
        private const string DefaultScenePath = "Assets/Scenes/SCN_Village.unity";

        [SerializeField] private SceneAsset targetScene;
        [SerializeField] private GameObject nodePrefab;
        [SerializeField] private Transform parentTransform;
        [SerializeField] private Transform placementOrigin;
        [SerializeField] private int spawnCount = 50;
        [SerializeField] private Vector2 areaSize = new(80f, 80f);
        [SerializeField] private float raycastHeight = 100f;
        [SerializeField] private float raycastDistance = 500f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private bool groundSnap = true;
        [SerializeField] private float yOffset = 0.05f;
        [SerializeField] private bool randomYaw = true;
        [SerializeField] private bool assignIdsAfterSpawn = true;

        [MenuItem("Tools/Hunters & Collectors/Harvesting/Resource Node Mass Placement")]
        private static void OpenWindow()
        {
            var window = GetWindow<ResourceNodeMassPlacementWindow>("Node Mass Placement");
            window.minSize = new Vector2(420f, 460f);
            window.Show();
        }

        private void OnEnable()
        {
            if (targetScene == null)
                targetScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(DefaultScenePath);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Scene", EditorStyles.boldLabel);
            targetScene = (SceneAsset)EditorGUILayout.ObjectField(new GUIContent("Target Scene", "Scene to open/place into."), targetScene, typeof(SceneAsset), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
            nodePrefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent("Node Prefab", "Prefab with ResourceNodeNet + NetworkObject."), nodePrefab, typeof(GameObject), false);
            parentTransform = (Transform)EditorGUILayout.ObjectField(new GUIContent("Parent (Optional)", "Parent for spawned nodes."), parentTransform, typeof(Transform), true);
            placementOrigin = (Transform)EditorGUILayout.ObjectField(new GUIContent("Origin (Optional)", "Center of spawn area. Uses world origin when empty."), placementOrigin, typeof(Transform), true);

            spawnCount = EditorGUILayout.IntField(new GUIContent("Spawn Count", "How many nodes to spawn."), spawnCount);
            areaSize = EditorGUILayout.Vector2Field(new GUIContent("Area Size", "Spawn rectangle width/length around origin."), areaSize);
            groundSnap = EditorGUILayout.Toggle(new GUIContent("Ground Snap", "Raycast to place on terrain/ground."), groundSnap);

            using (new EditorGUI.DisabledScope(!groundSnap))
            {
                groundMask = LayerMaskField(new GUIContent("Ground Mask", "Layers considered ground for raycast snapping."), groundMask);
                raycastHeight = EditorGUILayout.FloatField(new GUIContent("Raycast Height", "Ray starts this many units above candidate point."), raycastHeight);
                raycastDistance = EditorGUILayout.FloatField(new GUIContent("Raycast Distance", "Max downward ray distance."), raycastDistance);
                yOffset = EditorGUILayout.FloatField(new GUIContent("Y Offset", "Offset from hit point to avoid clipping."), yOffset);
            }

            randomYaw = EditorGUILayout.Toggle(new GUIContent("Random Yaw", "Apply random Y rotation per spawn."), randomYaw);
            assignIdsAfterSpawn = EditorGUILayout.Toggle(new GUIContent("Assign Unique IDs", "Auto-assign unique NodeId values after spawn."), assignIdsAfterSpawn);

            EditorGUILayout.Space();
            if (GUILayout.Button("Open Target Scene"))
                OpenTargetScene();

            using (new EditorGUI.DisabledScope(nodePrefab == null || spawnCount <= 0))
            {
                if (GUILayout.Button("Mass Place Nodes In Active Scene"))
                    MassPlaceNodesInActiveScene();
            }

            if (GUILayout.Button("Assign/Fix Unique NodeIDs In Active Scene"))
                AssignUniqueNodeIdsInActiveScene();
        }

        private void OpenTargetScene()
        {
            if (targetScene == null)
            {
                Debug.LogError("[NodeMassPlacement] Assign a target scene first.");
                return;
            }

            var scenePath = AssetDatabase.GetAssetPath(targetScene);
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                Debug.LogError("[NodeMassPlacement] Could not resolve target scene path.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Debug.Log($"[NodeMassPlacement] Opened scene: {scenePath}");
        }

        private void MassPlaceNodesInActiveScene()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !activeScene.isLoaded)
            {
                Debug.LogError("[NodeMassPlacement] No active scene loaded.");
                return;
            }

            if (!PrefabUtility.IsPartOfPrefabAsset(nodePrefab))
            {
                Debug.LogError("[NodeMassPlacement] Node Prefab must be a prefab asset, not a scene instance.");
                return;
            }

            spawnCount = Mathf.Max(1, spawnCount);
            areaSize.x = Mathf.Max(1f, areaSize.x);
            areaSize.y = Mathf.Max(1f, areaSize.y);
            raycastHeight = Mathf.Max(1f, raycastHeight);
            raycastDistance = Mathf.Max(1f, raycastDistance);

            Vector3 origin = placementOrigin != null ? placementOrigin.position : Vector3.zero;
            int spawned = 0;
            int attempts = 0;
            int maxAttempts = Mathf.Max(spawnCount * 8, spawnCount);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Mass Place Resource Nodes");

            while (spawned < spawnCount && attempts < maxAttempts)
            {
                attempts++;

                float x = UnityEngine.Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f);
                float z = UnityEngine.Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f);
                Vector3 candidate = origin + new Vector3(x, 0f, z);
                Vector3 position = candidate;

                if (groundSnap)
                {
                    Vector3 rayStart = candidate + Vector3.up * raycastHeight;
                    if (!Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastDistance, groundMask, QueryTriggerInteraction.Ignore))
                        continue;

                    position = hit.point + Vector3.up * yOffset;
                }

                Quaternion rotation = randomYaw
                    ? Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)
                    : Quaternion.identity;

                var instanceObj = PrefabUtility.InstantiatePrefab(nodePrefab, activeScene) as GameObject;
                if (instanceObj == null)
                    continue;

                Undo.RegisterCreatedObjectUndo(instanceObj, "Create Resource Node");
                if (parentTransform != null)
                    instanceObj.transform.SetParent(parentTransform, true);

                instanceObj.transform.SetPositionAndRotation(position, rotation);

                if (!instanceObj.TryGetComponent<ResourceNodeNet>(out _))
                {
                    Debug.LogWarning("[NodeMassPlacement] Spawned object has no ResourceNodeNet; deleting instance.", instanceObj);
                    Undo.DestroyObjectImmediate(instanceObj);
                    continue;
                }

                spawned++;
            }

            Undo.CollapseUndoOperations(undoGroup);

            if (assignIdsAfterSpawn)
                AssignUniqueNodeIdsInActiveScene();

            EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log($"[NodeMassPlacement] Spawned {spawned}/{spawnCount} nodes in scene '{activeScene.name}' after {attempts} attempts.");
        }

        private static void AssignUniqueNodeIdsInActiveScene()
        {
            var nodes = UnityEngine.Object.FindObjectsByType<ResourceNodeNet>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (nodes == null || nodes.Length == 0)
            {
                Debug.Log("[NodeMassPlacement] No ResourceNodeNet instances found in active scene.");
                return;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perPrefixNext = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int changed = 0;

            Array.Sort(nodes, (a, b) => string.CompareOrdinal(a.name, b.name));

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Assign Unique Resource Node IDs");

            foreach (var node in nodes)
            {
                if (node == null)
                    continue;

                var so = new SerializedObject(node);
                var nodeIdProp = so.FindProperty("nodeId");
                if (nodeIdProp == null)
                    continue;

                string current = nodeIdProp.stringValue?.Trim() ?? string.Empty;
                bool currentIsUnique = !string.IsNullOrWhiteSpace(current) && !used.Contains(current);

                if (currentIsUnique)
                {
                    used.Add(current);
                    continue;
                }

                string prefix = BuildPrefix(node);
                string nextId = GenerateUniqueId(prefix, used, perPrefixNext);

                Undo.RecordObject(node, "Assign NodeId");
                nodeIdProp.stringValue = nextId;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(node);

                used.Add(nextId);
                changed++;
            }

            Undo.CollapseUndoOperations(undoGroup);

            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
                EditorSceneManager.MarkSceneDirty(activeScene);

            Debug.Log($"[NodeMassPlacement] Processed {nodes.Length} nodes. Assigned/fixed {changed} NodeId values.");
        }

        private static string BuildPrefix(ResourceNodeNet node)
        {
            string resource = node.ResourceType.ToString().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(resource) || resource == "NONE")
                resource = "NODE";

            return resource;
        }

        private static string GenerateUniqueId(string prefix, HashSet<string> used, Dictionary<string, int> perPrefixNext)
        {
            if (!perPrefixNext.TryGetValue(prefix, out int next))
                next = 1;

            string candidate;
            do
            {
                candidate = $"{prefix}_{next:0000}";
                next++;
            }
            while (used.Contains(candidate));

            perPrefixNext[prefix] = next;
            return candidate;
        }

        private static LayerMask LayerMaskField(GUIContent label, LayerMask selected)
        {
            var layers = InternalEditorUtility.layers;
            var layerNumbers = new List<int>(layers.Length);
            int maskWithoutEmpty = 0;

            for (int i = 0; i < layers.Length; i++)
            {
                int layer = LayerMask.NameToLayer(layers[i]);
                if (layer < 0)
                    continue;

                layerNumbers.Add(layer);
                if (((1 << layer) & selected.value) != 0)
                    maskWithoutEmpty |= 1 << (layerNumbers.Count - 1);
            }

            maskWithoutEmpty = EditorGUILayout.MaskField(label, maskWithoutEmpty, layers);

            int finalMask = 0;
            for (int i = 0; i < layerNumbers.Count; i++)
            {
                if ((maskWithoutEmpty & (1 << i)) != 0)
                    finalMask |= 1 << layerNumbers[i];
            }

            selected.value = finalMask;
            return selected;
        }
    }
}
#endif
