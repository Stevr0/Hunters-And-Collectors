using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    [Serializable]
    public sealed class MenuProfileMetadata
    {
        public string key = string.Empty;
        public string displayName = string.Empty;
    }

    [Serializable]
    public sealed class MenuIndexData
    {
        public List<MenuProfileMetadata> players = new();
        public List<MenuProfileMetadata> shards = new();
    }

    public static class MenuIndexService
    {
        private static string FilePath => Path.Combine(SavePaths.Root, "MenuIndex.json");

        public static MenuIndexData Load()
        {
            SavePaths.EnsureDirectories();

            try
            {
                if (!File.Exists(FilePath))
                    return new MenuIndexData();

                string json = File.ReadAllText(FilePath);
                MenuIndexData data = JsonUtility.FromJson<MenuIndexData>(json);
                return data ?? new MenuIndexData();
            }
            catch
            {
                return new MenuIndexData();
            }
        }

        public static void Save(MenuIndexData data)
        {
            SavePaths.EnsureDirectories();
            string json = JsonUtility.ToJson(data ?? new MenuIndexData(), true);
            File.WriteAllText(FilePath, json);
        }

        public static void AddPlayer(string key, string displayName)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            MenuIndexData data = Load();
            EnsureEntry(data.players, key.Trim(), displayName);
            Save(data);
        }

        public static void AddShard(string key, string displayName)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            MenuIndexData data = Load();
            EnsureEntry(data.shards, key.Trim(), displayName);
            Save(data);
        }

        private static void EnsureEntry(List<MenuProfileMetadata> target, string key, string displayName)
        {
            for (int i = 0; i < target.Count; i++)
            {
                if (string.Equals(target[i].key, key, StringComparison.Ordinal))
                    return;
            }

            target.Add(new MenuProfileMetadata
            {
                key = key,
                displayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName.Trim()
            });
        }
    }
}
