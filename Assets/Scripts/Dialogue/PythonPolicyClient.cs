using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Rpg.Dialogue
{
    public sealed class PythonPolicyClient
    {
        readonly OllamaSettings _settings;

        public PythonPolicyClient(OllamaSettings settings)
        {
            _settings = settings;
        }

        public Task<PythonPolicyEnvelopeDto> DialogueTurnAsync(PythonDialogueTurnRequestDto request, CancellationToken token)
        {
            var path = "/v1/dialogue/turn";
            return PostAsync(path, request, token);
        }

        public Task<PythonPolicyEnvelopeDto> SummaryAsync(PythonSummaryRequestDto request, CancellationToken token)
        {
            var path = "/v1/dialogue/summary";
            return PostAsync(path, request, token);
        }

        public Task<PythonPolicyEnvelopeDto> NarrativeAsync(PythonNarrativeRequestDto request, CancellationToken token)
        {
            var path = "/v1/narrative/generate";
            return PostAsync(path, request, token);
        }

        public Task<PythonPolicyEnvelopeDto> NpcDeliberationAsync(PythonNpcDeliberationRequestDto request, CancellationToken token)
        {
            var path = "/v1/npc/deliberate";
            return PostAsync(path, request, token);
        }

        Task<PythonPolicyEnvelopeDto> PostAsync<T>(string path, T payload, CancellationToken cancellationToken)
        {
            if (_settings == null)
                return Task.FromResult(new PythonPolicyEnvelopeDto
                {
                    ok = false,
                    error = new PythonPolicyErrorDto { code = "missing_settings", message = "Missing OllamaSettings." }
                });
            var root = string.IsNullOrWhiteSpace(_settings.pythonPolicyBaseUrl)
                ? "http://127.0.0.1:8787"
                : _settings.pythonPolicyBaseUrl.TrimEnd('/');
            var url = root + path;
            var json = JsonConvert.SerializeObject(payload);

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = Mathf.Clamp(_settings.timeoutSeconds, 5, 120);

            return RunWebRequestOnMainThread(request, cancellationToken, () =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                    return new PythonPolicyEnvelopeDto
                    {
                        ok = false,
                        error = new PythonPolicyErrorDto
                        {
                            code = "http_error",
                            message = $"{request.error} ({request.responseCode})"
                        }
                    };
                var text = request.downloadHandler.text ?? string.Empty;
                try
                {
                    var parsed = JsonConvert.DeserializeObject<PythonPolicyEnvelopeDto>(text);
                    if (parsed == null)
                    {
                        return new PythonPolicyEnvelopeDto
                        {
                            ok = false,
                            error = new PythonPolicyErrorDto { code = "parse_error", message = "Empty sidecar envelope." }
                        };
                    }

                    return parsed;
                }
                catch (Exception ex)
                {
                    return new PythonPolicyEnvelopeDto
                    {
                        ok = false,
                        error = new PythonPolicyErrorDto { code = "parse_error", message = ex.Message }
                    };
                }
            });
        }

        static Task<T> RunWebRequestOnMainThread<T>(UnityWebRequest request, CancellationToken cancellationToken,
            Func<T> buildResult)
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
    }
}
