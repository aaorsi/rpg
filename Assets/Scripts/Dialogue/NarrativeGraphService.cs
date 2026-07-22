using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class NarrativeGraphSection
    {
        public string id;
        public string milestoneId;
        public string objective;
        public int priority;
        public List<string> activeWhileStatus = new List<string>();
        public List<string> allowedActionTypes = new List<string>();
        public List<string> redirectHints = new List<string>();
    }

    [Serializable]
    sealed class NarrativeGraphDocument
    {
        public int schemaVersion = 1;
        public List<NarrativeGraphSection> sections = new List<NarrativeGraphSection>();
    }

    /// <summary>
    /// Resolves active narrative section from quest milestone state (Option A hero dialogue spine).
    /// </summary>
    public sealed class NarrativeGraphService
    {
        readonly NarrativeGraphDocument _doc;

        public NarrativeGraphService(string streamingAssetsRoot = null)
        {
            _doc = LoadDoc(streamingAssetsRoot);
        }

        public NarrativeGraphSection ResolveActiveSection(IReadOnlyList<MilestoneStateEntry> milestones)
        {
            if (_doc?.sections == null || _doc.sections.Count == 0 || milestones == null)
                return null;

            NarrativeGraphSection best = null;
            var bestPriority = int.MinValue;
            for (var i = 0; i < _doc.sections.Count; i++)
            {
                var section = _doc.sections[i];
                if (section == null || string.IsNullOrWhiteSpace(section.milestoneId))
                    continue;
                var entry = milestones.FirstOrDefault(m =>
                    m != null && string.Equals(m.milestoneId, section.milestoneId, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    continue;
                if (!IsStatusActive(section, entry.status))
                    continue;
                if (section.priority > bestPriority)
                {
                    best = section;
                    bestPriority = section.priority;
                }
            }

            return best;
        }

        static bool IsStatusActive(NarrativeGraphSection section, MilestoneStatus status)
        {
            if (section.activeWhileStatus == null || section.activeWhileStatus.Count == 0)
                return status == MilestoneStatus.hinted || status == MilestoneStatus.unlocked;
            var wire = status.ToString();
            for (var i = 0; i < section.activeWhileStatus.Count; i++)
            {
                if (string.Equals(section.activeWhileStatus[i], wire, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public string BuildSectionContextBlock(
            NarrativeGraphSection section,
            int stallCount,
            string redirectHintOverride = null)
        {
            if (section == null)
                return string.Empty;

            var actions = section.allowedActionTypes != null && section.allowedActionTypes.Count > 0
                ? string.Join(", ", section.allowedActionTypes.Where(a => !string.IsNullOrWhiteSpace(a)))
                : "(any valid JSON action)";
            var redirect = !string.IsNullOrWhiteSpace(redirectHintOverride)
                ? redirectHintOverride.Trim()
                : PickRedirectHint(section, stallCount);

            return
                "ACTIVE_NARRATIVE_SECTION: " + section.id + "\n" +
                "SECTION_MILESTONE: " + section.milestoneId + "\n" +
                "SECTION_OBJECTIVE: " + (section.objective ?? string.Empty).Trim() + "\n" +
                "AUTHORIZED_ACTIONS: " + actions + "\n" +
                "STALL_COUNT: " + Mathf.Max(0, stallCount) + "\n" +
                "REDIRECT_HINT: " + redirect;
        }

        static string PickRedirectHint(NarrativeGraphSection section, int stallCount)
        {
            if (section.redirectHints == null || section.redirectHints.Count == 0)
                return "Offer one concrete next step tied to the section objective.";
            var index = Mathf.Clamp(stallCount, 0, section.redirectHints.Count - 1);
            return section.redirectHints[index];
        }

        static NarrativeGraphDocument LoadDoc(string streamingAssetsRoot)
        {
            var root = string.IsNullOrWhiteSpace(streamingAssetsRoot)
                ? Application.streamingAssetsPath
                : streamingAssetsRoot;
            var path = Path.Combine(root, "Dialogue", "narrative_graph.json");
            try
            {
                if (!File.Exists(path))
                    return new NarrativeGraphDocument();
                return JsonConvert.DeserializeObject<NarrativeGraphDocument>(File.ReadAllText(path))
                    ?? new NarrativeGraphDocument();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativeGraphService] Load failed: {ex.Message}");
                return new NarrativeGraphDocument();
            }
        }
    }
}
