using System.Collections.Generic;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Deterministic post-turn check: did the hero conversation advance the active narrative section?
    /// </summary>
    public sealed class TurnAuthorizer
    {
        readonly Dictionary<string, int> _stallBySectionId = new Dictionary<string, int>();

        public bool DidTurnMakeProgress(AssistantModelPayload payload)
        {
            if (payload == null)
                return false;
            if (!string.IsNullOrWhiteSpace(payload.Say))
            {
                if (payload.MilestoneSignals != null && payload.MilestoneSignals.Count > 0)
                    return true;
                if (payload.ProposedActions != null && payload.ProposedActions.Count > 0)
                    return true;
                if (payload.MemoryAdds != null && payload.MemoryAdds.Count > 0)
                    return true;
                var outcome = payload.InteractionOutcome ?? string.Empty;
                if (outcome.IndexOf("cooperate", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || outcome.IndexOf("accept", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || outcome.IndexOf("trade", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        public void NoteTurnResult(NarrativeGraphSection section, AssistantModelPayload payload)
        {
            if (section == null || string.IsNullOrWhiteSpace(section.id))
                return;
            if (DidTurnMakeProgress(payload))
            {
                _stallBySectionId[section.id] = 0;
                return;
            }

            _stallBySectionId.TryGetValue(section.id, out var count);
            _stallBySectionId[section.id] = count + 1;
        }

        public int GetStallCount(NarrativeGraphSection section)
        {
            if (section == null || string.IsNullOrWhiteSpace(section.id))
                return 0;
            return _stallBySectionId.TryGetValue(section.id, out var count) ? count : 0;
        }

        public bool ShouldEscalate(NarrativeGraphSection section, int threshold = 3)
        {
            return GetStallCount(section) >= Mathf.Max(1, threshold);
        }
    }
}
