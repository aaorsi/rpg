using System.Collections.Generic;
using System.Linq;

namespace Rpg.Dialogue
{
    public sealed class FailForwardService
    {
        readonly Dictionary<string, int> _milestoneStalls = new Dictionary<string, int>();

        public void NoteTurnWithoutProgress(IEnumerable<MilestoneStateEntry> milestones)
        {
            foreach (var m in milestones ?? Enumerable.Empty<MilestoneStateEntry>())
            {
                if (m == null || m.status == MilestoneStatus.completed)
                    continue;
                _milestoneStalls.TryGetValue(m.milestoneId, out var c);
                _milestoneStalls[m.milestoneId] = c + 1;
            }
        }

        public List<string> BuildEscalationSignals(IEnumerable<MilestoneStateEntry> milestones)
        {
            var signals = new List<string>();
            foreach (var m in milestones ?? Enumerable.Empty<MilestoneStateEntry>())
            {
                if (m == null || m.status == MilestoneStatus.completed)
                    continue;
                _milestoneStalls.TryGetValue(m.milestoneId, out var c);
                if (c >= 3 && m.status == MilestoneStatus.locked)
                    signals.Add("hint:" + m.milestoneId);
                if (c >= 5 && m.status != MilestoneStatus.unlocked && m.status != MilestoneStatus.completed)
                    signals.Add("unlock:" + m.milestoneId);
            }

            return signals;
        }
    }
}
