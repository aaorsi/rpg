using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Rpg.Dialogue
{
    public sealed class ConversationSummaryService
    {
        readonly OllamaClient _client;
        readonly OllamaSettings _settings;

        public ConversationSummaryService(OllamaClient client, OllamaSettings settings)
        {
            _client = client;
            _settings = settings;
        }

        public async Task<NpcConversationSummary> SummarizeAsync(
            string npcId,
            IReadOnlyList<OllamaMessageDto> turns,
            CancellationToken token)
        {
            var fallback = BuildFallback(turns);
            if (_client == null || _settings == null || turns == null || turns.Count == 0)
                return fallback;

            var transcript = JsonConvert.SerializeObject(turns, Formatting.Indented);
            var msgs = new List<OllamaMessageDto>
            {
                new OllamaMessageDto("system",
                    "Summarize dialogue. Return ONLY JSON object: {\"summary\":\"...\",\"learnedFacts\":[...],\"openThreads\":[...],\"relationshipShift\":\"negative|neutral|positive\"}"),
                new OllamaMessageDto("user", $"npcId={npcId}\ntranscript={transcript}")
            };

            try
            {
                var http = await _client.ChatAsync(msgs, _settings.model, token);
                if (!http.IsSuccess)
                    return fallback;
                var normalized = ResponseValidator.NormalizeJsonPayload(http.AssistantContent);
                var parsed = JsonConvert.DeserializeObject<NpcConversationSummary>(normalized);
                if (parsed == null || string.IsNullOrWhiteSpace(parsed.summary))
                    return fallback;
                parsed.createdUtc = DateTime.UtcNow.ToString("o");
                return parsed;
            }
            catch
            {
                return fallback;
            }
        }

        static NpcConversationSummary BuildFallback(IReadOnlyList<OllamaMessageDto> turns)
        {
            var lastUser = string.Empty;
            var lastNpc = string.Empty;
            if (turns != null)
            {
                for (var i = turns.Count - 1; i >= 0; i--)
                {
                    var t = turns[i];
                    if (string.IsNullOrWhiteSpace(lastNpc) && t != null && t.role == "assistant")
                        lastNpc = t.content;
                    if (string.IsNullOrWhiteSpace(lastUser) && t != null && t.role == "user")
                        lastUser = t.content;
                    if (!string.IsNullOrWhiteSpace(lastUser) && !string.IsNullOrWhiteSpace(lastNpc))
                        break;
                }
            }

            return new NpcConversationSummary
            {
                summary = string.IsNullOrWhiteSpace(lastNpc)
                    ? "Conversation ended with limited actionable exchange."
                    : $"Last exchange centered on: {Truncate(lastNpc, 160)}",
                learnedFacts = new List<string> { string.IsNullOrWhiteSpace(lastUser) ? "Player intent unclear." : $"Player asked: {Truncate(lastUser, 100)}" },
                openThreads = new List<string> { "Continue probing NPC goals and required conditions." },
                relationshipShift = "neutral",
                createdUtc = DateTime.UtcNow.ToString("o")
            };
        }

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;
            s = s.Trim();
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }
    }
}
