using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HuntersAndCollectors.World
{
    [CreateAssetMenu(fileName = "AreaTransferDef", menuName = "HuntersAndCollectors/World/Area Transfer Def")]
    public sealed class AreaTransferDef : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable unique id for this transfer. Use a permanent authored id such as TR_VillageToBeastCaverns.")]
        public string TransferId;

        [Tooltip("Player-facing name used by prompts and fallback messages.")]
        public string DisplayName;

        [Header("Scene Routing")]
        [Tooltip("Expected scene name where this transfer may be used. Leave blank only if the transfer is intentionally scene-agnostic.")]
        public string SourceSceneName;

        [Tooltip("Destination scene that must be loaded before the player is placed there.")]
        public string TargetSceneName;

        [Tooltip("Stable authored spawn point id inside the destination scene.")]
        public string TargetSpawnPointId;

        [Header("Requirements")]
        [Tooltip("Server-side rule used to validate whether the transfer can be used.")]
        public AreaTransferRequirementType RequirementType = AreaTransferRequirementType.None;

        [Tooltip("Required item id when the requirement uses an item. The authoritative player inventory must contain at least one.")]
        public string RequiredItemId;

        [Tooltip("Required progression flag id when the requirement uses a flag.")]
        public string RequiredFlagId;

        [Tooltip("If enabled, the required item is consumed by the server after validation succeeds.")]
        public bool ConsumeRequiredItemOnUse;

        [Header("Progression")]
        [Tooltip("If enabled, the server unlocks a progression flag for the player after the transfer succeeds.")]
        public bool UnlockFlagOnSuccess;

        [Tooltip("Per-player flag unlocked when this transfer succeeds.")]
        public string FlagToUnlockOnSuccess;

        [Header("Feedback")]
        [Tooltip("Optional success message sent to the triggering player. Leave blank to use an automatic fallback.")]
        [TextArea(2, 4)]
        public string SuccessMessage;

        [Tooltip("Optional locked message sent when requirements are not met. Leave blank to use an automatic fallback.")]
        [TextArea(2, 4)]
        public string LockedMessage;

#if UNITY_EDITOR
        private void OnValidate()
        {
            TransferId = string.IsNullOrWhiteSpace(TransferId) ? string.Empty : TransferId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? string.Empty : DisplayName.Trim();
            SourceSceneName = string.IsNullOrWhiteSpace(SourceSceneName) ? string.Empty : SourceSceneName.Trim();
            TargetSceneName = string.IsNullOrWhiteSpace(TargetSceneName) ? string.Empty : TargetSceneName.Trim();
            TargetSpawnPointId = string.IsNullOrWhiteSpace(TargetSpawnPointId) ? string.Empty : TargetSpawnPointId.Trim();
            RequiredItemId = string.IsNullOrWhiteSpace(RequiredItemId) ? string.Empty : RequiredItemId.Trim();
            RequiredFlagId = string.IsNullOrWhiteSpace(RequiredFlagId) ? string.Empty : RequiredFlagId.Trim();
            FlagToUnlockOnSuccess = string.IsNullOrWhiteSpace(FlagToUnlockOnSuccess) ? string.Empty : FlagToUnlockOnSuccess.Trim();
            SuccessMessage = string.IsNullOrWhiteSpace(SuccessMessage) ? string.Empty : SuccessMessage.Trim();
            LockedMessage = string.IsNullOrWhiteSpace(LockedMessage) ? string.Empty : LockedMessage.Trim();

            if (RequirementType != AreaTransferRequirementType.Item && RequirementType != AreaTransferRequirementType.ItemAndFlag)
            {
                RequiredItemId = string.Empty;
                ConsumeRequiredItemOnUse = false;
            }

            if (RequirementType != AreaTransferRequirementType.Flag && RequirementType != AreaTransferRequirementType.ItemAndFlag)
                RequiredFlagId = string.Empty;

            if (!UnlockFlagOnSuccess)
                FlagToUnlockOnSuccess = string.Empty;

            ValidateRequiredFields();
            ValidateTransferIdUniqueness();
        }

        private void ValidateRequiredFields()
        {
            if (string.IsNullOrWhiteSpace(TransferId))
                Debug.LogWarning($"[AreaTransferDef] '{name}' is missing TransferId.", this);

            if (string.IsNullOrWhiteSpace(TargetSceneName))
                Debug.LogWarning($"[AreaTransferDef] '{name}' is missing TargetSceneName.", this);

            if (string.IsNullOrWhiteSpace(TargetSpawnPointId))
                Debug.LogWarning($"[AreaTransferDef] '{name}' is missing TargetSpawnPointId.", this);

            bool usesItem = RequirementType == AreaTransferRequirementType.Item || RequirementType == AreaTransferRequirementType.ItemAndFlag;
            bool usesFlag = RequirementType == AreaTransferRequirementType.Flag || RequirementType == AreaTransferRequirementType.ItemAndFlag;

            if (usesItem && string.IsNullOrWhiteSpace(RequiredItemId))
                Debug.LogWarning($"[AreaTransferDef] '{name}' requires RequiredItemId for requirement type {RequirementType}.", this);

            if (usesFlag && string.IsNullOrWhiteSpace(RequiredFlagId))
                Debug.LogWarning($"[AreaTransferDef] '{name}' requires RequiredFlagId for requirement type {RequirementType}.", this);

            if (UnlockFlagOnSuccess && string.IsNullOrWhiteSpace(FlagToUnlockOnSuccess))
                Debug.LogWarning($"[AreaTransferDef] '{name}' must set FlagToUnlockOnSuccess when UnlockFlagOnSuccess is enabled.", this);
        }

        private void ValidateTransferIdUniqueness()
        {
            if (string.IsNullOrWhiteSpace(TransferId))
                return;

            string[] guids = AssetDatabase.FindAssets("t:AreaTransferDef");
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AreaTransferDef other = AssetDatabase.LoadAssetAtPath<AreaTransferDef>(path);
                if (other == null || string.IsNullOrWhiteSpace(other.TransferId))
                    continue;

                if (!seen.Add(other.TransferId.Trim()))
                    Debug.LogWarning($"[AreaTransferDef] Duplicate TransferId '{other.TransferId}' detected while validating '{name}'.", other);
            }
        }
#endif
    }
}
