using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Persists per-NPC memory as JSON under <see cref="Application.persistentDataPath"/> (writable on device builds).
    /// Each file groups memories by <c>characterId</c> (e.g. <c>player</c> for the human, or future party members).
    /// </summary>
    public sealed class NpcMemoryRepository
    {
        const int SchemaVersion = 1;
        const int MaxMemoriesPerSubject = 100;
        readonly object _fileLock = new object();

        readonly string _rootDir;

        /// <summary>
        /// Deletes all saved NPC memory JSON for this install path. Call when a new play session starts so memories
        /// do not carry across exiting Play Mode / quitting the build (design: memory is session-local only).
        /// </summary>
        public static void ClearAllForNewPlaySession(string rootDirectory = null)
        {
            var dir = string.IsNullOrEmpty(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "RpgNpcMemory")
                : rootDirectory;
            try
            {
                if (!Directory.Exists(dir))
                    return;
                foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[NpcMemoryRepository] Could not delete '{path}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcMemoryRepository] Could not clear memory directory '{dir}': {ex.Message}");
            }
        }

        public NpcMemoryRepository(string rootDirectory = null)
        {
            _rootDir = string.IsNullOrEmpty(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "RpgNpcMemory")
                : rootDirectory;
        }

        /// <summary>Human-readable block injected into the system prompt.</summary>
        public string BuildPromptBlock(string npcId, string primarySubjectId = "player")
        {
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";

            lock (_fileLock)
            {
                var doc = LoadDocumentUnlocked(npcId);
                if (doc.Subjects == null || doc.Subjects.Count == 0)
                    return "(No remembered facts yet.)";

                var sb = new StringBuilder();
                foreach (var subj in doc.Subjects)
                {
                    if (subj == null || string.IsNullOrWhiteSpace(subj.CharacterId))
                        continue;
                    var id = subj.CharacterId.Trim();
                    var label = string.IsNullOrWhiteSpace(subj.DisplayLabel) ? id : subj.DisplayLabel.Trim();
                    var memories = subj.Memories;
                    if (memories == null || memories.Count == 0)
                        continue;

                    sb.AppendLine($"— Regarding character \"{label}\" (id: {id}):");
                    foreach (var m in memories.OrderByDescending(x => x.AddedUtc))
                    {
                        if (m == null || string.IsNullOrWhiteSpace(m.Summary))
                            continue;
                        var k = string.IsNullOrWhiteSpace(m.Kind) ? "note" : m.Kind.Trim();
                        sb.AppendLine($"  • [{k}] {m.Summary.Trim()}");
                    }

                    sb.AppendLine();
                }

                if (sb.Length == 0)
                    return "(No remembered facts yet.)";

                return sb.ToString().TrimEnd();
            }
        }

        /// <summary>Append new memories after a validated model turn. Skips near-duplicates for the same subject.</summary>
        public void TryAppendCandidates(string npcId, IReadOnlyList<NpcMemoryCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return;
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";

            lock (_fileLock)
            {
                var doc = LoadDocumentUnlocked(npcId);
                doc.NpcId = npcId;
                doc.SchemaVersion = SchemaVersion;

                foreach (var c in candidates)
                {
                    if (string.IsNullOrWhiteSpace(c.Summary))
                        continue;
                    var subject = string.IsNullOrWhiteSpace(c.SubjectCharacterId) ? "player" : c.SubjectCharacterId.Trim();
                    var kind = string.IsNullOrWhiteSpace(c.Kind) ? "fact" : c.Kind.Trim();
                    var summary = c.Summary.Trim();
                    if (summary.Length > 400)
                        summary = summary.Substring(0, 397) + "…";

                    var slot = GetOrCreateSubject(doc, subject);
                    var norm = summary.ToLowerInvariant();
                    if (slot.Memories.Any(m =>
                            m != null &&
                            !string.IsNullOrEmpty(m.Summary) &&
                            m.Summary.Trim().ToLowerInvariant() == norm))
                        continue;

                    slot.Memories.Add(new NpcMemoryPersistedEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        AddedUtc = DateTime.UtcNow.ToString("o"),
                        Kind = kind,
                        Summary = summary
                    });

                    while (slot.Memories.Count > MaxMemoriesPerSubject)
                        slot.Memories.RemoveAt(0);
                }

                SaveDocumentUnlocked(npcId, doc);
            }
        }

        NpcMemorySubjectSlot GetOrCreateSubject(NpcMemoryDocument doc, string characterId)
        {
            doc.Subjects ??= new List<NpcMemorySubjectSlot>();
            foreach (var s in doc.Subjects)
            {
                if (s != null && string.Equals(s.CharacterId, characterId, StringComparison.OrdinalIgnoreCase))
                    return s;
            }

            var created = new NpcMemorySubjectSlot
            {
                CharacterId = characterId,
                DisplayLabel = characterId.Equals("player", StringComparison.OrdinalIgnoreCase) ? "Traveler" : characterId,
                Memories = new List<NpcMemoryPersistedEntry>()
            };
            doc.Subjects.Add(created);
            return created;
        }

        NpcMemoryDocument LoadDocumentUnlocked(string npcId)
        {
            var path = FilePathFor(npcId);
            if (!File.Exists(path))
                return new NpcMemoryDocument { SchemaVersion = SchemaVersion, NpcId = npcId, Subjects = new List<NpcMemorySubjectSlot>() };

            try
            {
                var json = File.ReadAllText(path);
                var doc = JsonConvert.DeserializeObject<NpcMemoryDocument>(json);
                if (doc == null)
                    return new NpcMemoryDocument { SchemaVersion = SchemaVersion, NpcId = npcId, Subjects = new List<NpcMemorySubjectSlot>() };
                doc.Subjects ??= new List<NpcMemorySubjectSlot>();
                doc.NpcId = npcId;
                return doc;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcMemoryRepository] Failed to load {path}: {ex.Message}");
                return new NpcMemoryDocument { SchemaVersion = SchemaVersion, NpcId = npcId, Subjects = new List<NpcMemorySubjectSlot>() };
            }
        }

        void SaveDocumentUnlocked(string npcId, NpcMemoryDocument doc)
        {
            try
            {
                Directory.CreateDirectory(_rootDir);
                var path = FilePathFor(npcId);
                var json = JsonConvert.SerializeObject(doc, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcMemoryRepository] Failed to save memory for '{npcId}': {ex.Message}");
            }
        }

        string FilePathFor(string npcId)
        {
            var safe = SanitizeFileName(npcId);
            return Path.Combine(_rootDir, $"{safe}.json");
        }

        static string SanitizeFileName(string npcId)
        {
            var s = npcId.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return string.IsNullOrEmpty(s) ? "npc_unknown" : s;
        }
    }

    [Serializable]
    sealed class NpcMemoryDocument
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("npcId")] public string NpcId { get; set; }
        [JsonProperty("subjects")] public List<NpcMemorySubjectSlot> Subjects { get; set; }
    }

    [Serializable]
    sealed class NpcMemorySubjectSlot
    {
        [JsonProperty("characterId")] public string CharacterId { get; set; } = "player";
        [JsonProperty("displayLabel")] public string DisplayLabel { get; set; }
        [JsonProperty("memories")] public List<NpcMemoryPersistedEntry> Memories { get; set; } = new List<NpcMemoryPersistedEntry>();
    }

    [Serializable]
    sealed class NpcMemoryPersistedEntry
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("addedUtc")] public string AddedUtc { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("summary")] public string Summary { get; set; }
    }
}
