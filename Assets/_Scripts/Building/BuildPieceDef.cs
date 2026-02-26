using UnityEngine;

namespace HuntersAndCollectors.Building
{
    /// <summary>
    /// ScriptableObject mapping build piece id to spawned prefab reference.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Building/Build Piece Def", fileName = "BuildPieceDef")]
    public sealed class BuildPieceDef : ScriptableObject
    {
        public string BuildPieceId = "BP_Floor";
        public GameObject Prefab;
    }
}