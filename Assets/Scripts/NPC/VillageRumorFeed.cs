using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Npc
{
    [Serializable]
    sealed class VillageRumorFeedDocument
    {
        public int schemaVersion = 1;
        public List<VillageConsequenceEvent> events = new List<VillageConsequenceEvent>();
    }

    /// <summary>
    /// Player-visible village consequence log (templates, no LLM).
    /// </summary>
    public sealed class VillageRumorFeed
    {
        const int MaxEvents = 24;

        readonly string _path;
        readonly object _lock = new object();
        readonly List<VillageConsequenceEvent> _events = new List<VillageConsequenceEvent>();

        public VillageRumorFeed(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, "RpgVillageRumors", "rumor_feed.json")
                : path;
            Load();
        }

        public static void ClearAllForNewPlaySession(string path = null)
        {
            var p = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, "RpgVillageRumors", "rumor_feed.json")
                : path;
            try
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VillageRumorFeed] Clear failed: {ex.Message}");
            }
        }

        public void Enqueue(VillageConsequenceEvent consequence)
        {
            if (consequence == null || string.IsNullOrWhiteSpace(consequence.rumorText))
                return;
            lock (_lock)
            {
                _events.Add(Clone(consequence));
                while (_events.Count > MaxEvents)
                    _events.RemoveAt(0);
                Save();
            }
        }

        public List<VillageConsequenceEvent> SnapshotRecent(int count)
        {
            lock (_lock)
            {
                var take = Mathf.Clamp(count, 1, MaxEvents);
                if (_events.Count <= take)
                    return _events.Select(Clone).ToList();
                return _events.Skip(_events.Count - take).Select(Clone).ToList();
            }
        }

        public VillageConsequenceEvent GetLatestForNpc(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return null;
            lock (_lock)
            {
                for (var i = _events.Count - 1; i >= 0; i--)
                {
                    var e = _events[i];
                    if (e == null)
                        continue;
                    if (string.Equals(e.actorNpcId, npcId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.targetNpcId, npcId, StringComparison.OrdinalIgnoreCase))
                        return Clone(e);
                }
            }

            return null;
        }

        public string BuildFactsBlock(int maxLines = 4)
        {
            lock (_lock)
            {
                if (_events.Count == 0)
                    return "(No recent village rumors.)";
                var lines = new List<string>();
                for (var i = _events.Count - 1; i >= 0 && lines.Count < maxLines; i--)
                {
                    var e = _events[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.rumorText))
                        continue;
                    lines.Add("- " + e.rumorText.Trim());
                }

                if (lines.Count == 0)
                    return "(No recent village rumors.)";
                lines.Reverse();
                return string.Join("\n", lines);
            }
        }

        static VillageConsequenceEvent Clone(VillageConsequenceEvent source)
        {
            if (source == null)
                return null;
            return new VillageConsequenceEvent
            {
                eventId = source.eventId,
                eventType = source.eventType,
                timestampUtc = source.timestampUtc,
                actorNpcId = source.actorNpcId,
                targetNpcId = source.targetNpcId,
                actorDisplayName = source.actorDisplayName,
                targetDisplayName = source.targetDisplayName,
                rumorText = source.rumorText,
                opinionDeltaTowardHero = source.opinionDeltaTowardHero
            };
        }

        void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                        return;
                    var parsed = JsonConvert.DeserializeObject<VillageRumorFeedDocument>(File.ReadAllText(_path));
                    _events.Clear();
                    if (parsed?.events != null)
                        _events.AddRange(parsed.events.Where(e => e != null && !string.IsNullOrWhiteSpace(e.rumorText)));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VillageRumorFeed] Load failed: {ex.Message}");
                }
            }
        }

        void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                var doc = new VillageRumorFeedDocument { events = _events.ToList() };
                File.WriteAllText(_path, JsonConvert.SerializeObject(doc, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VillageRumorFeed] Save failed: {ex.Message}");
            }
        }
    }
}
