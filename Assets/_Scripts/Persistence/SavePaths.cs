using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace HuntersAndCollectors.Persistence
{
    public static class SavePaths
    {
        public const int CurrentSchemaVersion = 2;

        public static string Root => Path.Combine(Application.persistentDataPath, "HuntersAndCollectors", "Saves");
        public static string Players => Path.Combine(Root, "Players");
        public static string Shards => Path.Combine(Root, "Shards");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Players);
            Directory.CreateDirectory(Shards);
        }

        public static string PlayerPath(string playerKey)
        {
            return Path.Combine(Players, SanitizeFileName(playerKey) + ".json");
        }

        public static string ShardPath(string shardKey)
        {
            return Path.Combine(Shards, SanitizeFileName(shardKey) + ".json");
        }

        public static string ArchiveWithTimestamp(string filePath)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string archivePath = filePath + ".bak_" + timestamp;

            if (File.Exists(archivePath))
                archivePath += "_" + Guid.NewGuid().ToString("N");

            File.Move(filePath, archivePath);
            return archivePath;
        }

        public static bool TryReadJson<T>(string path, out T data, out string error) where T : class
        {
            data = null;
            error = string.Empty;

            try
            {
                if (!File.Exists(path))
                {
                    error = "File does not exist.";
                    return false;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                data = JsonUtility.FromJson<T>(json);

                if (data == null)
                {
                    error = "Deserialized object was null.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static int ReadSchemaVersionOrDefault(string path, int defaultValue = -1)
        {
            try
            {
                if (!File.Exists(path))
                    return defaultValue;

                string json = File.ReadAllText(path, Encoding.UTF8);
                SchemaProbe probe = JsonUtility.FromJson<SchemaProbe>(json);
                return probe != null ? probe.schemaVersion : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static void WriteJson<T>(string path, T data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unnamed";

            string sanitized = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                sanitized = sanitized.Replace(invalid, '_');

            return string.IsNullOrWhiteSpace(sanitized) ? "Unnamed" : sanitized;
        }
    }
}

