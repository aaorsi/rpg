using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Rpg.Dialogue
{
    public sealed class OllamaClient
    {
        readonly OllamaSettings _settings;

        public OllamaClient(OllamaSettings settings)
        {
            _settings = settings;
        }

        /// <summary>GET /api/tags — confirms host is reachable and optionally whether the configured model id appears in the tag list.</summary>
        public static Task<OllamaReachabilityResult> CheckReachableAsync(OllamaSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null)
                return Task.FromResult(new OllamaReachabilityResult(false, false, null, "OllamaSettings asset is missing."));

            var url = settings.ResolveTagsUrl();
            var request = UnityWebRequest.Get(url);
            request.timeout = Mathf.Clamp(settings.timeoutSeconds, 5, 30);
            ApplyBearerIfPresent(settings, request);
            return RunWebRequestOnMainThread(request, cancellationToken, () =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    var hint = LooksLikeOllamaCloud(settings)
                        ? "Check your API key and network. Keys are created at ollama.com/settings/keys."
                        : "Is `ollama serve` running?";
                    return new OllamaReachabilityResult(false, false, url,
                        $"Cannot reach Ollama at {url}. {request.error} (HTTP {request.responseCode}). {hint}");
                }

                var body = request.downloadHandler.text ?? string.Empty;
                var modelId = string.IsNullOrWhiteSpace(settings.model) ? "llama3.2" : settings.model.Trim();
                var listed = TryModelListed(body, modelId);
                string msg;
                if (listed)
                    msg = $"Reachable: {url}. Model '{modelId}' appears in the tag list.";
                else if (LooksLikeOllamaCloud(settings))
                    msg =
                        $"Reachable: {url}, but no tag matching '{modelId}' was found. Pick a model available on Ollama Cloud or adjust the name.";
                else
                    msg =
                        $"Reachable: {url}, but no tag matching '{modelId}' was found. Run `ollama pull {modelId}` (or set the model name in DefaultOllamaSettings).";
                return new OllamaReachabilityResult(true, listed, url, msg);
            });
        }

        /// <summary>GET /api/tags and return model names (for startup / UI lists).</summary>
        public static Task<OllamaTagNamesResult> FetchTagModelNamesAsync(OllamaSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null)
                return Task.FromResult(OllamaTagNamesResult.Fail(null, "OllamaSettings missing."));

            var url = settings.ResolveTagsUrl();
            var request = UnityWebRequest.Get(url);
            request.timeout = Mathf.Clamp(settings.timeoutSeconds, 5, 30);
            ApplyBearerIfPresent(settings, request);
            return RunWebRequestOnMainThread(request, cancellationToken, () =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    var hint = LooksLikeOllamaCloud(settings)
                        ? "Check API key and network."
                        : $"{request.error} (HTTP {request.responseCode})";
                    return OllamaTagNamesResult.Fail(url, hint);
                }

                var body = request.downloadHandler.text ?? string.Empty;
                var names = ParseModelNamesFromTagsJson(body);
                return OllamaTagNamesResult.Ok(url, names);
            });
        }

        public static IReadOnlyList<string> ParseModelNamesFromTagsJson(string tagsJson)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(tagsJson))
                return list;
            try
            {
                var root = JObject.Parse(tagsJson);
                var models = root["models"] as JArray;
                if (models == null)
                    return list;
                foreach (var m in models)
                {
                    var name = m["name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                        list.Add(name);
                }
            }
            catch
            {
                // ignored
            }

            return list.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        }

        static bool LooksLikeOllamaCloud(OllamaSettings settings)
        {
            var b = (settings.baseUrl ?? string.Empty).Trim().ToLowerInvariant();
            return b.Contains("ollama.com");
        }

        static void ApplyBearerIfPresent(OllamaSettings settings, UnityWebRequest request)
        {
            if (settings == null || request == null)
                return;
            var t = settings.ollamaApiBearerToken;
            if (string.IsNullOrWhiteSpace(t))
                return;
            request.SetRequestHeader("Authorization", "Bearer " + t.Trim());
        }

        static bool TryModelListed(string tagsJson, string modelId)
        {
            try
            {
                var root = JObject.Parse(tagsJson);
                var models = root["models"] as JArray;
                if (models == null)
                    return false;
                foreach (var m in models)
                {
                    var name = m["name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    if (name == modelId || name.StartsWith(modelId + ":", StringComparison.Ordinal) ||
                        name.StartsWith(modelId + "@", StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// UnityWebRequest must be completed and inspected on the main thread. Polling with
        /// <c>while (!op.isDone) await Task.Yield()</c> can resume off-thread and break requests and UI.
        /// </summary>
        static Task<T> RunWebRequestOnMainThread<T>(UnityWebRequest request, CancellationToken cancellationToken, Func<T> buildResult)
        {
            var sync = SynchronizationContext.Current;
            var tcs = new TaskCompletionSource<T>();
            var reg = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() =>
                {
                    try
                    {
                        request.Abort();
                    }
                    catch
                    {
                        // ignored
                    }
                })
                : default;

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                void Finish()
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            tcs.TrySetCanceled(cancellationToken);
                            return;
                        }

                        var result = buildResult();
                        tcs.TrySetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                    finally
                    {
                        if (cancellationToken.CanBeCanceled)
                            reg.Dispose();
                        request.Dispose();
                    }
                }

                if (sync != null)
                    sync.Post(_ => Finish(), null);
                else
                    Finish();
            };

            return tcs.Task;
        }

        public Task<OllamaHttpResult> ChatAsync(
            IReadOnlyList<OllamaMessageDto> messages,
            string modelOverride,
            CancellationToken cancellationToken)
        {
            if (_settings == null)
                return Task.FromResult(OllamaHttpResult.Failure("OllamaSettings missing"));

            var model = string.IsNullOrWhiteSpace(modelOverride) ? _settings.model : modelOverride;
            var dto = new OllamaChatRequestDto
            {
                model = model,
                messages = new List<OllamaMessageDto>(messages),
                stream = false,
                options = new OllamaOptionsDto { num_predict = _settings.maxTokens }
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(dto);
            var url = _settings.ResolveChatUrl();

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = _settings.timeoutSeconds;
            ApplyBearerIfPresent(_settings, request);

            return RunWebRequestOnMainThread(request, cancellationToken, () =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                    return OllamaHttpResult.Failure($"{request.error} ({request.responseCode})");

                var text = request.downloadHandler.text;
                var content = TryExtractAssistantContent(text);
                if (string.IsNullOrEmpty(content))
                    return OllamaHttpResult.Failure("Empty assistant content from Ollama response");

                return OllamaHttpResult.Success(text, content);
            });
        }

        static string TryExtractAssistantContent(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return string.Empty;

            var trimmed = rawJson.Trim();
            var fromObject = TryContentFromChatObject(trimmed);
            if (!string.IsNullOrEmpty(fromObject))
                return fromObject;

            // Streaming NDJSON: one JSON object per line; concatenate message.content deltas.
            return TryConcatenateNdjsonAssistantContent(trimmed);
        }

        static string TryContentFromChatObject(string json)
        {
            try
            {
                var root = JObject.Parse(json);
                var content = root["message"]?["content"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(content))
                    return content;
                // Some reasoning models expose text only under "thinking".
                return root["message"]?["thinking"]?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        static string TryConcatenateNdjsonAssistantContent(string raw)
        {
            var sb = new StringBuilder();
            using (var reader = new StringReader(raw))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var piece = TryContentFromChatObject(line.Trim());
                    if (!string.IsNullOrEmpty(piece))
                        sb.Append(piece);
                }
            }

            return sb.Length > 0 ? sb.ToString() : string.Empty;
        }
    }

    public readonly struct OllamaHttpResult
    {
        public readonly bool IsSuccess;
        public readonly string RawJson;
        public readonly string AssistantContent;
        public readonly string Error;

        OllamaHttpResult(bool success, string rawJson, string assistantContent, string error)
        {
            IsSuccess = success;
            RawJson = rawJson;
            AssistantContent = assistantContent;
            Error = error;
        }

        public static OllamaHttpResult Success(string rawJson, string assistantContent) =>
            new OllamaHttpResult(true, rawJson, assistantContent, null);

        public static OllamaHttpResult Failure(string error) =>
            new OllamaHttpResult(false, null, null, error);
    }

    public readonly struct OllamaReachabilityResult
    {
        public readonly bool HostReachable;
        public readonly bool ModelListed;
        public readonly string RequestUrl;
        public readonly string UserMessage;

        public OllamaReachabilityResult(bool hostReachable, bool modelListed, string requestUrl, string userMessage)
        {
            HostReachable = hostReachable;
            ModelListed = modelListed;
            RequestUrl = requestUrl;
            UserMessage = userMessage;
        }
    }

    public readonly struct OllamaTagNamesResult
    {
        public readonly bool IsSuccess;
        public readonly string RequestUrl;
        public readonly IReadOnlyList<string> ModelNames;
        public readonly string Error;

        OllamaTagNamesResult(bool success, string requestUrl, IReadOnlyList<string> modelNames, string error)
        {
            IsSuccess = success;
            RequestUrl = requestUrl;
            ModelNames = modelNames;
            Error = error;
        }

        public static OllamaTagNamesResult Ok(string requestUrl, IReadOnlyList<string> modelNames) =>
            new OllamaTagNamesResult(true, requestUrl, modelNames ?? Array.Empty<string>(), null);

        public static OllamaTagNamesResult Fail(string requestUrl, string error) =>
            new OllamaTagNamesResult(false, requestUrl, Array.Empty<string>(), error);
    }
}
