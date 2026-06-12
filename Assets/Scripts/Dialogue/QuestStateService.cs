using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Dialogue
{
    public enum MilestoneStatus
    {
        locked,
        hinted,
        unlocked,
        completed
    }

    [Serializable]
    public sealed class MilestoneStateEntry
    {
        public string milestoneId;
        public MilestoneStatus status;
        public string source;
        public int routeCountUsed;
        public string updatedUtc;
    }

    [Serializable]
    sealed class QuestStateDocument
    {
        public int schemaVersion = 1;
        public string sessionId;
        public List<MilestoneStateEntry> milestones = new List<MilestoneStateEntry>();
    }

    public sealed class QuestStateService
    {
        readonly string _path;
        readonly object _lock = new object();
        QuestStateDocument _doc = new QuestStateDocument();

        public static void ClearAllForNewPlaySession(string path = null)
        {
            var p = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, "RpgQuestState", "quest_state.json")
                : path;
            try
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QuestStateService] Clear failed: {ex.Message}");
            }
        }

        public QuestStateService(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, "RpgQuestState", "quest_state.json")
                : path;
            Load();
        }

        public void InitializeFromCanon(NarrativeSessionCanon canon)
        {
            if (canon == null)
                return;
            lock (_lock)
            {
                _doc.sessionId = canon.sessionId;
                _doc.milestones ??= new List<MilestoneStateEntry>();
                foreach (var m in canon.criticalMilestones ?? new List<string>())
                {
                    if (_doc.milestones.Any(x => string.Equals(x.milestoneId, m, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    _doc.milestones.Add(new MilestoneStateEntry
                    {
                        milestoneId = m,
                        status = MilestoneStatus.locked,
                        source = "canon",
                        routeCountUsed = 0,
                        updatedUtc = DateTime.UtcNow.ToString("o")
                    });
                }
                Save();
            }
        }

        public void ApplySignals(string npcId, IReadOnlyList<string> signals)
        {
            if (signals == null || signals.Count == 0)
                return;
            lock (_lock)
            {
                foreach (var raw in signals)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;
                    var sig = raw.Trim();
                    var lower = sig.ToLowerInvariant();
                    MilestoneStatus target = MilestoneStatus.hinted;
                    if (lower.StartsWith("complete:"))
                    {
                        target = MilestoneStatus.completed;
                        sig = sig.Substring("complete:".Length).Trim();
                    }
                    else if (lower.StartsWith("unlock:"))
                    {
                        target = MilestoneStatus.unlocked;
                        sig = sig.Substring("unlock:".Length).Trim();
                    }
                    else if (lower.StartsWith("hint:"))
                    {
                        target = MilestoneStatus.hinted;
                        sig = sig.Substring("hint:".Length).Trim();
                    }

                    if (string.IsNullOrWhiteSpace(sig))
                        continue;
                    var e = _doc.milestones.FirstOrDefault(x => string.Equals(x.milestoneId, sig, StringComparison.OrdinalIgnoreCase));
                    if (e == null)
                    {
                        e = new MilestoneStateEntry { milestoneId = sig, status = target, source = npcId, updatedUtc = DateTime.UtcNow.ToString("o") };
                        _doc.milestones.Add(e);
                    }
                    else if ((int)target > (int)e.status)
                    {
                        e.status = target;
                        e.source = npcId;
                        e.updatedUtc = DateTime.UtcNow.ToString("o");
                    }
                }
                Save();
            }
        }

        public List<MilestoneStateEntry> Snapshot()
        {
            lock (_lock)
                return _doc.milestones.Select(m => new MilestoneStateEntry
                {
                    milestoneId = m.milestoneId,
                    status = m.status,
                    source = m.source,
                    routeCountUsed = m.routeCountUsed,
                    updatedUtc = m.updatedUtc
                }).ToList();
        }

        void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                        return;
                    var parsed = JsonConvert.DeserializeObject<QuestStateDocument>(File.ReadAllText(_path));
                    if (parsed != null)
                        _doc = parsed;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[QuestStateService] Load failed: {ex.Message}");
                }
            }
        }

        void Save()
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(_path, JsonConvert.SerializeObject(_doc, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[QuestStateService] Save failed: {ex.Message}");
                }
            }
        }
    }
}
