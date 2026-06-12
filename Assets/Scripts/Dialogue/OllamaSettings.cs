using UnityEngine;

namespace Rpg.Dialogue
{
    [CreateAssetMenu(fileName = "OllamaSettings", menuName = "RPG/Ollama Settings")]
    public sealed class OllamaSettings : ScriptableObject
    {
        [Tooltip("Example: http://127.0.0.1:11434")]
        public string baseUrl = "http://127.0.0.1:11434";

        [Tooltip("Ollama model id, e.g. llama3.2")]
        public string model = "llama3.2";

        [Min(5)] public int timeoutSeconds = 120;

        [Min(32)] public int maxTokens = 512;

        [Tooltip("If the model reply is not valid {\"say\",\"ackYear\"} JSON, still print the raw assistant text in the dialogue log (turn off for strict production behavior).")]
        public bool displayRawReplyOnJsonFailure;

        [Header("Python Policy Orchestrator")]
        [Tooltip("When enabled, dialogue turn orchestration runs through the local Python sidecar.")]
        public bool usePythonPolicyOrchestrator;

        [Tooltip("When enabled, conversation summary generation runs through the local Python sidecar.")]
        public bool usePythonSummaryService;

        [Tooltip("When enabled, narrative generation runs through the local Python sidecar.")]
        public bool usePythonNarrativeGeneration;

        [Tooltip("Example: http://127.0.0.1:8787")]
        public string pythonPolicyBaseUrl = "http://127.0.0.1:8787";

        [Tooltip("Provider base URL used by sidecar for model requests. Example: https://ollama.com for cloud.")]
        public string providerBaseUrl = "http://127.0.0.1:11434";

        [Tooltip("Optional provider API token used by sidecar.")]
        public string providerApiToken = "";

        [Tooltip("Sent as Authorization: Bearer for Unity HTTP calls to Ollama Cloud (https://ollama.com). Leave empty for local Ollama.")]
        public string ollamaApiBearerToken = "";

        public string ResolveChatUrl()
        {
            var root = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl.TrimEnd('/');
            return $"{root}/api/chat";
        }

        /// <summary>Lightweight GET used to verify Ollama is running (lists pulled models).</summary>
        public string ResolveTagsUrl()
        {
            var root = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl.TrimEnd('/');
            return $"{root}/api/tags";
        }
    }
}
