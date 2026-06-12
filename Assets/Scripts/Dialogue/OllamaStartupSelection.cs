namespace Rpg.Dialogue
{
    /// <summary>
    /// Play-session Ollama host choice from the startup title screen (or defaults when that phase is skipped).
    /// </summary>
    public static class OllamaStartupSelection
    {
        public const string DefaultModelId = "gemma3:4b";

        /// <summary>Documented Ollama Cloud API host (paths /api/chat, /api/tags).</summary>
        public const string OllamaCloudBaseUrl = "https://ollama.com";

        static bool _capturedFromTitle;

        /// <summary>Call at the start of each play-mode bootstrap so a new run can capture title choices again.</summary>
        public static void ResetPlaySession()
        {
            _capturedFromTitle = false;
            Mode = OllamaHostKind.Local;
            Model = DefaultModelId;
            CloudApiToken = string.Empty;
        }

        public static OllamaHostKind Mode { get; private set; } = OllamaHostKind.Local;

        public static string Model { get; private set; } = DefaultModelId;

        public static string CloudApiToken { get; private set; } = string.Empty;

        public static void CaptureFromTitle(OllamaHostKind mode, string model, string cloudApiToken)
        {
            Mode = mode;
            Model = string.IsNullOrWhiteSpace(model) ? DefaultModelId : model.Trim();
            CloudApiToken = cloudApiToken != null ? cloudApiToken.Trim() : string.Empty;
            _capturedFromTitle = true;
        }

        /// <summary>When the startup title phase is disabled, call before applying settings so Local + asset model are used.</summary>
        public static void EnsureDefaultsIfTitleSkipped(OllamaSettings templateFromResources)
        {
            if (_capturedFromTitle)
                return;
            Mode = OllamaHostKind.Local;
            if (templateFromResources != null && !string.IsNullOrWhiteSpace(templateFromResources.model))
                Model = templateFromResources.model.Trim();
            else
                Model = DefaultModelId;
            CloudApiToken = string.Empty;
        }

        /// <summary>Apply the captured session to a runtime <see cref="OllamaSettings"/> instance (typically an Instantiate clone).</summary>
        public static void ApplyToRuntimeClone(OllamaSettings clone)
        {
            if (clone == null)
                return;

            clone.model = string.IsNullOrWhiteSpace(Model) ? DefaultModelId : Model.Trim();

            if (Mode == OllamaHostKind.Cloud)
            {
                clone.baseUrl = OllamaCloudBaseUrl;
                clone.ollamaApiBearerToken = CloudApiToken ?? string.Empty;
                clone.providerBaseUrl = OllamaCloudBaseUrl;
                clone.providerApiToken = CloudApiToken ?? string.Empty;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(clone.baseUrl))
                    clone.baseUrl = "http://127.0.0.1:11434";
                clone.ollamaApiBearerToken = string.Empty;
            }
        }
    }

    public enum OllamaHostKind
    {
        Local,
        Cloud
    }
}
