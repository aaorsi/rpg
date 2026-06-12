using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Persists recent user/assistant turns per NPC so closing the panel and talking again resumes context.
    /// Stored under <see cref="Application.persistentDataPath"/> (writable on device builds).
    /// </summary>
    public sealed class NpcDialogueTranscriptRepository
    {
        const int SchemaVersion = 1;
        readonly object _fileLock = new object();
        readonly string _rootDir;

        public NpcDialogueTranscriptRepository(string rootDirectory = null)
        {
            _rootDir = string.IsNullOrEmpty(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "RpgNpcDialogue")
                : rootDirectory;
        }

        /// <summary>
        /// Deletes persisted dialogue transcripts so a new play session starts without prior conversation turns.
        /// </summary>
        public static void ClearAllForNewPlaySession(string rootDirectory = null)
        {
            var dir = string.IsNullOrEmpty(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "RpgNpcDialogue")
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
                        Debug.LogWarning($"[NpcDialogueTranscriptRepository] Could not delete '{path}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NpcDialogueTranscriptRepository] Could not clear transcript directory '{dir}': {ex.Message}");
            }
        }

        public List<OllamaMessageDto> TryLoad(string npcId) => LoadSnapshot(npcId).Messages;

        public NpcTranscriptSnapshot LoadSnapshot(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";

            lock (_fileLock)
            {
                var path = FilePathFor(npcId);
                if (!File.Exists(path))
                    return new NpcTranscriptSnapshot();

                try
                {
                    var json = File.ReadAllText(path);
                    var doc = JsonConvert.DeserializeObject<NpcDialogueTranscriptDocument>(json);
                    if (doc == null)
                        return new NpcTranscriptSnapshot();

                    var messages = doc.Messages == null
                        ? new List<OllamaMessageDto>()
                        : doc.Messages
                            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.role))
                            .Select(m => new OllamaMessageDto(m.role.Trim(), m.content ?? string.Empty))
                            .ToList();

                    return new NpcTranscriptSnapshot
                    {
                        Messages = messages,
                        LastConversationEndedUtc = doc.LastConversationEndedUtc,
                        LastConversationEndedGameplayTime = doc.LastConversationEndedGameplayTime,
                        DialoguePlayInstanceId = doc.DialoguePlayInstanceId
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NpcDialogueTranscriptRepository] Failed to load {path}: {ex.Message}");
                    return new NpcTranscriptSnapshot();
                }
            }
        }

        /// <summary>
        /// Writes messages. When <paramref name="markConversationEnded"/> is true, records when the UI closed so a later resume can report elapsed time.
        /// </summary>
        public void Save(string npcId, IReadOnlyList<OllamaMessageDto> messages, bool markConversationEnded)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";

            lock (_fileLock)
            {
                try
                {
                    Directory.CreateDirectory(_rootDir);
                    var path = FilePathFor(npcId);
                    NpcDialogueTranscriptDocument doc;
                    if (File.Exists(path))
                    {
                        try
                        {
                            doc = JsonConvert.DeserializeObject<NpcDialogueTranscriptDocument>(File.ReadAllText(path))
                                  ?? new NpcDialogueTranscriptDocument();
                        }
                        catch
                        {
                            doc = new NpcDialogueTranscriptDocument();
                        }
                    }
                    else
                    {
                        doc = new NpcDialogueTranscriptDocument();
                    }

                    doc.SchemaVersion = SchemaVersion;
                    doc.NpcId = npcId;
                    doc.Messages = messages == null
                        ? new List<OllamaMessageDto>()
                        : messages.Select(m => new OllamaMessageDto(m.role, m.content ?? string.Empty)).ToList();

                    if (markConversationEnded)
                    {
                        doc.LastConversationEndedUtc = DateTime.UtcNow.ToString("o");
                        doc.LastConversationEndedGameplayTime = Time.time;
                        doc.DialoguePlayInstanceId = DialogueRuntimeSession.PlayInstanceId;
                    }

                    File.WriteAllText(path, JsonConvert.SerializeObject(doc, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NpcDialogueTranscriptRepository] Failed to save transcript for '{npcId}': {ex.Message}");
                }
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
    sealed class NpcDialogueTranscriptDocument
    {
        [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonProperty("npcId")] public string NpcId { get; set; }
        [JsonProperty("messages")] public List<OllamaMessageDto> Messages { get; set; }

        [JsonProperty("lastConversationEndedUtc")]
        public string LastConversationEndedUtc { get; set; }

        [JsonProperty("lastConversationEndedGameplayTime")]
        public float? LastConversationEndedGameplayTime { get; set; }

        [JsonProperty("dialoguePlayInstanceId")]
        public string DialoguePlayInstanceId { get; set; }
    }
}
