using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Npc
{
    [Serializable]
    sealed class VillageSystemicEventsDoc
    {
        public int schemaVersion = 1;
        public float chatProximityRollChance = 0.12f;
        public float pairCooldownSeconds = 45f;
        public List<VillageSystemicEventDefinition> events = new List<VillageSystemicEventDefinition>();
    }

    [Serializable]
    sealed class VillageSystemicEventDefinition
    {
        public string id;
        public string trigger;
        public float weight = 1f;
        public string rumorTemplate;
        public float requiresActorOpinionBelow;
        public float requiresPairAffinityAbove;
        public float opinionDeltaTowardHero;
    }

    /// <summary>
    /// Deterministic village events for NPC chat proximity (Option A — no interaction FSM dialogue).
    /// </summary>
    public sealed class VillageSystemicEventResolver
    {
        readonly VillageSystemicEventsDoc _doc;
        readonly Dictionary<string, int> _pairAffinity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, float> _nextRollByPair = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public VillageSystemicEventResolver(string streamingAssetsRoot = null)
        {
            _doc = LoadDoc(streamingAssetsRoot);
        }

        public float ChatProximityRollChance => _doc != null ? Mathf.Clamp01(_doc.chatProximityRollChance) : 0.12f;

        public VillageConsequenceEvent TryResolveChatProximityEvent(
            string actorNpcId,
            string targetNpcId,
            string actorDisplayName,
            string targetDisplayName,
            VillageOpinionService opinionService,
            float nowTime,
            System.Random random = null)
        {
            if (string.IsNullOrWhiteSpace(actorNpcId) || string.IsNullOrWhiteSpace(targetNpcId))
                return null;
            if (string.Equals(actorNpcId, targetNpcId, StringComparison.OrdinalIgnoreCase))
                return null;

            var pairKey = BuildPairKey(actorNpcId, targetNpcId);

            var cooldown = _doc != null ? Mathf.Max(5f, _doc.pairCooldownSeconds) : 45f;
            if (_nextRollByPair.TryGetValue(pairKey, out var nextAllowed) && nowTime < nextAllowed)
                return null;

            IncrementPairAffinity(pairKey);

            var rng = random ?? new System.Random(unchecked(pairKey.GetHashCode() ^ (int)nowTime));
            if (rng.NextDouble() > ChatProximityRollChance)
            {
                _nextRollByPair[pairKey] = nowTime + cooldown;
                return null;
            }

            var affinity = GetPairAffinity(pairKey);
            var actorOpinion = opinionService != null ? opinionService.GetOpinionTowardHero(actorNpcId) : 0f;

            VillageSystemicEventDefinition chosen = null;
            var candidates = CollectCandidates(actorOpinion, affinity);
            if (candidates.Count == 0)
                return null;

            var totalWeight = 0f;
            for (var i = 0; i < candidates.Count; i++)
                totalWeight += Mathf.Max(0.01f, candidates[i].weight);

            var roll = (float)rng.NextDouble() * totalWeight;
            for (var i = 0; i < candidates.Count; i++)
            {
                roll -= Mathf.Max(0.01f, candidates[i].weight);
                if (roll <= 0f)
                {
                    chosen = candidates[i];
                    break;
                }
            }

            if (chosen == null)
                chosen = candidates[candidates.Count - 1];

            _nextRollByPair[pairKey] = nowTime + cooldown;
            return BuildEvent(chosen, actorNpcId, targetNpcId, actorDisplayName, targetDisplayName);
        }

        List<VillageSystemicEventDefinition> CollectCandidates(float actorOpinion, int pairAffinity)
        {
            var list = new List<VillageSystemicEventDefinition>();
            var events = _doc?.events;
            if (events == null)
                return list;

            for (var i = 0; i < events.Count; i++)
            {
                var def = events[i];
                if (def == null || string.IsNullOrWhiteSpace(def.id))
                    continue;
                var trigger = string.IsNullOrWhiteSpace(def.trigger) ? string.Empty : def.trigger.Trim().ToLowerInvariant();
                if (trigger == "chat_proximity")
                {
                    list.Add(def);
                    continue;
                }

                if (trigger == "low_opinion_theft"
                    && def.requiresActorOpinionBelow != 0f
                    && actorOpinion <= def.requiresActorOpinionBelow)
                {
                    list.Add(def);
                    continue;
                }

                if (trigger == "high_pair_affinity"
                    && def.requiresPairAffinityAbove > 0f
                    && pairAffinity >= def.requiresPairAffinityAbove)
                {
                    list.Add(def);
                }
            }

            return list;
        }

        static VillageConsequenceEvent BuildEvent(
            VillageSystemicEventDefinition def,
            string actorNpcId,
            string targetNpcId,
            string actorDisplayName,
            string targetDisplayName)
        {
            var actor = string.IsNullOrWhiteSpace(actorDisplayName) ? actorNpcId : actorDisplayName.Trim();
            var target = string.IsNullOrWhiteSpace(targetDisplayName) ? targetNpcId : targetDisplayName.Trim();
            var template = string.IsNullOrWhiteSpace(def.rumorTemplate)
                ? "{actor} and {target} were noticed together."
                : def.rumorTemplate.Trim();
            var rumor = template
                .Replace("{actor}", actor)
                .Replace("{target}", target);

            return new VillageConsequenceEvent
            {
                eventId = def.id,
                eventType = string.IsNullOrWhiteSpace(def.trigger) ? "chat_proximity" : def.trigger.Trim(),
                timestampUtc = DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds,
                actorNpcId = actorNpcId,
                targetNpcId = targetNpcId,
                actorDisplayName = actor,
                targetDisplayName = target,
                rumorText = rumor,
                opinionDeltaTowardHero = def.opinionDeltaTowardHero
            };
        }

        void IncrementPairAffinity(string pairKey)
        {
            _pairAffinity.TryGetValue(pairKey, out var value);
            _pairAffinity[pairKey] = value + 1;
        }

        int GetPairAffinity(string pairKey)
        {
            return _pairAffinity.TryGetValue(pairKey, out var value) ? value : 0;
        }

        static string BuildPairKey(string a, string b)
        {
            var left = string.IsNullOrWhiteSpace(a) ? string.Empty : a.Trim().ToLowerInvariant();
            var right = string.IsNullOrWhiteSpace(b) ? string.Empty : b.Trim().ToLowerInvariant();
            return string.CompareOrdinal(left, right) <= 0 ? left + "|" + right : right + "|" + left;
        }

        static VillageSystemicEventsDoc LoadDoc(string streamingAssetsRoot)
        {
            var root = string.IsNullOrWhiteSpace(streamingAssetsRoot)
                ? Application.streamingAssetsPath
                : streamingAssetsRoot;
            var path = Path.Combine(root, "Dialogue", "village_systemic_events.json");
            try
            {
                if (!File.Exists(path))
                    return new VillageSystemicEventsDoc();
                return JsonConvert.DeserializeObject<VillageSystemicEventsDoc>(File.ReadAllText(path))
                    ?? new VillageSystemicEventsDoc();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VillageSystemicEventResolver] Load failed: {ex.Message}");
                return new VillageSystemicEventsDoc();
            }
        }
    }
}
