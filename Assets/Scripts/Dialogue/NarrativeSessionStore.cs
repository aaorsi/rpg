using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Dialogue
{
    public sealed class NarrativeSessionStore
    {
        readonly string _root;

        public static void ClearAllForNewPlaySession(string root = null)
        {
            var dir = string.IsNullOrWhiteSpace(root)
                ? Path.Combine(Application.persistentDataPath, "Generated")
                : root;
            try
            {
                if (!Directory.Exists(dir))
                    return;
                foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try { File.Delete(path); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativeSessionStore] Clear failed: {ex.Message}");
            }
        }

        public NarrativeSessionStore(string root = null)
        {
            _root = string.IsNullOrWhiteSpace(root)
                ? Path.Combine(Application.persistentDataPath, "Generated")
                : root;
        }

        public void Save(NarrativeSessionCanon canon)
        {
            if (canon == null)
                return;
            try
            {
                Directory.CreateDirectory(_root);
                var safe = string.IsNullOrWhiteSpace(canon.sessionId) ? canon.seed.ToString() : canon.sessionId.Trim();
                var path = Path.Combine(_root, $"session_canon_{safe}.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(canon, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativeSessionStore] Save failed: {ex.Message}");
            }
        }

        public NarrativeSessionCanon LoadLatest()
        {
            try
            {
                if (!Directory.Exists(_root))
                    return null;
                var files = Directory.GetFiles(_root, "session_canon_*.json");
                if (files.Length == 0)
                    return null;
                Array.Sort(files, StringComparer.Ordinal);
                var latest = files[^1];
                return JsonConvert.DeserializeObject<NarrativeSessionCanon>(File.ReadAllText(latest));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativeSessionStore] Load failed: {ex.Message}");
                return null;
            }
        }
    }
}
