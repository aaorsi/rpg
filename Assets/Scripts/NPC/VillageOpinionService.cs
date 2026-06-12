using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Tracks villager opinions toward the hero and propagates those sentiments through bounded gossip events.
    /// </summary>
    public sealed class VillageOpinionService
    {
        public const float MinOpinion = -100f;
        public const float MaxOpinion = 100f;

        const float GossipConvergenceFactor = 0.35f;
        const float MaxOpinionShiftPerGossip = 6f;
        const float MaxTrackShiftPerGossip = 4f;
        const int MaxQueuedInteractions = 256;

        readonly Dictionary<string, NpcOpinionRecord> _recordsByNpcId =
            new Dictionary<string, NpcOpinionRecord>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<GossipInteraction> _gossipQueue = new Queue<GossipInteraction>();
        readonly HashSet<string> _queuedPairKeys = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> _removeScratch = new List<string>();

        public int VillagerCount => _recordsByNpcId.Count;
        public int PendingGossipCount => _gossipQueue.Count;

        public void SetParticipants(IReadOnlyList<string> npcIds)
        {
            _removeScratch.Clear();
            foreach (var kvp in _recordsByNpcId)
                _removeScratch.Add(kvp.Key);

            if (npcIds != null)
            {
                for (var i = 0; i < npcIds.Count; i++)
                {
                    if (!TryNormalizeNpcId(npcIds[i], out var npcId))
                        continue;

                    EnsureRecord(npcId);
                    _removeScratch.Remove(npcId);
                }
            }

            for (var i = 0; i < _removeScratch.Count; i++)
                _recordsByNpcId.Remove(_removeScratch[i]);
        }

        public float GetOpinionTowardHero(string npcId)
        {
            var record = EnsureRecord(npcId);
            return record.OpinionTowardHero;
        }

        public void ApplyHeroImpact(string observerNpcId, float opinionDelta, float leadershipDelta, float pietyDelta, float wealthDelta, float helpfulnessDelta)
        {
            var record = EnsureRecord(observerNpcId);
            record.OpinionTowardHero = ClampOpinion(record.OpinionTowardHero + opinionDelta);
            record.Leadership = ClampOpinion(record.Leadership + leadershipDelta);
            record.Piety = ClampOpinion(record.Piety + pietyDelta);
            record.Wealth = ClampOpinion(record.Wealth + wealthDelta);
            record.Helpfulness = ClampOpinion(record.Helpfulness + helpfulnessDelta);
        }

        public void QueueInteraction(string npcA, string npcB)
        {
            if (!TryNormalizeNpcId(npcA, out var left) || !TryNormalizeNpcId(npcB, out var right))
                return;
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return;
            if (_gossipQueue.Count >= MaxQueuedInteractions)
                return;

            EnsureRecord(left);
            EnsureRecord(right);

            var key = BuildPairKey(left, right);
            if (_queuedPairKeys.Contains(key))
                return;

            _gossipQueue.Enqueue(new GossipInteraction(left, right, key));
            _queuedPairKeys.Add(key);
        }

        public int ProcessGossip(int maxInteractions)
        {
            var budget = Mathf.Max(0, maxInteractions);
            var processed = 0;
            while (processed < budget && _gossipQueue.Count > 0)
            {
                var interaction = _gossipQueue.Dequeue();
                _queuedPairKeys.Remove(interaction.PairKey);

                if (!_recordsByNpcId.TryGetValue(interaction.NpcA, out var source))
                    continue;
                if (!_recordsByNpcId.TryGetValue(interaction.NpcB, out var target))
                    continue;

                BlendToward(source, target);
                BlendToward(target, source);
                processed++;
            }

            return processed;
        }

        public VillageOpinionSummary GetSummary(string npcId)
        {
            var record = EnsureRecord(npcId);
            var aggregate = ComputeAggregateStanding();
            return new VillageOpinionSummary
            {
                NpcId = record.NpcId,
                OpinionTowardHero = record.OpinionTowardHero,
                AggregateLeadership = aggregate.Leadership,
                AggregatePiety = aggregate.Piety,
                AggregateWealth = aggregate.Wealth,
                AggregateHelpfulness = aggregate.Helpfulness,
                PendingGossip = _gossipQueue.Count
            };
        }

        public List<string> BuildDeliberationContext(string npcId)
        {
            var summary = GetSummary(npcId);
            var lines = new List<string>
            {
                $"Village standing tracks: leadership {FormatSigned(summary.AggregateLeadership)}, piety {FormatSigned(summary.AggregatePiety)}, wealth {FormatSigned(summary.AggregateWealth)}, helpfulness {FormatSigned(summary.AggregateHelpfulness)}.",
                $"Local opinion for {summary.NpcId} toward hero: {FormatSigned(summary.OpinionTowardHero)} ({DescribeBand(summary.OpinionTowardHero)})."
            };

            if (summary.PendingGossip > 0)
                lines.Add($"Pending gossip interactions: {summary.PendingGossip}.");
            return lines;
        }

        void BlendToward(NpcOpinionRecord source, NpcOpinionRecord target)
        {
            target.OpinionTowardHero = BlendValue(target.OpinionTowardHero, source.OpinionTowardHero, MaxOpinionShiftPerGossip);
            target.Leadership = BlendValue(target.Leadership, source.Leadership, MaxTrackShiftPerGossip);
            target.Piety = BlendValue(target.Piety, source.Piety, MaxTrackShiftPerGossip);
            target.Wealth = BlendValue(target.Wealth, source.Wealth, MaxTrackShiftPerGossip);
            target.Helpfulness = BlendValue(target.Helpfulness, source.Helpfulness, MaxTrackShiftPerGossip);
        }

        static float BlendValue(float current, float source, float maxShift)
        {
            var delta = (source - current) * GossipConvergenceFactor;
            delta = Mathf.Clamp(delta, -Mathf.Abs(maxShift), Mathf.Abs(maxShift));
            return ClampOpinion(current + delta);
        }

        NpcOpinionRecord EnsureRecord(string npcId)
        {
            if (!TryNormalizeNpcId(npcId, out var normalized))
                normalized = "unknown_npc";

            if (!_recordsByNpcId.TryGetValue(normalized, out var record))
            {
                record = new NpcOpinionRecord(normalized);
                _recordsByNpcId[normalized] = record;
            }

            return record;
        }

        AggregateStanding ComputeAggregateStanding()
        {
            if (_recordsByNpcId.Count == 0)
            {
                return new AggregateStanding
                {
                    Leadership = 0f,
                    Piety = 0f,
                    Wealth = 0f,
                    Helpfulness = 0f
                };
            }

            var leadership = 0f;
            var piety = 0f;
            var wealth = 0f;
            var helpfulness = 0f;

            foreach (var kvp in _recordsByNpcId)
            {
                var r = kvp.Value;
                leadership += r.Leadership;
                piety += r.Piety;
                wealth += r.Wealth;
                helpfulness += r.Helpfulness;
            }

            var invCount = 1f / _recordsByNpcId.Count;
            return new AggregateStanding
            {
                Leadership = leadership * invCount,
                Piety = piety * invCount,
                Wealth = wealth * invCount,
                Helpfulness = helpfulness * invCount
            };
        }

        static float ClampOpinion(float value)
        {
            return Mathf.Clamp(value, MinOpinion, MaxOpinion);
        }

        static string DescribeBand(float value)
        {
            if (value >= 45f)
                return "supportive";
            if (value >= 15f)
                return "positive";
            if (value > -15f)
                return "neutral";
            if (value > -45f)
                return "skeptical";
            return "hostile";
        }

        static string FormatSigned(float value)
        {
            var rounded = Mathf.Round(value * 10f) / 10f;
            return rounded.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        }

        static bool TryNormalizeNpcId(string npcId, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(npcId))
                return false;
            normalized = npcId.Trim();
            return normalized.Length > 0;
        }

        static string BuildPairKey(string npcA, string npcB)
        {
            return string.Compare(npcA, npcB, StringComparison.OrdinalIgnoreCase) <= 0
                ? $"{npcA}|{npcB}"
                : $"{npcB}|{npcA}";
        }

        sealed class NpcOpinionRecord
        {
            public NpcOpinionRecord(string npcId)
            {
                NpcId = npcId;
            }

            public string NpcId { get; }
            public float OpinionTowardHero { get; set; }
            public float Leadership { get; set; }
            public float Piety { get; set; }
            public float Wealth { get; set; }
            public float Helpfulness { get; set; }
        }

        struct GossipInteraction
        {
            public GossipInteraction(string npcA, string npcB, string pairKey)
            {
                NpcA = npcA;
                NpcB = npcB;
                PairKey = pairKey;
            }

            public string NpcA { get; }
            public string NpcB { get; }
            public string PairKey { get; }
        }

        struct AggregateStanding
        {
            public float Leadership;
            public float Piety;
            public float Wealth;
            public float Helpfulness;
        }
    }

    public struct VillageOpinionSummary
    {
        public string NpcId;
        public float OpinionTowardHero;
        public float AggregateLeadership;
        public float AggregatePiety;
        public float AggregateWealth;
        public float AggregateHelpfulness;
        public int PendingGossip;
    }
}
