using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class DialogueVoiceAssignmentsDoc
    {
        public int schemaVersion = 1;
        public string heroVoiceId = string.Empty;
        public Dictionary<string, string> npcVoiceById = new Dictionary<string, string>();
        public string updatedUtc = string.Empty;
    }

    /// <summary>
    /// Persists hero and NPC Pocket TTS voice assignments so each actor keeps a stable voice.
    /// </summary>
    public sealed class NpcVoiceAssignmentRepository
    {
        const int SchemaVersion = 1;
        const string VoiceAssignmentKey = "voice_assignments";
        readonly JsonFileStore _store;
        readonly System.Random _rng = new System.Random();

        public NpcVoiceAssignmentRepository(string rootDirectory = null)
        {
            var rootDir = string.IsNullOrWhiteSpace(rootDirectory)
                ? System.IO.Path.Combine(Application.persistentDataPath, "RpgDialogueVoices")
                : rootDirectory;
            _store = new JsonFileStore(rootDir, "NpcVoiceAssignmentRepository");
        }

        public string GetOrAssignHeroVoice(string preferredVoice, IReadOnlyList<string> voicePool)
        {
            var pool = BuildNormalizedPool(voicePool, preferredVoice);
            string assigned = null;
            _store.Update<DialogueVoiceAssignmentsDoc>(
                VoiceAssignmentKey,
                BuildFallback,
                doc =>
                {
                    Normalize(doc);
                    if (!string.IsNullOrWhiteSpace(doc.heroVoiceId) && pool.Contains(doc.heroVoiceId))
                    {
                        assigned = doc.heroVoiceId;
                        return doc;
                    }

                    var requested = NormalizeVoice(preferredVoice);
                    doc.heroVoiceId = pool.Contains(requested) ? requested : PickRandom(pool);
                    assigned = doc.heroVoiceId;
                    doc.updatedUtc = DateTime.UtcNow.ToString("o");
                    return doc;
                },
                Normalize,
                Normalize);

            return string.IsNullOrWhiteSpace(assigned) ? NormalizeVoice(preferredVoice) : assigned;
        }

        public string GetOrAssignNpcVoice(string npcId, string heroVoice, IReadOnlyList<string> voicePool)
        {
            var normalizedNpcId = string.IsNullOrWhiteSpace(npcId) ? "npc_unknown" : npcId.Trim();
            var pool = BuildNormalizedPool(voicePool, heroVoice);
            string assigned = null;

            _store.Update<DialogueVoiceAssignmentsDoc>(
                VoiceAssignmentKey,
                BuildFallback,
                doc =>
                {
                    Normalize(doc);
                    if (doc.npcVoiceById.TryGetValue(normalizedNpcId, out var existing) && pool.Contains(existing))
                    {
                        assigned = existing;
                        return doc;
                    }

                    var usedVoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in doc.npcVoiceById)
                    {
                        if (kv.Value != null)
                            usedVoices.Add(kv.Value.Trim());
                    }

                    var candidates = new List<string>();
                    for (var i = 0; i < pool.Count; i++)
                    {
                        var voice = pool[i];
                        if (!string.Equals(voice, heroVoice, StringComparison.OrdinalIgnoreCase) && !usedVoices.Contains(voice))
                            candidates.Add(voice);
                    }
                    if (candidates.Count == 0)
                    {
                        for (var i = 0; i < pool.Count; i++)
                        {
                            var voice = pool[i];
                            if (!string.Equals(voice, heroVoice, StringComparison.OrdinalIgnoreCase))
                                candidates.Add(voice);
                        }
                    }
                    if (candidates.Count == 0)
                        candidates.Add(pool[0]);

                    var picked = PickRandom(candidates);
                    doc.npcVoiceById[normalizedNpcId] = picked;
                    assigned = picked;
                    doc.updatedUtc = DateTime.UtcNow.ToString("o");
                    return doc;
                },
                Normalize,
                Normalize);

            return string.IsNullOrWhiteSpace(assigned) ? NormalizeVoice(heroVoice) : assigned;
        }

        DialogueVoiceAssignmentsDoc BuildFallback()
        {
            return new DialogueVoiceAssignmentsDoc
            {
                schemaVersion = SchemaVersion,
                heroVoiceId = string.Empty,
                npcVoiceById = new Dictionary<string, string>(),
                updatedUtc = DateTime.UtcNow.ToString("o")
            };
        }

        static void Normalize(DialogueVoiceAssignmentsDoc doc)
        {
            if (doc == null)
                return;
            doc.schemaVersion = SchemaVersion;
            doc.heroVoiceId = NormalizeVoice(doc.heroVoiceId);
            doc.npcVoiceById ??= new Dictionary<string, string>();
            var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in doc.npcVoiceById)
            {
                var key = string.IsNullOrWhiteSpace(kv.Key) ? string.Empty : kv.Key.Trim();
                var value = NormalizeVoice(kv.Value);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;
                sanitized[key] = value;
            }
            doc.npcVoiceById = sanitized;
            if (string.IsNullOrWhiteSpace(doc.updatedUtc))
                doc.updatedUtc = DateTime.UtcNow.ToString("o");
        }

        List<string> BuildNormalizedPool(IReadOnlyList<string> voicePool, string fallback)
        {
            var pool = new List<string>();
            if (voicePool != null)
            {
                for (var i = 0; i < voicePool.Count; i++)
                {
                    var voice = NormalizeVoice(voicePool[i]);
                    if (!string.IsNullOrWhiteSpace(voice) && !pool.Contains(voice))
                        pool.Add(voice);
                }
            }
            var normalizedFallback = NormalizeVoice(fallback);
            if (!string.IsNullOrWhiteSpace(normalizedFallback) && !pool.Contains(normalizedFallback))
                pool.Add(normalizedFallback);
            if (pool.Count == 0)
                pool.Add("alba");
            return pool;
        }

        string PickRandom(IReadOnlyList<string> options)
        {
            if (options == null || options.Count == 0)
                return "alba";
            lock (_rng)
            {
                var idx = _rng.Next(0, options.Count);
                return options[idx];
            }
        }

        static string NormalizeVoice(string voiceId)
        {
            return string.IsNullOrWhiteSpace(voiceId) ? string.Empty : voiceId.Trim();
        }
    }
}
