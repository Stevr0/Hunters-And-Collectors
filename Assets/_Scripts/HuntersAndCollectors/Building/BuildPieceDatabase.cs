using System;
using System.Collections.Generic;
using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// BuildPieceDatabase
    /// --------------------------------------------------------------------
    /// Small lookup database for build piece definitions.
    ///
    /// First-pass scope:
    /// - Serialized list for easy authoring.
    /// - Runtime dictionary for fast BuildPieceId lookup.
    /// - Duplicate ID warning protection.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Building/Build Piece Database", fileName = "BuildPieceDatabase")]
    public sealed class BuildPieceDatabase : ScriptableObject
    {
        [SerializeField] private List<BuildPieceDef> buildPieces = new();

        private readonly Dictionary<string, BuildPieceDef> byId =
            new(StringComparer.OrdinalIgnoreCase);

        private bool isLookupBuilt;

        /// <summary>
        /// Attempts to resolve a build piece definition by BuildPieceId.
        /// </summary>
        public bool TryGet(string buildPieceId, out BuildPieceDef def)
        {
            def = null;

            if (string.IsNullOrWhiteSpace(buildPieceId))
                return false;

            EnsureLookupBuilt();
            return byId.TryGetValue(buildPieceId, out def) && def != null;
        }

        /// <summary>
        /// Resolves a build piece definition or throws with a clear message.
        /// </summary>
        public BuildPieceDef GetOrThrow(string buildPieceId)
        {
            if (TryGet(buildPieceId, out BuildPieceDef def))
                return def;

            throw new KeyNotFoundException($"BuildPieceId '{buildPieceId}' was not found in BuildPieceDatabase '{name}'.");
        }

        private void EnsureLookupBuilt()
        {
            if (isLookupBuilt)
                return;

            RebuildLookup();
        }

        private void RebuildLookup()
        {
            byId.Clear();

            for (int i = 0; i < buildPieces.Count; i++)
            {
                BuildPieceDef def = buildPieces[i];
                if (def == null)
                    continue;

                string id = def.BuildPieceId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning($"[BuildPieceDatabase] Ignoring entry with empty BuildPieceId at index {i} in '{name}'.", this);
                    continue;
                }

                if (byId.ContainsKey(id))
                {
                    Debug.LogWarning($"[BuildPieceDatabase] Duplicate BuildPieceId '{id}' in '{name}'. Keeping first entry.", this);
                    continue;
                }

                byId[id] = def;
            }

            isLookupBuilt = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Rebuild lazily on next query so runtime lookup always reflects latest asset edits.
            isLookupBuilt = false;
        }
#endif
    }
}
