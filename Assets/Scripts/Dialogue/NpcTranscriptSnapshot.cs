using System;
using System.Collections.Generic;

namespace Rpg.Dialogue
{
    /// <summary>Transcript plus metadata used when reopening dialogue with an NPC.</summary>
    public sealed class NpcTranscriptSnapshot
    {
        public List<OllamaMessageDto> Messages { get; set; } = new List<OllamaMessageDto>();
        public string LastConversationEndedUtc { get; set; }
        public float? LastConversationEndedGameplayTime { get; set; }
        public string DialoguePlayInstanceId { get; set; }
    }
}
