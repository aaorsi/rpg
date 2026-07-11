using System;
using System.Collections.Generic;

namespace Rpg.Npc
{
    public readonly struct VillageInteractionRejectEvent
    {
        public VillageInteractionRejectEvent(
            float atTime,
            string interactionInstanceId,
            string interactionId,
            string actorNpcId,
            string reason)
        {
            AtTime = atTime;
            InteractionInstanceId = interactionInstanceId ?? string.Empty;
            InteractionId = interactionId ?? string.Empty;
            ActorNpcId = actorNpcId ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public float AtTime { get; }
        public string InteractionInstanceId { get; }
        public string InteractionId { get; }
        public string ActorNpcId { get; }
        public string Reason { get; }
    }

    public sealed class VillageInteractionDebugEventLog
    {
        readonly List<VillageInteractionRejectEvent> _rejectEvents = new List<VillageInteractionRejectEvent>();
        readonly int _maxEvents;

        public VillageInteractionDebugEventLog(int maxEvents = 32)
        {
            _maxEvents = Math.Max(4, maxEvents);
        }

        public IReadOnlyList<VillageInteractionRejectEvent> RejectEvents => _rejectEvents;

        public void RecordReject(VillageInteractionRejectEvent entry)
        {
            _rejectEvents.Add(entry);
            while (_rejectEvents.Count > _maxEvents)
                _rejectEvents.RemoveAt(0);
        }
    }
}
