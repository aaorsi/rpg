using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class NpcConversationSummary
    {
        public string summary;
        public List<string> learnedFacts = new List<string>();
        public List<string> openThreads = new List<string>();
        public string relationshipShift = "neutral";
        public string createdUtc;
    }

    public sealed class NpcSummaryRepository
    {
        readonly string _root;
        readonly object _lock = new object();

        public static void ClearAllForNewPlaySession(string root = null)
        {
            var dir = string.IsNullOrWhiteSpace(root)
                ? Path.Combine(Application.persistentDataPath, "RpgNpcSummaries")
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
                Debug.LogWarning($"[NpcSummaryRepository] Clear failed: {ex.Message}");
            }
        }

        public NpcSummaryRepository(string root = null)
        {
            _root = string.IsNullOrWhiteSpace(root)
                ? Path.Combine(Application.persistentDataPath, "RpgNpcSummaries")
                : root;
        }

        public void Save(string npcId, NpcConversationSummary summary)
        {
            if (summary == null)
                return;
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";
            summary.createdUtc ??= DateTime.UtcNow.ToString("o");
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(_root);
                    var path = Path.Combine(_root, Sanitize(npcId) + ".json");
                    File.WriteAllText(path, JsonConvert.SerializeObject(summary, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NpcSummaryRepository] Save failed: {ex.Message}");
                }
            }
        }

        public NpcConversationSummary Load(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";
            lock (_lock)
            {
                try
                {
                    var path = Path.Combine(_root, Sanitize(npcId) + ".json");
                    if (!File.Exists(path))
                        return null;
                    return JsonConvert.DeserializeObject<NpcConversationSummary>(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NpcSummaryRepository] Load failed: {ex.Message}");
                    return null;
                }
            }
        }

        static string Sanitize(string s)
        {
            var safe = s.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            return safe;
        }
    }
}
