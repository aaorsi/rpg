using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Selects NPCs for deliberation at a fixed cadence, with optional event-priority requests.
    /// </summary>
    public sealed class VillageDeliberationScheduler
    {
        readonly List<string> _orderedNpcIds = new List<string>();
        readonly Queue<PriorityRequest> _priorityQueue = new Queue<PriorityRequest>();
        readonly HashSet<string> _queuedNpcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _indexByNpcId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int _nextRoundRobinIndex;
        float _nextAllowedDeliberationAt;

        public VillageDeliberationScheduler(float cadenceSeconds)
        {
            CadenceSeconds = cadenceSeconds;
        }

        public float CadenceSeconds { get; set; }
        public int RegisteredCount => _orderedNpcIds.Count;

        public void SetParticipants(IReadOnlyList<string> npcIds)
        {
            var nextNpcId = _orderedNpcIds.Count > 0 && _nextRoundRobinIndex >= 0 && _nextRoundRobinIndex < _orderedNpcIds.Count
                ? _orderedNpcIds[_nextRoundRobinIndex]
                : null;

            _orderedNpcIds.Clear();
            _indexByNpcId.Clear();
            _nextRoundRobinIndex = -1;

            if (npcIds == null)
                return;

            for (var i = 0; i < npcIds.Count; i++)
            {
                var npcId = npcIds[i];
                if (string.IsNullOrWhiteSpace(npcId))
                    continue;

                var key = npcId.Trim();
                if (_indexByNpcId.ContainsKey(key))
                    continue;

                _indexByNpcId[key] = _orderedNpcIds.Count;
                _orderedNpcIds.Add(key);
            }

            if (_orderedNpcIds.Count == 0)
            {
                _nextRoundRobinIndex = 0;
                return;
            }

            if (!string.IsNullOrWhiteSpace(nextNpcId) && _indexByNpcId.TryGetValue(nextNpcId, out var preserved))
                _nextRoundRobinIndex = preserved;
            if (_nextRoundRobinIndex < 0)
                _nextRoundRobinIndex = 0;
        }

        public void RequestImmediate(string npcId, string reason)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return;
            if (!_indexByNpcId.ContainsKey(npcId.Trim()))
                return;
            var key = npcId.Trim();
            if (_queuedNpcIds.Contains(key))
                return;

            _priorityQueue.Enqueue(new PriorityRequest
            {
                NpcId = key,
                Reason = string.IsNullOrWhiteSpace(reason) ? "event" : reason.Trim()
            });
            _queuedNpcIds.Add(key);
        }

        public bool TryAcquire(float nowSeconds, out string npcId, out string reason)
        {
            npcId = string.Empty;
            reason = string.Empty;

            if (_orderedNpcIds.Count == 0)
                return false;
            if (nowSeconds < _nextAllowedDeliberationAt)
                return false;

            while (_priorityQueue.Count > 0)
            {
                var req = _priorityQueue.Dequeue();
                _queuedNpcIds.Remove(req.NpcId);
                if (!_indexByNpcId.ContainsKey(req.NpcId))
                    continue;

                npcId = req.NpcId;
                reason = req.Reason;
                _nextAllowedDeliberationAt = nowSeconds + SafeCadenceSeconds();
                return true;
            }

            if (_nextRoundRobinIndex < 0 || _nextRoundRobinIndex >= _orderedNpcIds.Count)
                _nextRoundRobinIndex = 0;

            npcId = _orderedNpcIds[_nextRoundRobinIndex];
            reason = "round_robin";
            _nextRoundRobinIndex = (_nextRoundRobinIndex + 1) % _orderedNpcIds.Count;
            _nextAllowedDeliberationAt = nowSeconds + SafeCadenceSeconds();
            return true;
        }

        float SafeCadenceSeconds()
        {
            return Mathf.Max(0.05f, CadenceSeconds);
        }

        struct PriorityRequest
        {
            public string NpcId;
            public string Reason;
        }
    }
}
