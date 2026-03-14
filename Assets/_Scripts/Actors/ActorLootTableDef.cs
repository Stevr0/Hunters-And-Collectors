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
        public class LootEntry
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

        [Serializable]
        public sealed class WeightedLootEntry : LootEntry
        {
            [Min(0f)]
            [Tooltip("Relative weight used when this row participates in a weighted loot roll.")]
            public float weight = 1f;

            internal new void Clamp()
            {
                base.Clamp();
                weight = Mathf.Max(0f, weight);
            }
        }

        [Serializable]
        public struct CoinDropSettings
        {
            [Tooltip("Optional explicit coin item. Leave empty to let runtime fall back to the project's coin item.")]
            public ItemDef coinItem;

            [Min(0)]
            public int minQuantity;

            [Min(0)]
            public int maxQuantity;

            [Range(0f, 1f)]
            public float dropChance01;

            public bool IsEnabled => coinItem != null && maxQuantity > 0 && dropChance01 > 0f;

            public void Clamp()
            {
                minQuantity = Mathf.Max(0, minQuantity);
                maxQuantity = Mathf.Max(minQuantity, maxQuantity);
                dropChance01 = Mathf.Clamp01(dropChance01);
            }
        }

        public struct ResolvedLootDrop
        {
            public ItemDef item;
            public int quantity;
        }

        [Header("Legacy Independent Entries")]
        [Tooltip("Existing project behavior: each entry is rolled independently when the actor dies.")]
        [SerializeField] private List<LootEntry> entries = new();

        [Header("Guaranteed Drops")]
        [SerializeField] private List<LootEntry> guaranteedDrops = new();

        [Header("Weighted Random Drops")]
        [SerializeField] private List<WeightedLootEntry> weightedDrops = new();
        [Min(0)]
        [SerializeField] private int weightedRollsMin = 0;
        [Min(0)]
        [SerializeField] private int weightedRollsMax = 0;

        [Header("Optional Coin Drop")]
        [SerializeField] private CoinDropSettings coinDrop;

        public IReadOnlyList<LootEntry> Entries => entries;
        public IReadOnlyList<LootEntry> GuaranteedDrops => guaranteedDrops;
        public IReadOnlyList<WeightedLootEntry> WeightedDrops => weightedDrops;
        public CoinDropSettings CoinDrop => coinDrop;

        public int RollLoot(List<ResolvedLootDrop> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            int startCount = results.Count;

            AppendIndependentEntries(guaranteedDrops, results);
            AppendIndependentEntries(entries, results);
            AppendWeightedRolls(results);

            return results.Count - startCount;
        }

        public bool TryRollCoins(out ItemDef coinItem, out int quantity)
        {
            coinItem = null;
            quantity = 0;

            if (!coinDrop.IsEnabled)
                return false;

            if (UnityEngine.Random.value > coinDrop.dropChance01)
                return false;

            coinItem = coinDrop.coinItem;
            quantity = UnityEngine.Random.Range(coinDrop.minQuantity, coinDrop.maxQuantity + 1);
            return coinItem != null && quantity > 0;
        }

        private static void AppendIndependentEntries(List<LootEntry> source, List<ResolvedLootDrop> results)
        {
            if (source == null || source.Count == 0)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                LootEntry entry = source[i];
                if (entry == null || entry.item == null)
                    continue;

                if (UnityEngine.Random.value > Mathf.Clamp01(entry.dropChance01))
                    continue;

                int quantity = UnityEngine.Random.Range(Mathf.Max(1, entry.minQuantity), Mathf.Max(Mathf.Max(1, entry.minQuantity), entry.maxQuantity) + 1);
                AddResolvedDrop(results, entry.item, quantity);
            }
        }

        private void AppendWeightedRolls(List<ResolvedLootDrop> results)
        {
            if (weightedDrops == null || weightedDrops.Count == 0)
                return;

            int minRolls = Mathf.Max(0, weightedRollsMin);
            int maxRolls = Mathf.Max(minRolls, weightedRollsMax);
            int rollCount = UnityEngine.Random.Range(minRolls, maxRolls + 1);
            if (rollCount <= 0)
                return;

            float totalWeight = 0f;
            for (int i = 0; i < weightedDrops.Count; i++)
            {
                WeightedLootEntry entry = weightedDrops[i];
                if (entry == null || entry.item == null)
                    continue;

                totalWeight += Mathf.Max(0f, entry.weight);
            }

            if (totalWeight <= 0f)
                return;

            for (int roll = 0; roll < rollCount; roll++)
            {
                float choice = UnityEngine.Random.value * totalWeight;
                float cursor = 0f;

                for (int i = 0; i < weightedDrops.Count; i++)
                {
                    WeightedLootEntry entry = weightedDrops[i];
                    if (entry == null || entry.item == null || entry.weight <= 0f)
                        continue;

                    cursor += entry.weight;
                    if (choice > cursor)
                        continue;

                    if (UnityEngine.Random.value <= Mathf.Clamp01(entry.dropChance01))
                    {
                        int quantity = UnityEngine.Random.Range(Mathf.Max(1, entry.minQuantity), Mathf.Max(Mathf.Max(1, entry.minQuantity), entry.maxQuantity) + 1);
                        AddResolvedDrop(results, entry.item, quantity);
                    }

                    break;
                }
            }
        }

        private static void AddResolvedDrop(List<ResolvedLootDrop> results, ItemDef item, int quantity)
        {
            if (item == null || quantity <= 0)
                return;

            for (int i = 0; i < results.Count; i++)
            {
                ResolvedLootDrop existing = results[i];
                if (existing.item != item)
                    continue;

                existing.quantity += quantity;
                results[i] = existing;
                return;
            }

            results.Add(new ResolvedLootDrop
            {
                item = item,
                quantity = quantity
            });
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            coinDrop.Clamp();
            weightedRollsMin = Mathf.Max(0, weightedRollsMin);
            weightedRollsMax = Mathf.Max(weightedRollsMin, weightedRollsMax);
            ClampEntries(entries);
            ClampEntries(guaranteedDrops);

            if (weightedDrops == null)
                return;

            for (int i = 0; i < weightedDrops.Count; i++)
            {
                if (weightedDrops[i] == null)
                    continue;

                weightedDrops[i].Clamp();
            }
        }

        private static void ClampEntries(List<LootEntry> source)
        {
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == null)
                    continue;

                source[i].Clamp();
            }
        }
#endif
    }
}
