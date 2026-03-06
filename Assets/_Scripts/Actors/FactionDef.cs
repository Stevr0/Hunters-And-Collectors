using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    /// <summary>
    /// Authoritative definition for one faction id and its meaning.
    ///
    /// ActorDef.DefaultFactionId should reference ids defined in a FactionDatabase.
    /// </summary>
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Actors/FactionDef")]
    public sealed class FactionDef : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private int factionId = 0;
        [SerializeField] private string key = "neutral";
        [SerializeField] private string displayName = "Neutral";

        [Header("Optional UI")]
        [SerializeField] private Color color = Color.white;

        public int FactionId => factionId;
        public string Key => key;
        public string DisplayName => displayName;
        public Color Color => color;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(key))
                key = "neutral";

            key = key.Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = key;
        }
#endif
    }
}
