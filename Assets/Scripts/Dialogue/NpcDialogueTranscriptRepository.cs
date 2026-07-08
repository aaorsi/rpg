using System;
using System.Collections.Generic;
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
        readonly JsonFileStore _store;

        public NpcDialogueTranscriptRepository(string rootDirectory = null)
        {
            var rootDir = string.IsNullOrEmpty(rootDirectory)
                ? System.IO.Path.Combine(Application.persistentDataPath, "RpgNpcDialogue")
                : rootDirectory;
            _store = new JsonFileStore(rootDir, "NpcDialogueTranscriptRepository");
        }

        /// <summary>
        /// Deletes persisted dialogue transcripts so a new play session starts without prior conversation turns.
        /// </summary>
        public static void ClearAllForNewPlaySession(string rootDirectory = null)
        {
            var dir = string.IsNullOrEmpty(rootDirectory)
                ? System.IO.Path.Combine(Application.persistentDataPath, "RpgNpcDialogue")
                : rootDirectory;
            JsonFileStore.ClearAllJsonFiles(dir, "NpcDialogueTranscriptRepository");
        }

        public List<OllamaMessageDto> TryLoad(string npcId) => LoadSnapshot(npcId).Messages;

        public NpcTranscriptSnapshot LoadSnapshot(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";

            var doc = _store.Load(npcId, () => new NpcDialogueTranscriptDocument(), NormalizeDocument);
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

        /// <summary>
        /// Writes messages. When <paramref name="markConversationEnded"/> is true, records when the UI closed so a later resume can report elapsed time.
        /// </summary>
        public void Save(string npcId, IReadOnlyList<OllamaMessageDto> messages, bool markConversationEnded)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";

            _store.Update(
                npcId,
                () => new NpcDialogueTranscriptDocument(),
                doc =>
                {
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
                    return doc;
                },
                NormalizeDocument,
                NormalizeDocument);
        }

        static void NormalizeDocument(NpcDialogueTranscriptDocument doc)
        {
            if (doc == null)
                return;
            doc.Messages ??= new List<OllamaMessageDto>();
            if (doc.SchemaVersion <= 0)
                doc.SchemaVersion = SchemaVersion;
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
