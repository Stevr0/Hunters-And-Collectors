using System;
using System.Collections.Generic;
using System.IO;

namespace HuntersAndCollectors.Persistence
{
    public static class SaveDiscoveryService
    {
        public static IReadOnlyList<SaveFileInfo> DiscoverPlayerSaves()
        {
            return DiscoverByFolder(SavePaths.Players);
        }

        public static IReadOnlyList<SaveFileInfo> DiscoverShardSaves()
        {
            return DiscoverByFolder(SavePaths.Shards);
        }

        public static bool BackupSaveFile(string filePath, out string backupPath, out string error)
        {
            backupPath = string.Empty;
            error = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    error = "Save file was not found.";
                    return false;
                }

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string directory = Path.GetDirectoryName(filePath) ?? SavePaths.Root;
                string name = Path.GetFileNameWithoutExtension(filePath);
                string extension = Path.GetExtension(filePath);
                backupPath = Path.Combine(directory, $"{name}.backup_{timestamp}{extension}");
                File.Copy(filePath, backupPath, false);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool DeleteSaveFile(string filePath, out string error)
        {
            error = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    error = "Save file was not found.";
                    return false;
                }

                File.Delete(filePath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static IReadOnlyList<SaveFileInfo> DiscoverByFolder(string folderPath)
        {
            SavePaths.EnsureDirectories();

            var results = new List<SaveFileInfo>();
            if (!Directory.Exists(folderPath))
                return results;

            string[] files = Directory.GetFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string key = Path.GetFileNameWithoutExtension(file);
                DateTime lastModified = File.GetLastWriteTimeUtc(file);
                int schemaVersion = SavePaths.ReadSchemaVersionOrDefault(file, -1);
                bool corrupted = schemaVersion < 0;
                string parseStatus = corrupted ? "Unreadable or missing schemaVersion" : "OK";

                results.Add(new SaveFileInfo(key, file, lastModified, schemaVersion, true, corrupted, parseStatus));
            }

            results.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return results;
        }
    }
}
