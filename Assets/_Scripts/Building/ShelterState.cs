using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Thin compatibility wrapper kept so existing scene references and older code paths do not break
    /// during migration from the shelter-specific rule to the generic structure requirement system.
    ///
    /// Real shelter logic now lives in StructureRequirementController + StructureRequirementDef.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShelterState : MonoBehaviour
    {
        [SerializeField] private StructureRequirementController requirementController;

        /// <summary>
        /// Backward-compatible read-only completion access for older code that still queries ShelterState.
        /// </summary>
        public bool IsComplete => requirementController != null && requirementController.IsComplete;

        private void OnEnable()
        {
            if (requirementController == null)
                requirementController = GetComponent<StructureRequirementController>();

            requirementController?.ServerReevaluate();
        }

        /// <summary>
        /// Backward-compatible wrapper for older callers.
        /// New code should call StructureRequirementController.ServerReevaluate directly.
        /// </summary>
        public void ServerReevaluateShelter()
        {
            if (requirementController == null)
                requirementController = GetComponent<StructureRequirementController>();

            if (requirementController == null)
            {
                Debug.LogWarning("[ShelterState] Missing StructureRequirementController. Shelter wrapper cannot re-evaluate.", this);
                return;
            }

            requirementController.ServerReevaluate();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (requirementController == null)
                requirementController = GetComponent<StructureRequirementController>();
        }
#endif
    }
}
