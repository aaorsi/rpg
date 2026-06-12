namespace Rpg.Dialogue
{
    public readonly struct DialogueResult
    {
        public readonly string DisplayText;
        public readonly string RawModelText;
        public readonly string Error;
        public readonly bool AckYear;
        public readonly bool WasFallback;
        public readonly AssistantModelPayload Payload;

        /// <summary>Set when HTTP succeeded but the reply was not valid game JSON; can be shown in UI for debugging.</summary>
        public readonly string RawAssistantWhenFailed;

        DialogueResult(string displayText, string rawModelText, string error, bool ackYear, bool wasFallback, string rawAssistantWhenFailed, AssistantModelPayload payload)
        {
            DisplayText = displayText;
            RawModelText = rawModelText;
            Error = error;
            AckYear = ackYear;
            WasFallback = wasFallback;
            RawAssistantWhenFailed = rawAssistantWhenFailed;
            Payload = payload;
        }

        public static DialogueResult FromModel(string displayText, string rawModelText, bool ackYear, AssistantModelPayload payload) =>
            new DialogueResult(displayText, rawModelText, null, ackYear, false, null, payload);

        public static DialogueResult FromFallback(string text) =>
            new DialogueResult(text, null, null, false, true, null, null);

        public static DialogueResult FromError(string error, string rawAssistantWhenFailed = null) =>
            new DialogueResult(null, null, error, false, false, rawAssistantWhenFailed, null);
    }
}
