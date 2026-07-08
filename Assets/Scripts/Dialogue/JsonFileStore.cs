using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Deep JSON persistence module: owns file naming, locking, load/save fallback, and warning behavior.
    /// Callers keep domain-specific normalization at their own interface.
    /// </summary>
    public sealed class JsonFileStore
    {
        readonly object _fileLock = new object();
        readonly string _rootDir;
        readonly string _logTag;

        public JsonFileStore(string rootDirectory, string logTag)
        {
            _rootDir = rootDirectory;
            _logTag = string.IsNullOrWhiteSpace(logTag) ? "JsonFileStore" : logTag.Trim();
        }

        public T Load<T>(string key, Func<T> fallbackFactory, Action<T> normalize = null) where T : class
        {
            lock (_fileLock)
            {
                return LoadUnlocked(key, fallbackFactory, normalize);
            }
        }

        public void Save<T>(string key, T doc) where T : class
        {
            lock (_fileLock)
            {
                SaveUnlocked(key, doc);
            }
        }

        public void Update<T>(
            string key,
            Func<T> fallbackFactory,
            Func<T, T> mutate,
            Action<T> normalizeOnLoad = null,
            Action<T> normalizeOnSave = null) where T : class
        {
            if (mutate == null)
                return;

            lock (_fileLock)
            {
                var doc = LoadUnlocked(key, fallbackFactory, normalizeOnLoad);
                doc = mutate(doc);
                normalizeOnSave?.Invoke(doc);
                SaveUnlocked(key, doc);
            }
        }

        public static void ClearAllJsonFiles(string directory, string logTag)
        {
            var tag = string.IsNullOrWhiteSpace(logTag) ? "JsonFileStore" : logTag.Trim();
            try
            {
                if (!Directory.Exists(directory))
                    return;
                foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[{tag}] Could not delete '{path}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{tag}] Could not clear directory '{directory}': {ex.Message}");
            }
        }

        T LoadUnlocked<T>(string key, Func<T> fallbackFactory, Action<T> normalize) where T : class
        {
            var fallback = fallbackFactory ?? (() => null);
            var path = FilePathFor(key);
            if (!File.Exists(path))
                return fallback();

            try
            {
                var json = File.ReadAllText(path);
                var doc = JsonConvert.DeserializeObject<T>(json);
                if (doc == null)
                    return fallback();
                normalize?.Invoke(doc);
                return doc;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{_logTag}] Failed to load '{path}': {ex.Message}");
                return fallback();
            }
        }

        void SaveUnlocked<T>(string key, T doc) where T : class
        {
            if (doc == null)
                return;

            try
            {
                Directory.CreateDirectory(_rootDir);
                var path = FilePathFor(key);
                var json = JsonConvert.SerializeObject(doc, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{_logTag}] Failed to save '{key}': {ex.Message}");
            }
        }

        string FilePathFor(string key)
        {
            var safe = SanitizeFileName(key);
            return Path.Combine(_rootDir, $"{safe}.json");
        }

        static string SanitizeFileName(string value)
        {
            var safe = string.IsNullOrWhiteSpace(value) ? "npc_unknown" : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            return safe;
        }
    }
}
