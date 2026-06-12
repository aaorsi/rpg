using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Builds deterministic ambient villager chatter barks from lightweight persona/opinion context.
    /// </summary>
    public sealed class AmbientNpcChatterService
    {
        const float MinGlobalCooldownSeconds = 1f;
        const float MinPairCooldownSeconds = 2f;
        static readonly string[] SupportiveTemplates =
        {
            "\"The hero keeps the square steady these days.\"",
            "\"If the hero keeps helping, this season might turn around.\"",
            "\"I trust the hero's hand when trouble rises.\""
        };

        static readonly string[] NeutralTemplates =
        {
            "\"We'll see what the hero does next.\"",
            "\"Hard to read the hero yet; we keep watching.\"",
            "\"The hero's path is still uncertain to me.\""
        };

        static readonly string[] SkepticalTemplates =
        {
            "\"The hero says much, but I am not convinced.\"",
            "\"I keep one eye on the hero and one on my door.\"",
            "\"If the hero slips once, the village pays for it.\""
        };

        static readonly string[] RichSupportiveTemplates =
        {
            "\"Since the hero arrived, market tempers cooled and carts roll safer.\"",
            "\"Even the shrine bells sound calmer when the hero walks these roads.\""
        };

        static readonly string[] RichNeutralTemplates =
        {
            "\"The hero shifts old habits, but we need a full season to judge.\"",
            "\"Some praise the hero, some doubt; I keep my tally in silence.\""
        };

        static readonly string[] RichSkepticalTemplates =
        {
            "\"Promises are cheap at dusk; I trust the hero only after winter.\"",
            "\"One wrong bargain from the hero and old quarrels will wake again.\""
        };

        readonly Dictionary<string, float> _nextAllowedByPair = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<string, float> _nextAllowedBySpeaker = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _activeParticipants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _removeScratch = new List<string>();

        int _eventCounter;

        public void SetParticipants(IReadOnlyList<string> npcIds)
        {
            _activeParticipants.Clear();
            _removeScratch.Clear();
            foreach (var kvp in _nextAllowedBySpeaker)
                _removeScratch.Add(kvp.Key);

            if (npcIds != null)
            {
                for (var i = 0; i < npcIds.Count; i++)
                {
                    var id = npcIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var normalized = id.Trim();
                    _activeParticipants.Add(normalized);
                    _removeScratch.Remove(normalized);
                }
            }

            for (var i = 0; i < _removeScratch.Count; i++)
                _nextAllowedBySpeaker.Remove(_removeScratch[i]);

            _removeScratch.Clear();
            foreach (var kvp in _nextAllowedByPair)
            {
                if (!TrySplitPairKey(kvp.Key, out var left, out var right))
                {
                    _removeScratch.Add(kvp.Key);
                    continue;
                }
                if (!_activeParticipants.Contains(left) || !_activeParticipants.Contains(right))
                    _removeScratch.Add(kvp.Key);
            }

            for (var i = 0; i < _removeScratch.Count; i++)
                _nextAllowedByPair.Remove(_removeScratch[i]);
            _removeScratch.Clear();
        }

        public bool TryBuildEvent(
            float nowSeconds,
            string speakerNpcId,
            string targetNpcId,
            string speakerDisplayName,
            string targetDisplayName,
            string speakerPersonality,
            string targetPersonality,
            IReadOnlyList<string> speakerGoals,
            float speakerOpinionTowardHero,
            float globalCooldownSeconds,
            float pairCooldownSeconds,
            int richVariantPercent,
            out AmbientNpcChatterEvent chatter)
        {
            chatter = default;
            if (!TryNormalizeNpcId(speakerNpcId, out var speakerId))
                return false;
            if (!TryNormalizeNpcId(targetNpcId, out var targetId))
                return false;
            if (string.Equals(speakerId, targetId, StringComparison.OrdinalIgnoreCase))
                return false;

            var pairKey = BuildPairKey(speakerId, targetId);
            if (_nextAllowedByPair.TryGetValue(pairKey, out var nextPairTime) && nowSeconds < nextPairTime)
                return false;
            if (_nextAllowedBySpeaker.TryGetValue(speakerId, out var nextSpeakerTime) && nowSeconds < nextSpeakerTime)
                return false;

            var clampedGlobalCooldown = Mathf.Max(MinGlobalCooldownSeconds, globalCooldownSeconds);
            var clampedPairCooldown = Mathf.Max(MinPairCooldownSeconds, pairCooldownSeconds);
            _nextAllowedByPair[pairKey] = nowSeconds + clampedPairCooldown;
            _nextAllowedBySpeaker[speakerId] = nowSeconds + clampedGlobalCooldown;

            var mood = ResolveMood(speakerOpinionTowardHero);
            var topicTag = ResolveTopicTag(speakerPersonality, targetPersonality, speakerGoals);
            var richPct = Mathf.Clamp(richVariantPercent, 0, 100);

            var hash = 17;
            hash = HashToken(hash, speakerId);
            hash = HashToken(hash, targetId);
            hash = HashToken(hash, mood);
            hash = HashToken(hash, topicTag);
            hash = hash * 31 + _eventCounter;
            _eventCounter++;

            var rich = richPct > 0 && PositiveMod(hash, 100) < richPct;
            var line = PickTemplate(mood, rich, hash);
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var speaker = string.IsNullOrWhiteSpace(speakerDisplayName) ? speakerId : speakerDisplayName.Trim();
            var target = string.IsNullOrWhiteSpace(targetDisplayName) ? targetId : targetDisplayName.Trim();
            chatter = new AmbientNpcChatterEvent
            {
                SpeakerNpcId = speakerId,
                TargetNpcId = targetId,
                Text = $"{speaker} to {target}: {line}"
            };
            return true;
        }

        static string ResolveMood(float opinionTowardHero)
        {
            if (opinionTowardHero >= 20f)
                return "supportive";
            if (opinionTowardHero <= -20f)
                return "skeptical";
            return "neutral";
        }

        static string ResolveTopicTag(string speakerPersonality, string targetPersonality, IReadOnlyList<string> speakerGoals)
        {
            var personality = (speakerPersonality ?? string.Empty).Trim().ToLowerInvariant();
            if (personality.Contains("guard") || personality.Contains("stern") || personality.Contains("strict"))
                return "order";
            if (personality.Contains("warm") || personality.Contains("kind") || personality.Contains("helpful"))
                return "care";
            if (personality.Contains("cunning") || personality.Contains("wary") || personality.Contains("suspicious"))
                return "risk";

            var target = (targetPersonality ?? string.Empty).Trim().ToLowerInvariant();
            if (target.Contains("warm") || target.Contains("kind"))
                return "trust";

            if (speakerGoals != null)
            {
                for (var i = 0; i < speakerGoals.Count; i++)
                {
                    var goal = speakerGoals[i];
                    if (string.IsNullOrWhiteSpace(goal))
                        continue;
                    var g = goal.ToLowerInvariant();
                    if (g.Contains("guard") || g.Contains("protect"))
                        return "guard";
                    if (g.Contains("trade") || g.Contains("market"))
                        return "trade";
                    if (g.Contains("farm") || g.Contains("harvest"))
                        return "harvest";
                }
            }

            return "general";
        }

        static string PickTemplate(string mood, bool rich, int hash)
        {
            string[] pool;
            if (string.Equals(mood, "supportive", StringComparison.Ordinal))
                pool = rich ? RichSupportiveTemplates : SupportiveTemplates;
            else if (string.Equals(mood, "skeptical", StringComparison.Ordinal))
                pool = rich ? RichSkepticalTemplates : SkepticalTemplates;
            else
                pool = rich ? RichNeutralTemplates : NeutralTemplates;

            if (pool == null || pool.Length == 0)
                return string.Empty;
            return pool[PositiveMod(hash, pool.Length)];
        }

        static int HashToken(int seed, string token)
        {
            unchecked
            {
                var hash = seed;
                if (string.IsNullOrWhiteSpace(token))
                    return hash * 31 + 7;
                var value = token.Trim();
                for (var i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];
                return hash;
            }
        }

        static int PositiveMod(int value, int modulo)
        {
            if (modulo <= 0)
                return 0;
            var m = value % modulo;
            return m < 0 ? m + modulo : m;
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

        static bool TrySplitPairKey(string pairKey, out string left, out string right)
        {
            left = string.Empty;
            right = string.Empty;
            if (string.IsNullOrWhiteSpace(pairKey))
                return false;
            var sep = pairKey.IndexOf('|');
            if (sep <= 0 || sep >= pairKey.Length - 1)
                return false;
            left = pairKey.Substring(0, sep);
            right = pairKey.Substring(sep + 1);
            return left.Length > 0 && right.Length > 0;
        }
    }

    public struct AmbientNpcChatterEvent
    {
        public string SpeakerNpcId;
        public string TargetNpcId;
        public string Text;
    }
}
