using Unity.Collections;
using UnityEngine;

namespace HuntersAndCollectors.Players
{
    [DisallowMultipleComponent]
    public sealed class PlayerEquipmentVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerEquipmentNet equipmentNet;

        [Header("Anchor (your rig)")]
        [SerializeField] private Transform rightHandCombatAnchor;

        private GameObject currentToolInstance;

        private void Awake()
        {
            if (equipmentNet == null)
                equipmentNet = GetComponent<PlayerEquipmentNet>();
        }

        private void OnEnable()
        {
            if (equipmentNet == null)
                return;

            // Subscribe to the actual replicated value changes (bulletproof).
            equipmentNet.MainHandNetVar.OnValueChanged += OnMainHandChanged;
        }

        private void OnDisable()
        {
            if (equipmentNet == null)
                return;

            equipmentNet.MainHandNetVar.OnValueChanged -= OnMainHandChanged;
        }

        private void Start()
        {
            // Force an initial refresh in case the value was already set.
            RefreshFromEquipmentState();
        }

        private void OnMainHandChanged(FixedString64Bytes prev, FixedString64Bytes next)
        {
            Debug.Log($"[PlayerEquipmentVisual] MainHand changed '{prev}' -> '{next}'");
            RefreshFromEquipmentState();
        }

        private void RefreshFromEquipmentState()
        {
            if (currentToolInstance != null)
            {
                Destroy(currentToolInstance);
                currentToolInstance = null;
            }

            if (equipmentNet == null || rightHandCombatAnchor == null)
                return;

            string mainHandItemId = equipmentNet.GetMainHandItemId();
            if (string.IsNullOrWhiteSpace(mainHandItemId))
                return;

            // Resolve ItemDef via ItemDatabase
            if (!equipmentNet.TryGetItemDef(mainHandItemId, out var def))
                return;

            var prefab = def.VisualPrefab;
            if (prefab == null)
                return;

            currentToolInstance = Instantiate(prefab, rightHandCombatAnchor);

            // Apply per-item tuning from ItemDef
            currentToolInstance.transform.localPosition = def.EquipLocalPosition;
            currentToolInstance.transform.localRotation = Quaternion.Euler(def.EquipLocalEuler);
            currentToolInstance.transform.localScale = def.EquipLocalScale;

            Debug.Log($"[PlayerEquipmentVisual] Spawned '{currentToolInstance.name}' under '{rightHandCombatAnchor.name}'");
        }
    }
}