using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
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
        readonly string _askStatePath;
        readonly List<VillageGroupAskDefinition> _groupAskDefinitions = new List<VillageGroupAskDefinition>();
        readonly Dictionary<string, VillageGroupAskRecord> _groupAskRecordsById =
            new Dictionary<string, VillageGroupAskRecord>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> _pendingMilestoneSignals = new Queue<string>();

        public VillageOpinionService(string askStatePath = null, IReadOnlyList<VillageGroupAskDefinition> askDefinitions = null)
        {
            _askStatePath = string.IsNullOrWhiteSpace(askStatePath)
                ? Path.Combine(Application.persistentDataPath, "RpgVillageAsks", "group_asks.json")
                : askStatePath;
            BuildGroupAskDefinitions(askDefinitions);
            LoadGroupAskState();
        }

        public int VillagerCount => _recordsByNpcId.Count;
        public int PendingGossipCount => _gossipQueue.Count;

        public static void ClearAllForNewPlaySession(string askStatePath = null)
        {
            var path = string.IsNullOrWhiteSpace(askStatePath)
                ? Path.Combine(Application.persistentDataPath, "RpgVillageAsks", "group_asks.json")
                : askStatePath;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VillageOpinionService] Failed to clear ask state file: {ex.Message}");
            }
        }

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

            EvaluateAndPersistGroupAsks();
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
            EvaluateAndPersistGroupAsks();
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

            if (processed > 0)
                EvaluateAndPersistGroupAsks();

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
            AppendGroupAskContext(lines);
            return lines;
        }

        public List<VillageGroupAskRecord> SnapshotGroupAsks()
        {
            var snapshot = new List<VillageGroupAskRecord>();
            foreach (var def in _groupAskDefinitions)
            {
                if (_groupAskRecordsById.TryGetValue(def.askId, out var rec) && rec != null)
                    snapshot.Add(CloneRecord(rec));
            }

            return snapshot;
        }

        public bool TryRespondToGroupAsk(string askId, bool accept, string responderNpcId, out List<string> milestoneSignals)
        {
            milestoneSignals = new List<string>();
            if (string.IsNullOrWhiteSpace(askId))
                return false;

            var key = askId.Trim();
            if (!_groupAskRecordsById.TryGetValue(key, out var record) || record == null)
                return false;
            if (!TryParseAskState(record.state, out var state) || state != VillageGroupAskState.Offered)
                return false;
            if (!TryGetAskDefinition(key, out var definition))
                return false;

            var now = DateTime.UtcNow.ToString("o");
            record.state = ToWire(accept ? VillageGroupAskState.Accepted : VillageGroupAskState.Declined);
            record.respondedUtc = now;
            record.lastResponderNpcId = string.IsNullOrWhiteSpace(responderNpcId) ? string.Empty : responderNpcId.Trim();

            var effects = accept ? definition.acceptEffect : definition.declineEffect;
            ApplyAggregateStandingShift(effects.leadershipDelta, effects.pietyDelta, effects.wealthDelta, effects.helpfulnessDelta);

            var signal = accept ? definition.onAcceptMilestoneSignal : definition.onDeclineMilestoneSignal;
            if (!string.IsNullOrWhiteSpace(signal))
            {
                var clean = signal.Trim();
                _pendingMilestoneSignals.Enqueue(clean);
                milestoneSignals.Add(clean);
            }

            EvaluateAndPersistGroupAsks();
            SaveGroupAskState();
            return true;
        }

        public List<string> ConsumePendingMilestoneSignals()
        {
            var consumed = new List<string>();
            while (_pendingMilestoneSignals.Count > 0)
                consumed.Add(_pendingMilestoneSignals.Dequeue());
            return consumed;
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

        void BuildGroupAskDefinitions(IReadOnlyList<VillageGroupAskDefinition> askDefinitions)
        {
            _groupAskDefinitions.Clear();
            if (askDefinitions != null && askDefinitions.Count > 0)
            {
                for (var i = 0; i < askDefinitions.Count; i++)
                {
                    var def = askDefinitions[i];
                    if (def == null || string.IsNullOrWhiteSpace(def.askId))
                        continue;
                    _groupAskDefinitions.Add(def.Clone());
                }
            }

            if (_groupAskDefinitions.Count == 0)
            {
                _groupAskDefinitions.Add(new VillageGroupAskDefinition
                {
                    askId = "ask_run_for_mayor",
                    title = "Run for mayor",
                    summary = "Villagers think your leadership can stabilize the town council.",
                    thresholds = new VillageStandingThreshold
                    {
                        minLeadership = 45f,
                        minPiety = -100f,
                        minWealth = -100f,
                        minHelpfulness = 20f
                    },
                    onOfferMilestoneSignal = "hint:m_village_mayor_arc",
                    onAcceptMilestoneSignal = "unlock:m_village_mayor_arc",
                    onDeclineMilestoneSignal = "hint:m_village_mayor_declined",
                    acceptEffect = new VillageAskStandingEffect
                    {
                        leadershipDelta = 8f,
                        pietyDelta = 0f,
                        wealthDelta = 0f,
                        helpfulnessDelta = 4f
                    },
                    declineEffect = new VillageAskStandingEffect
                    {
                        leadershipDelta = -8f,
                        pietyDelta = 0f,
                        wealthDelta = 0f,
                        helpfulnessDelta = -4f
                    }
                });

                _groupAskDefinitions.Add(new VillageGroupAskDefinition
                {
                    askId = "ask_religious_figure",
                    title = "Serve as religious figure",
                    summary = "The faithful ask you to lead rites and settle spiritual disputes.",
                    thresholds = new VillageStandingThreshold
                    {
                        minLeadership = -100f,
                        minPiety = 40f,
                        minWealth = -100f,
                        minHelpfulness = 15f
                    },
                    onOfferMilestoneSignal = "hint:m_village_religious_arc",
                    onAcceptMilestoneSignal = "unlock:m_village_religious_arc",
                    onDeclineMilestoneSignal = "hint:m_village_religious_declined",
                    acceptEffect = new VillageAskStandingEffect
                    {
                        leadershipDelta = 0f,
                        pietyDelta = 8f,
                        wealthDelta = 0f,
                        helpfulnessDelta = 3f
                    },
                    declineEffect = new VillageAskStandingEffect
                    {
                        leadershipDelta = 0f,
                        pietyDelta = -8f,
                        wealthDelta = 0f,
                        helpfulnessDelta = -2f
                    }
                });

                _groupAskDefinitions.Add(new VillageGroupAskDefinition
                {
                    askId = "ask_market_patron",
                    title = "Sponsor market recovery",
                    summary = "Merchants ask for steady patronage to restore trade confidence.",
                    thresholds = new VillageStandingThreshold
                    {
                        minLeadership = -100f,
                        minPiety = -100f,
                        minWealth = 35f,
                        minHelpfulness = 12f
                    },
                    onOfferMilestoneSignal = "hint:m_village_market_arc",
                    onAcceptMilestoneSignal = "unlock:m_village_market_arc",
                    onDeclineMilestoneSignal = "hint:m_village_market_declined",
                    acceptEffect = new VillageAskStandingEffect
                    {
                        leadershipDelta = 2f,
                        pietyDelta = 0f,
                        wealthDelta = 6f,
                        helpfulnessDelta = 2f
                    },
                    declineEffect = new VillageAskStandingEffect
                    {
                        leadershipDelta = 0f,
                        pietyDelta = 0f,
                        wealthDelta = -6f,
                        helpfulnessDelta = -2f
                    }
                });
            }
        }

        void EvaluateAndPersistGroupAsks()
        {
            if (_groupAskDefinitions.Count == 0)
                return;

            var aggregate = ComputeAggregateStanding();
            var changed = false;
            for (var i = 0; i < _groupAskDefinitions.Count; i++)
            {
                var def = _groupAskDefinitions[i];
                if (def == null || string.IsNullOrWhiteSpace(def.askId))
                    continue;
                if (!_groupAskRecordsById.TryGetValue(def.askId, out var existing) || existing == null)
                {
                    if (!IsThresholdMet(def.thresholds, aggregate))
                        continue;

                    var now = DateTime.UtcNow.ToString("o");
                    var rec = new VillageGroupAskRecord
                    {
                        askId = def.askId.Trim(),
                        state = ToWire(VillageGroupAskState.Offered),
                        title = Safe(def.title),
                        summary = Safe(def.summary),
                        offeredUtc = now,
                        respondedUtc = string.Empty,
                        lastResponderNpcId = string.Empty
                    };
                    _groupAskRecordsById[rec.askId] = rec;
                    changed = true;

                    if (!string.IsNullOrWhiteSpace(def.onOfferMilestoneSignal))
                        _pendingMilestoneSignals.Enqueue(def.onOfferMilestoneSignal.Trim());
                }
            }

            if (changed)
                SaveGroupAskState();
        }

        void AppendGroupAskContext(List<string> lines)
        {
            if (lines == null)
                return;

            var offered = new List<string>();
            var accepted = new List<string>();
            var declined = new List<string>();
            for (var i = 0; i < _groupAskDefinitions.Count; i++)
            {
                var def = _groupAskDefinitions[i];
                if (def == null || string.IsNullOrWhiteSpace(def.askId))
                    continue;
                if (!_groupAskRecordsById.TryGetValue(def.askId, out var rec) || rec == null)
                    continue;
                if (!TryParseAskState(rec.state, out var state))
                    continue;

                var line = $"{rec.askId}: {Safe(rec.title)} - {Safe(rec.summary)}";
                if (state == VillageGroupAskState.Offered)
                    offered.Add(line);
                else if (state == VillageGroupAskState.Accepted)
                    accepted.Add(line);
                else if (state == VillageGroupAskState.Declined)
                    declined.Add(line);
            }

            if (offered.Count > 0)
                lines.Add("Group asks awaiting response: " + string.Join(" | ", offered));
            if (accepted.Count > 0)
                lines.Add("Accepted leadership arcs: " + string.Join(" | ", accepted));
            if (declined.Count > 0)
                lines.Add("Declined leadership arcs: " + string.Join(" | ", declined));
            if (_pendingMilestoneSignals.Count > 0)
                lines.Add("Milestone progression hooks pending: " + string.Join(" | ", _pendingMilestoneSignals.ToArray()));
        }

        bool IsThresholdMet(VillageStandingThreshold threshold, AggregateStanding aggregate)
        {
            if (threshold == null)
                return false;
            return aggregate.Leadership >= threshold.minLeadership
                   && aggregate.Piety >= threshold.minPiety
                   && aggregate.Wealth >= threshold.minWealth
                   && aggregate.Helpfulness >= threshold.minHelpfulness;
        }

        void ApplyAggregateStandingShift(float leadershipDelta, float pietyDelta, float wealthDelta, float helpfulnessDelta)
        {
            if (_recordsByNpcId.Count == 0)
                return;

            foreach (var kvp in _recordsByNpcId)
            {
                var rec = kvp.Value;
                rec.Leadership = ClampOpinion(rec.Leadership + leadershipDelta);
                rec.Piety = ClampOpinion(rec.Piety + pietyDelta);
                rec.Wealth = ClampOpinion(rec.Wealth + wealthDelta);
                rec.Helpfulness = ClampOpinion(rec.Helpfulness + helpfulnessDelta);
            }
        }

        void LoadGroupAskState()
        {
            try
            {
                if (!File.Exists(_askStatePath))
                    return;
                var parsed = JsonConvert.DeserializeObject<VillageGroupAskDocument>(File.ReadAllText(_askStatePath));
                if (parsed == null || parsed.groupAsks == null)
                    return;
                _groupAskRecordsById.Clear();
                for (var i = 0; i < parsed.groupAsks.Count; i++)
                {
                    var rec = parsed.groupAsks[i];
                    if (rec == null || string.IsNullOrWhiteSpace(rec.askId))
                        continue;
                    _groupAskRecordsById[rec.askId.Trim()] = CloneRecord(rec);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VillageOpinionService] Failed to load ask state: {ex.Message}");
            }
        }

        void SaveGroupAskState()
        {
            try
            {
                var doc = new VillageGroupAskDocument();
                for (var i = 0; i < _groupAskDefinitions.Count; i++)
                {
                    var askId = _groupAskDefinitions[i] != null ? _groupAskDefinitions[i].askId : null;
                    if (string.IsNullOrWhiteSpace(askId))
                        continue;
                    if (_groupAskRecordsById.TryGetValue(askId, out var rec) && rec != null)
                        doc.groupAsks.Add(CloneRecord(rec));
                }

                var dir = Path.GetDirectoryName(_askStatePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(_askStatePath, JsonConvert.SerializeObject(doc, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VillageOpinionService] Failed to save ask state: {ex.Message}");
            }
        }

        bool TryGetAskDefinition(string askId, out VillageGroupAskDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(askId))
                return false;
            for (var i = 0; i < _groupAskDefinitions.Count; i++)
            {
                var d = _groupAskDefinitions[i];
                if (d == null || string.IsNullOrWhiteSpace(d.askId))
                    continue;
                if (string.Equals(d.askId, askId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    definition = d;
                    return true;
                }
            }

            return false;
        }

        static VillageGroupAskRecord CloneRecord(VillageGroupAskRecord source)
        {
            if (source == null)
                return null;
            return new VillageGroupAskRecord
            {
                askId = Safe(source.askId),
                state = Safe(source.state),
                title = Safe(source.title),
                summary = Safe(source.summary),
                offeredUtc = Safe(source.offeredUtc),
                respondedUtc = Safe(source.respondedUtc),
                lastResponderNpcId = Safe(source.lastResponderNpcId)
            };
        }

        static string ToWire(VillageGroupAskState state)
        {
            if (state == VillageGroupAskState.Accepted)
                return "accepted";
            if (state == VillageGroupAskState.Declined)
                return "declined";
            return "offered";
        }

        static bool TryParseAskState(string raw, out VillageGroupAskState state)
        {
            state = VillageGroupAskState.Offered;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var key = raw.Trim().ToLowerInvariant();
            if (key == "offered")
            {
                state = VillageGroupAskState.Offered;
                return true;
            }

            if (key == "accepted")
            {
                state = VillageGroupAskState.Accepted;
                return true;
            }

            if (key == "declined")
            {
                state = VillageGroupAskState.Declined;
                return true;
            }

            return false;
        }

        static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
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

    public enum VillageGroupAskState
    {
        Offered,
        Accepted,
        Declined
    }

    [Serializable]
    public sealed class VillageGroupAskDefinition
    {
        public string askId;
        public string title;
        public string summary;
        public VillageStandingThreshold thresholds = new VillageStandingThreshold();
        public string onOfferMilestoneSignal;
        public string onAcceptMilestoneSignal;
        public string onDeclineMilestoneSignal;
        public VillageAskStandingEffect acceptEffect = new VillageAskStandingEffect();
        public VillageAskStandingEffect declineEffect = new VillageAskStandingEffect();

        public VillageGroupAskDefinition Clone()
        {
            return new VillageGroupAskDefinition
            {
                askId = askId,
                title = title,
                summary = summary,
                thresholds = thresholds != null ? thresholds.Clone() : new VillageStandingThreshold(),
                onOfferMilestoneSignal = onOfferMilestoneSignal,
                onAcceptMilestoneSignal = onAcceptMilestoneSignal,
                onDeclineMilestoneSignal = onDeclineMilestoneSignal,
                acceptEffect = acceptEffect != null ? acceptEffect.Clone() : new VillageAskStandingEffect(),
                declineEffect = declineEffect != null ? declineEffect.Clone() : new VillageAskStandingEffect()
            };
        }
    }

    [Serializable]
    public sealed class VillageStandingThreshold
    {
        public float minLeadership = VillageOpinionService.MinOpinion;
        public float minPiety = VillageOpinionService.MinOpinion;
        public float minWealth = VillageOpinionService.MinOpinion;
        public float minHelpfulness = VillageOpinionService.MinOpinion;

        public VillageStandingThreshold Clone()
        {
            return new VillageStandingThreshold
            {
                minLeadership = minLeadership,
                minPiety = minPiety,
                minWealth = minWealth,
                minHelpfulness = minHelpfulness
            };
        }
    }

    [Serializable]
    public sealed class VillageAskStandingEffect
    {
        public float leadershipDelta;
        public float pietyDelta;
        public float wealthDelta;
        public float helpfulnessDelta;

        public VillageAskStandingEffect Clone()
        {
            return new VillageAskStandingEffect
            {
                leadershipDelta = leadershipDelta,
                pietyDelta = pietyDelta,
                wealthDelta = wealthDelta,
                helpfulnessDelta = helpfulnessDelta
            };
        }
    }

    [Serializable]
    public sealed class VillageGroupAskRecord
    {
        public string askId;
        public string state;
        public string title;
        public string summary;
        public string offeredUtc;
        public string respondedUtc;
        public string lastResponderNpcId;
    }

    [Serializable]
    sealed class VillageGroupAskDocument
    {
        public int schemaVersion = 1;
        public List<VillageGroupAskRecord> groupAsks = new List<VillageGroupAskRecord>();
    }
}
