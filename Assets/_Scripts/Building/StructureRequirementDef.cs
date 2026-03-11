using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// Reusable definition data for build-driven world unlock rules.
    ///
    /// Important architecture rule:
    /// - This asset is definition data only.
    /// - Runtime completion state belongs on StructureRequirementController.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StructureRequirementDef",
        menuName = "HuntersAndCollectors/Building/Structure Requirement Def")]
    public sealed class StructureRequirementDef : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string requirementId = "REQ_NewRequirement";

        [Header("Scan")]
        [Min(0f)]
        [SerializeField] private float radius = 10f;

        [Header("Requirements")]
        [SerializeField] private StructureRequirementEntry[] requiredEntries = new StructureRequirementEntry[0];

        public string RequirementId => requirementId;
        public float Radius => radius;
        public StructureRequirementEntry[] RequiredEntries => requiredEntries;

#if UNITY_EDITOR
        private void OnValidate()
        {
            requirementId = string.IsNullOrWhiteSpace(requirementId)
                ? "REQ_NewRequirement"
                : requirementId.Trim();

            radius = Mathf.Max(0f, radius);

            if (requiredEntries == null)
                requiredEntries = new StructureRequirementEntry[0];

            var seenIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < requiredEntries.Length; i++)
            {
                StructureRequirementEntry entry = requiredEntries[i];
                entry.Sanitize();
                requiredEntries[i] = entry;

                if (string.IsNullOrWhiteSpace(entry.SourceItemId))
                    continue;

                if (!seenIds.Add(entry.SourceItemId))
                {
                    Debug.LogWarning(
                        $"[StructureRequirementDef] Duplicate SourceItemId '{entry.SourceItemId}' in '{name}'. " +
                        "Duplicate rows are allowed but usually indicate inspector setup mistakes.",
                        this);
                }
            }
        }
#endif
    }
}
