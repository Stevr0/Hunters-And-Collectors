using System;
using System.Collections.Generic;
using HuntersAndCollectors.Items;
using UnityEngine;

namespace HuntersAndCollectors.Actors
{
    [CreateAssetMenu(menuName = "HuntersAndCollectors/Actors/Actor Loot Table", fileName = "ActorLootTable")]
    public sealed class ActorLootTableDef : ScriptableObject
    {
        [Serializable]
        public sealed class LootEntry
        {
            [Tooltip("Item definition that will be spawned as a world ResourceDrop when this roll succeeds.")]
            public ItemDef item;

            [Min(1)]
            [Tooltip("Minimum quantity for the spawned drop when this roll succeeds.")]
            public int minQuantity = 1;

            [Min(1)]
            [Tooltip("Maximum quantity for the spawned drop when this roll succeeds.")]
            public int maxQuantity = 1;

            [Range(0f, 1f)]
            [Tooltip("Independent chance roll (0..1) for this entry.")]
            public float dropChance01 = 1f;

            internal void Clamp()
            {
                if (minQuantity < 1) minQuantity = 1;
                if (maxQuantity < minQuantity) maxQuantity = minQuantity;
                dropChance01 = Mathf.Clamp01(dropChance01);
            }
        }

        [Header("Loot Entries")]
        [Tooltip("Each entry is rolled independently when the actor dies.")]
        [SerializeField] private List<LootEntry> entries = new();

        public IReadOnlyList<LootEntry> Entries => entries;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null)
                    continue;

                entries[i].Clamp();
            }
        }
#endif
    }
}
