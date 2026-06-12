using System.Collections.Generic;

namespace Rpg.Dialogue
{
    public sealed class DialogueSession
    {
        readonly int _maxTurnPairs;
        readonly List<OllamaMessageDto> _recentUserAssistant;

        public DialogueSession(int maxTurnPairs = 6, IEnumerable<OllamaMessageDto> seedMessages = null)
        {
            _maxTurnPairs = maxTurnPairs;
            _recentUserAssistant = new List<OllamaMessageDto>();
            if (seedMessages != null)
            {
                foreach (var m in seedMessages)
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.role))
                        continue;
                    _recentUserAssistant.Add(new OllamaMessageDto(m.role, m.content ?? string.Empty));
                }

                Trim();
            }
        }

        public void AddUserLine(string text) =>
            _recentUserAssistant.Add(new OllamaMessageDto("user", text));

        public void AddAssistantLine(string text) =>
            _recentUserAssistant.Add(new OllamaMessageDto("assistant", text));

        public IReadOnlyList<OllamaMessageDto> GetRecentTurnMessages() => _recentUserAssistant;

        public void Trim()
        {
            var maxMessages = _maxTurnPairs * 2;
            if (_recentUserAssistant.Count <= maxMessages)
                return;

            var remove = _recentUserAssistant.Count - maxMessages;
            _recentUserAssistant.RemoveRange(0, remove);
        }
    }
}
