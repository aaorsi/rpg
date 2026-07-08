using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rpg.Audio;
using Rpg.Core;
using Rpg.GameState;
using Rpg.Npc;
using Rpg.Player;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Dialogue
{
    public sealed class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }

        /// <summary>Invoked with the closing NPC id when a dialogue session ends (same frame as <see cref="EndDialogue"/>).</summary>
        public static event Action<string> DialogueClosed;

        /// <summary>Invoked on the main thread after a player line produced a successful assistant reply (before inventory UI refresh).</summary>
        public static event Action<NpcDefinition> AfterNpcDialogueTurnCommitted;

        WorldStateService worldState;
        DialogueUIController ui;
        DialoguePolicy policy;

        OllamaSettings _ollamaSettings;
        OllamaClient _ollamaClient;
        PythonPolicyClient _pythonClient;
        PromptComposer _promptComposer;
        NpcMemoryRepository _memory;
        NpcDialogueTranscriptRepository _transcriptRepo;
        NpcSummaryRepository _summaryRepo;
        NpcPersonaRepository _personaRepo;
        DialogueSession _session;
        NpcDefinition _activeNpc;
        bool _dialogueOpen;
        CancellationTokenSource _cts;
        CancellationTokenSource _summaryCts;
        CancellationTokenSource _ttsCts;
        SynchronizationContext _mainThreadContext;
        string _sessionBreakInset;
        NarrativeGenerationService _generationService;
        NarrativeSessionCanon _narrativeCanon;
        ConversationSummaryService _summaryService;
        NpcActionExecutor _actionExecutor;
        InventoryService _inventory;
        AgreementService _agreements;
        QuestStateService _questState;
        FailForwardService _failForward;
        LocationBindingRegistry _locations;
        NarrativeReferenceValidator _refs;
        NarrativePersistencePolicy _persistencePolicy;
        NarrativeSessionStore _sessionStore;
        NarrativeContentLibrary _contentLibrary;
        string _lastPayloadSummary;
        string _lastCommittedInteractionOutcome;
        PendingTransferDecision _pendingTransfer;
        readonly Dictionary<string, int> _consecutiveRejectByNpc = new Dictionary<string, int>();
        int _runtimeGenerationSeed;
        readonly ChickenTheftDialogueScenario _chickenTheftScenario = new ChickenTheftDialogueScenario();
        string _pendingSkipNpcGuideReturnForNpcId;
        DialogueSpeechPlayer _dialogueSpeechPlayer;
        NpcVoiceAssignmentRepository _voiceAssignments;
        string _heroVoiceId;
        static readonly string[] TtsEnglishVoices =
        {
            "alba",
            "anna",
            "azelma",
            "bill_boerst",
            "caro_davy",
            "charles",
            "cosette",
            "eponine",
            "eve",
            "fantine",
            "george",
            "jane",
            "jean",
            "javert",
            "marius",
            "mary",
            "michael",
            "paul",
            "peter_yearsley",
            "stuart_bell",
            "vera"
        };

        sealed class PendingTransferDecision
        {
            public string npcId;
            public string itemId;
            public int qty;
            public bool npcToHero;
            public string contextNote;
        }

        public sealed class QuickActionState
        {
            public string npcId;
            public List<InventoryViewEntry> heroItems = new List<InventoryViewEntry>();
            public List<InventoryViewEntry> npcItems = new List<InventoryViewEntry>();
        }

        void Awake()
        {
            Instance = this;
            _mainThreadContext = SynchronizationContext.Current;
            if (Application.isPlaying)
            {
                _persistencePolicy = Resources.Load<NarrativePersistencePolicy>("NarrativePersistencePolicy");
                if (_persistencePolicy == null || _persistencePolicy.clearNpcMemoryOnPlay)
                    NpcMemoryRepository.ClearAllForNewPlaySession();
                if (_persistencePolicy == null || _persistencePolicy.clearNpcTranscriptsOnPlay)
                    NpcDialogueTranscriptRepository.ClearAllForNewPlaySession();
                if (_persistencePolicy == null || _persistencePolicy.clearInventoryOnPlay)
                    InventoryService.ClearAllForNewPlaySession();
                if (_persistencePolicy == null || _persistencePolicy.clearQuestStateOnPlay)
                    QuestStateService.ClearAllForNewPlaySession();
                if (_persistencePolicy == null || _persistencePolicy.clearAgreementsOnPlay)
                    AgreementService.ClearAllForNewPlaySession();
                if (_persistencePolicy == null || _persistencePolicy.clearCanonOnPlay)
                    NarrativeSessionStore.ClearAllForNewPlaySession();
                if (_persistencePolicy != null && _persistencePolicy.clearNpcSummariesOnPlay)
                    NpcSummaryRepository.ClearAllForNewPlaySession();
            }
            _summaryCts = new CancellationTokenSource();
            _dialogueSpeechPlayer = new DialogueSpeechPlayer(gameObject);
            if (policy == null)
                policy = ScriptableObject.CreateInstance<DialoguePolicy>();
        }

        /// <summary>Unity UI must be touched from the main thread; async continuations are not guaranteed to land there.</summary>
        void RunUi(Action action)
        {
            if (action == null)
                return;
            var ctx = _mainThreadContext ?? SynchronizationContext.Current;
            if (ctx != null)
            {
                ctx.Post(_ =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }, null);
                return;
            }

            // Awake can run before Unity installs a sync context; never touch UI from a threadpool thread.
            if (isActiveAndEnabled)
            {
                StartCoroutine(RunUiNextFrame(action));
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        static IEnumerator RunUiNextFrame(Action action)
        {
            yield return null;
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _summaryCts?.Cancel();
            _summaryCts?.Dispose();
            _ttsCts?.Cancel();
            _ttsCts?.Dispose();
            _dialogueSpeechPlayer?.Stop();
        }

        /// <summary>Wires core references (typically from runtime bootstrap).</summary>
        public void Wire(WorldStateService ws, DialogueUIController u, DialoguePolicy pol = null)
        {
            worldState = ws;
            ui = u;
            if (pol != null)
                policy = pol;
            if (policy == null)
                policy = ScriptableObject.CreateInstance<DialoguePolicy>();
        }

        public void ConfigureRuntime(
            OllamaSettings settings,
            PromptComposer composer = null,
            NpcMemoryRepository memoryRepository = null,
            NpcDialogueTranscriptRepository transcriptRepository = null)
        {
            _memory = memoryRepository ?? new NpcMemoryRepository();
            _transcriptRepo = transcriptRepository ?? new NpcDialogueTranscriptRepository();
            _summaryRepo = new NpcSummaryRepository();
            _personaRepo = new NpcPersonaRepository();
            _voiceAssignments = new NpcVoiceAssignmentRepository();
            _ollamaSettings = settings;
            _promptComposer = composer ?? new PromptComposer(null, _memory);
            _ollamaClient = new OllamaClient(_ollamaSettings);
            _pythonClient = new PythonPolicyClient(_ollamaSettings);
            _summaryService = new ConversationSummaryService(_ollamaClient, _ollamaSettings);
            _contentLibrary = new NarrativeContentLibrary();
            _inventory = new InventoryService(_contentLibrary);
            _inventory.EnsureActor(InventoryService.HeroActorId);
            _agreements = new AgreementService();
            _questState = new QuestStateService();
            _failForward = new FailForwardService();
            _locations = new LocationBindingRegistry(_contentLibrary.LoadLocationCatalog());
            _sessionStore = new NarrativeSessionStore();
            _refs = BuildReferenceValidator();
            _generationService = new NarrativeGenerationService(_contentLibrary, _ollamaClient, _ollamaSettings, _sessionStore, _refs);
            _runtimeGenerationSeed = ComputeStableSessionSeed();
            _narrativeCanon = _generationService.BuildFallback(_runtimeGenerationSeed, _refsSnapshotNpcIds());
            _heroVoiceId = ResolveOrAssignHeroVoiceId();
            RefreshNarrativeWiring();
            EnsureNpcPersonaBindings();
            NpcChickenTheftConfrontation.EnsureOnAllNpcBindings();
            WarmupTtsInBackground();
        }

        public int RuntimeGenerationSeed => _runtimeGenerationSeed;
        public NarrativeSessionCanon CurrentCanon => _narrativeCanon;

        public async Task GenerateNarrativeCanonAsync(int? seedOverride = null, IReadOnlyList<string> npcIds = null)
        {
            if (_generationService == null)
                return;
            var seed = seedOverride ?? _runtimeGenerationSeed;
            var cts = _cts != null ? _cts.Token : CancellationToken.None;
            var sidecarCanon = await TryAcquireNarrativeCanonFromSidecarAsync(seed, npcIds, cts);
            if (sidecarCanon != null)
            {
                CommitNarrativeCanon(sidecarCanon);
                return;
            }

            var canon = await _generationService.GenerateOrFallbackAsync(seed, npcIds, cts);
            CommitNarrativeCanon(canon);
        }

        public bool IsDialogueOpen => _dialogueOpen;

        /// <summary>GET /api/tags — logs reachability into the dialogue log (for debugging).</summary>
        public async void PingOllamaConnectionFromUi()
        {
            if (ui == null)
                return;
            if (_ollamaSettings == null)
            {
                ui.AppendSystemLine("Ollama: not configured (missing DefaultOllamaSettings in Resources).");
                return;
            }

            ui.AppendSystemLine($"Ollama: requesting {_ollamaSettings.ResolveTagsUrl()} …");
            try
            {
                var token = _cts != null ? _cts.Token : CancellationToken.None;
                var r = await OllamaClient.CheckReachableAsync(_ollamaSettings, token);
                RunUi(() => ui.AppendSystemLine(r.UserMessage));
            }
            catch (System.OperationCanceledException)
            {
                RunUi(() => ui.AppendSystemLine("Ollama: request cancelled."));
            }
            catch (System.Exception ex)
            {
                RunUi(() => ui.AppendSystemLine($"Ollama: check threw — {ex.GetType().Name}: {ex.Message}"));
            }
        }

        /// <summary>Minimal POST to /api/chat — verifies Unity can read assistant text and print it in the dialogue log.</summary>
        public async void PingOllamaChatPostFromUi()
        {
            if (ui == null)
                return;
            if (_ollamaSettings == null || _ollamaClient == null)
            {
                ui.AppendSystemLine("Ollama chat probe: not configured.");
                return;
            }

            ui.AppendSystemLine($"Ollama: POST {_ollamaSettings.ResolveChatUrl()} (minimal probe) …");
            try
            {
                var token = _cts != null ? _cts.Token : CancellationToken.None;
                var messages = new List<OllamaMessageDto>
                {
                    new OllamaMessageDto(
                        "system",
                        "Output ONLY this JSON object, no markdown fences, no other text: {\"say\":\"chat ok\",\"ackYear\":false}"),
                    new OllamaMessageDto("user", "ping")
                };

                var http = await _ollamaClient.ChatAsync(messages, null, token);
                RunUi(() =>
                {
                    if (!http.IsSuccess)
                    {
                        ui.AppendSystemLine($"Ollama chat probe failed: {http.Error}");
                        return;
                    }

                    var body = http.AssistantContent.Trim();
                    ui.AppendSystemLine(
                        $"Ollama chat probe: HTTP OK, extracted assistant text ({body.Length} chars).");
                    if (ResponseValidator.TryParseModelJson(body, out var say, out _))
                        ui.AppendNpcLine(say);
                    else
                        ui.AppendNpcLine(string.IsNullOrEmpty(body) ? "(empty assistant string)" : body);
                });
            }
            catch (OperationCanceledException)
            {
                RunUi(() => ui.AppendSystemLine("Ollama chat probe: cancelled."));
            }
            catch (Exception ex)
            {
                RunUi(() => ui.AppendSystemLine($"Ollama chat probe threw — {ex.GetType().Name}: {ex.Message}"));
            }
        }

        public bool TryStartDialogue(NpcDefinition npc) => TryStartDialogue(npc, null, false);

        public bool TryStartChickenTheftConfrontationDialogue(NpcDefinition npc, string forcedNpcOpening)
        {
            if (!_chickenTheftScenario.CanStartConfrontationDialogue(npc, forcedNpcOpening))
                return false;
            return TryStartDialogue(npc, forcedNpcOpening, true);
        }

        public bool TryStartDialogue(NpcDefinition npc, string forcedNpcOpening, bool chickenTheftSession)
        {
            if (_dialogueOpen || npc == null || ui == null)
                return false;

            _activeNpc = npc;
            var maxPairs = policy != null ? policy.maxRecentTurnPairs : 6;
            var snap = _transcriptRepo != null ? _transcriptRepo.LoadSnapshot(npc.npcId) : new NpcTranscriptSnapshot();
            var saved = snap.Messages;
            _session = saved != null && saved.Count > 0
                ? new DialogueSession(maxPairs, saved)
                : new DialogueSession(maxPairs);
            var resume = !chickenTheftSession && _session.GetRecentTurnMessages().Count > 0;
            _sessionBreakInset = null;
            string resumeUiLine = null;
            if (resume && ConversationResumeNarrative.TryBuild(snap, out var inset, out var uiLine))
            {
                _sessionBreakInset = inset;
                resumeUiLine = uiLine;
            }

            _dialogueOpen = true;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancelDialogueSpeech();
            EnsureNpcVoiceAssigned(npc.npcId);

            ui.Open(npc.displayName);
            _inventory?.EnsureSeededNpc(npc.npcId);
            UpdateInventoryDebugUi(npc.npcId);
            _chickenTheftScenario.OnDialogueStart(npc.npcId, chickenTheftSession);

            if (resume && !string.IsNullOrWhiteSpace(resumeUiLine))
                ui.AppendSystemLine(resumeUiLine);
            if (resume)
                ReplayTranscriptToUi(_session.GetRecentTurnMessages());
            else
            {
                if (!string.IsNullOrWhiteSpace(forcedNpcOpening))
                    EmitNpcLineWithSpeech(FormatGhoulNpcSpeechForUi(npc, forcedNpcOpening.Trim()), forcedNpcOpening.Trim());
                else if (GhoulMenaceController.IsGhoulStoryNpcId(npc.npcId) && !string.IsNullOrWhiteSpace(npc.openingLine))
                    EmitNpcLineWithSpeech(FormatGhoulNpcSpeechForUi(npc, npc.openingLine), npc.openingLine);
                else
                {
                    var generatedIntro = BuildGeneratedOpeningLine(npc.npcId);
                    if (!string.IsNullOrWhiteSpace(generatedIntro))
                        EmitNpcLineWithSpeech(FormatGhoulNpcSpeechForUi(npc, generatedIntro), generatedIntro);
                    else if (!string.IsNullOrWhiteSpace(npc.openingLine))
                        EmitNpcLineWithSpeech(FormatGhoulNpcSpeechForUi(npc, npc.openingLine), npc.openingLine);
                }
            }

            return true;
        }

        void ReplayTranscriptToUi(IReadOnlyList<OllamaMessageDto> messages)
        {
            if (ui == null || messages == null || messages.Count == 0)
                return;

            var lastAsst = -1;
            for (var i = messages.Count - 1; i >= 0; i--)
            {
                var m = messages[i];
                if (m != null && string.Equals(m.role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    lastAsst = i;
                    break;
                }
            }

            if (lastAsst < 0)
                return;

            var asst = messages[lastAsst];
            string npcShown;
            if (ResponseValidator.TryParseModelResponse(asst.content, out var p) && !string.IsNullOrWhiteSpace(p.Say))
                npcShown = p.Say;
            else
                npcShown = string.IsNullOrWhiteSpace(asst.content) ? "…" : asst.content.Trim();

            string lastUser = null;
            for (var j = lastAsst - 1; j >= 0; j--)
            {
                var m = messages[j];
                if (m != null && string.Equals(m.role, "user", StringComparison.OrdinalIgnoreCase))
                {
                    lastUser = m.content;
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(lastUser))
                ui.AppendPlayerLine(lastUser);
            var replayLine = _activeNpc != null && GhoulMenaceController.IsGhoulStoryNpcId(_activeNpc.npcId)
                ? FormatGhoulNpcSpeechForUi(_activeNpc, npcShown)
                : npcShown;
            EmitNpcLineWithSpeech(replayLine, npcShown);
        }

        public void EndDialogue()
        {
            if (!_dialogueOpen)
                return;

            var closingNpcId = _activeNpc != null ? _activeNpc.npcId : null;
            var skipNpcGuideReturnForNpcId = _chickenTheftScenario.OnDialogueEnd(closingNpcId);
            if (!string.IsNullOrWhiteSpace(skipNpcGuideReturnForNpcId))
                _pendingSkipNpcGuideReturnForNpcId = skipNpcGuideReturnForNpcId;

            RequestNpcReturnToOrigin(closingNpcId);
            var closingTurns = _session != null ? new List<OllamaMessageDto>(_session.GetRecentTurnMessages()) : null;
            if (_transcriptRepo != null && _activeNpc != null && _session != null)
            {
                var turns = _session.GetRecentTurnMessages();
                if (turns.Count > 0)
                    _transcriptRepo.Save(_activeNpc.npcId, turns, markConversationEnded: true);
            }
            if (_summaryService != null && _summaryRepo != null && !string.IsNullOrWhiteSpace(closingNpcId) && closingTurns != null && closingTurns.Count > 0)
                _ = SummarizeAndPersistAsync(closingNpcId, closingTurns);

            _dialogueOpen = false;
            _activeNpc = null;
            _session = null;
            _cts?.Cancel();
            CancelDialogueSpeech();
            ui?.Close();
            DialogueClosed?.Invoke(closingNpcId ?? string.Empty);
        }

        /// <summary>Appends the final scream, unlocks Ghoul aggression in the world, then closes dialogue.</summary>
        public void AppendGhoulSnapOutroThenEnd(string screamAllCaps)
        {
            if (ui == null || !_dialogueOpen || _activeNpc == null || !GhoulMenaceController.IsGhoulStoryNpcId(_activeNpc.npcId))
                return;
            if (!string.IsNullOrWhiteSpace(screamAllCaps))
                ui.AppendNpcLine(FormatGhoulNpcSpeechForUi(_activeNpc, screamAllCaps));
            foreach (var g in UnityEngine.Object.FindObjectsByType<GhoulMenaceController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (g == null)
                    continue;
                var b = g.GetComponent<NpcDialogueBinding>();
                if (b?.Definition == null)
                    continue;
                if (string.Equals(b.Definition.npcId, _activeNpc.npcId, StringComparison.OrdinalIgnoreCase))
                    g.NotifyAggressionUnleashedFromDialogue();
            }

            EndDialogue();
        }

        public static string FormatGhoulNpcSpeechForUi(NpcDefinition npc, string text)
        {
            if (npc == null || !GhoulMenaceController.IsGhoulStoryNpcId(npc.npcId) || string.IsNullOrWhiteSpace(text))
                return text;
            var t = text.Trim().Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            t = t.ToUpperInvariant();
            return $"<color=#D32F2F>{t}</color>";
        }

        public void OnNpcGuideArrived(string npcId, string destinationId)
        {
            if (ui == null || string.IsNullOrWhiteSpace(npcId))
                return;
            if (_activeNpc == null || !string.Equals(_activeNpc.npcId, npcId, StringComparison.OrdinalIgnoreCase))
                return;
            var place = string.IsNullOrWhiteSpace(destinationId) ? "the place" : destinationId;
            var line = $"We've arrived at {place}. Ask me what matters here, and I'll tell you what I know.";
            ui.AppendNpcLine(line);
        }

        public void OnNpcGuidePathTooDifficult(string npcId)
        {
            if (ui == null || string.IsNullOrWhiteSpace(npcId))
                return;
            if (_activeNpc == null || !string.Equals(_activeNpc.npcId, npcId, StringComparison.OrdinalIgnoreCase))
                return;
            ui.AppendNpcLine(
                "This path is too difficult for me. Perhaps someone else knows a better way to get there.");
        }

        /// <summary>
        /// Navigation / guide diagnostics (system strip). Safe from background threads via <see cref="RunUi"/>.
        /// </summary>
        public void AppendGuideNavigationSystem(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            Debug.Log("[GuideNav] " + (message.Length > 500 ? message.Substring(0, 500) + "…" : message));
            if (ui == null || _activeNpc != null)
                return;
            const int maxUiChars = 4000;
            var text = message.TrimEnd();
            if (text.Length > maxUiChars)
                text = text.Substring(0, maxUiChars) + "\n…(truncated)";
            RunUi(() =>
            {
                if (ui != null)
                    ui.AppendSystemLine(text);
            });
        }

        void RequestNpcReturnToOrigin(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return;
            if (string.Equals(_pendingSkipNpcGuideReturnForNpcId, npcId, StringComparison.OrdinalIgnoreCase))
            {
                _pendingSkipNpcGuideReturnForNpcId = null;
                return;
            }

            foreach (var b in UnityEngine.Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null || string.IsNullOrWhiteSpace(b.Definition.npcId))
                    continue;
                if (!string.Equals(b.Definition.npcId, npcId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var guide = b.gameObject.GetComponent<NpcGuideToLocation>();
                if (guide != null && guide.IsGuidingActive)
                    break; // Do not interrupt active guide movement when dialogue closes.
                guide?.BeginReturnToOrigin();
                break;
            }
        }

        async Task SummarizeAndPersistAsync(string npcId, IReadOnlyList<OllamaMessageDto> turns)
        {
            try
            {
                var token = _summaryCts != null ? _summaryCts.Token : CancellationToken.None;
                var summary = await TryAcquireSummaryFromSidecarAsync(npcId, turns, token);
                if (summary == null)
                    summary = await _summaryService.SummarizeAsync(npcId, turns, token);
                PersistConversationSummary(npcId, summary);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueManager] Summary failed for npc '{npcId}': {ex.Message}");
            }
        }

        static int ComputeStableSessionSeed()
        {
            unchecked
            {
                var s = DialogueRuntimeSession.PlayInstanceId ?? "default_seed";
                var hash = 17;
                for (var i = 0; i < s.Length; i++)
                    hash = hash * 31 + s[i];
                return hash;
            }
        }

        public async void SubmitPlayerLineFromUi(string rawLine)
        {
            if (!_dialogueOpen || ui == null)
                return;

            var line = (rawLine ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(line))
                return;
            if (TryHandlePendingTransferDecision(line))
                return;
            if (TryHandleNarrativeDebugCommand(line))
                return;
            if (TryHandleInventoryDebugCommand(line))
                return;
            if (_activeNpc == null)
            {
                ui.AppendSystemLine("No active NPC. Use /canon, /quest, /route, /smoke N, /debug prompt, or start a dialogue.");
                return;
            }

            if (policy != null && line.Length > policy.maxPlayerCharacters)
            {
                ui.AppendSystemLine("That message is too long.");
                return;
            }

            ui.SetThinking(true);
            ui.AppendPlayerLine(line);
            QueueDialogueSpeech(line, "hero");

            DialogueResult result;
            try
            {
                result = await SubmitPlayerLineAsync(line, _cts.Token);
            }
            catch (Exception ex)
            {
                RunUi(() =>
                {
                    ui.SetThinking(false);
                    ui.AppendSystemLine($"Request failed: {ex.GetType().Name}: {ex.Message}");
                });
                return;
            }

            RunUi(() =>
            {
                ui.SetThinking(false);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    ui.AppendSystemLine($"Error: {result.Error}");
                    if (_ollamaSettings != null && _ollamaSettings.displayRawReplyOnJsonFailure &&
                        !string.IsNullOrWhiteSpace(result.RawAssistantWhenFailed))
                        ui.AppendNpcLine(result.RawAssistantWhenFailed.Trim());
                    else
                    {
                        var fb = PickFallback(_activeNpc);
                        if (!string.IsNullOrEmpty(fb))
                            ui.AppendNpcLine(FormatGhoulNpcSpeechForUi(_activeNpc, fb));
                    }

                    return;
                }

                if (!string.IsNullOrEmpty(result.DisplayText))
                {
                    EmitNpcLineWithSpeech(FormatGhoulNpcSpeechForUi(_activeNpc, result.DisplayText), result.DisplayText);
                }

                if (result.AckYear && worldState != null)
                    worldState.MarkPlayerAcknowledgedYear();

                if (result.Payload != null && _actionExecutor != null && _activeNpc != null)
                {
                    SidekickFollowActionSynthesizer.TryAppendSyntheticFollowAction(_activeNpc.npcId, result.Payload, line);
                    var nonTransferActions = new List<NpcProposedAction>();
                    foreach (var a in result.Payload.ProposedActions ?? new List<NpcProposedAction>())
                    {
                        if (a == null || string.IsNullOrWhiteSpace(a.ActionType))
                            continue;
                        var t = a.ActionType.Trim().ToLowerInvariant();
                        if (t == NpcActionTypes.GiveObject)
                        {
                            QueueNpcOfferForDecision(_activeNpc.npcId, a, result.Payload);
                            continue;
                        }
                        if (t == NpcActionTypes.ReceiveObject)
                        {
                            QueueHeroGiveForDecision(_activeNpc.npcId, a, result.Payload);
                            continue;
                        }

                        nonTransferActions.Add(a);
                    }

                    _actionExecutor.ExecuteValidated(_activeNpc.npcId, InventoryService.HeroActorId, nonTransferActions);
                }
                if (result.Payload != null && _activeNpc != null)
                {
                    _questState?.ApplySignals(_activeNpc.npcId, result.Payload.MilestoneSignals);
                    AgreementOutcomeAdapter.ApplyFromDialoguePayload(
                        _agreements,
                        _inventory,
                        _activeNpc.npcId,
                        result.Payload,
                        InventoryService.HeroActorId);
                    _lastCommittedInteractionOutcome = NormalizeInteractionOutcome(result.Payload.InteractionOutcome);
                    _lastPayloadSummary = $"outcome={result.Payload.InteractionOutcome}, actions={result.Payload.ProposedActions?.Count ?? 0}, signals={result.Payload.MilestoneSignals?.Count ?? 0}";
                }
                if (_activeNpc != null)
                    UpdateInventoryDebugUi(_activeNpc.npcId);
                RefreshWorldInventoryVisuals();
                if (_activeNpc != null && string.IsNullOrEmpty(result.Error))
                    AfterNpcDialogueTurnCommitted?.Invoke(_activeNpc);
            });
        }

        void AppendDebugLine(string msg)
        {
            if (ui == null || string.IsNullOrWhiteSpace(msg))
                return;
            ui.AppendSystemLine(msg);
            ui.AppendNpcLine(msg);
        }

        bool TryGetNpcProfile(string npcId, out GeneratedNpcProfile profile) =>
            TryGetNpcProfile(_narrativeCanon, npcId, out profile);

        static bool TryGetNpcProfile(NarrativeSessionCanon canon, string npcId, out GeneratedNpcProfile profile)
        {
            profile = null;
            if (canon?.npcProfiles == null || string.IsNullOrWhiteSpace(npcId))
                return false;
            profile = canon.npcProfiles.Find(x =>
                x != null && !string.IsNullOrWhiteSpace(x.npcId) &&
                string.Equals(x.npcId, npcId, StringComparison.OrdinalIgnoreCase));
            return profile != null;
        }

        void EnsureNpcPersonaBindings()
        {
            if (_personaRepo == null)
                return;
            foreach (var binding in UnityEngine.Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (binding == null || binding.Definition == null || string.IsNullOrWhiteSpace(binding.Definition.npcId))
                    continue;
                var persona = _personaRepo.LoadOrCreate(binding.Definition, ResolveNpcType(binding.Definition.npcId));
                if (persona != null)
                    binding.SetPersona(persona);
            }
        }

        NpcPersona ResolveOrCreatePersona(NpcDefinition npc, GeneratedNpcProfile profile)
        {
            if (npc == null || _personaRepo == null)
                return null;

            var npcId = string.IsNullOrWhiteSpace(npc.npcId) ? string.Empty : npc.npcId.Trim();
            foreach (var binding in UnityEngine.Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (binding == null || binding.Definition == null || string.IsNullOrWhiteSpace(binding.Definition.npcId))
                    continue;
                if (!string.Equals(binding.Definition.npcId.Trim(), npcId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (binding.Persona != null)
                    return binding.Persona;
            }

            var persona = _personaRepo.LoadOrCreate(npc, ResolveNpcType(npcId));
            if (persona == null)
                return null;

            if (profile != null)
            {
                if (string.IsNullOrWhiteSpace(persona.personality) && !string.IsNullOrWhiteSpace(profile.personality))
                    persona.personality = profile.personality.Trim();
                if ((persona.socialTraits == null || persona.socialTraits.Count == 0) && profile.socialTraits != null && profile.socialTraits.Count > 0)
                    persona.socialTraits = new Dictionary<string, string>(profile.socialTraits);
                if ((persona.goals == null || persona.goals.Count == 0) && profile.goals != null && profile.goals.Count > 0)
                    persona.goals = new List<string>(profile.goals);
                if ((persona.capabilities == null || persona.capabilities.Count == 0) && profile.capabilities != null && profile.capabilities.Count > 0)
                    persona.capabilities = new List<string>(profile.capabilities);
            }

            _personaRepo.Save(persona);
            return persona;
        }

        static string ResolveNpcType(string npcId)
        {
            if (GhoulMenaceController.IsGhoulStoryNpcId(npcId))
                return "ghoul";
            return SidekickCompanion.BindingRootHasSidekick(npcId) ? "sidekick" : "normal";
        }

        static string ResolvePersonality(GeneratedNpcProfile profile, NpcPersona persona)
        {
            if (profile != null && !string.IsNullOrWhiteSpace(profile.personality))
                return profile.personality.Trim();
            return persona != null && !string.IsNullOrWhiteSpace(persona.personality)
                ? persona.personality.Trim()
                : string.Empty;
        }

        static Dictionary<string, string> ResolveSocialTraits(GeneratedNpcProfile profile, NpcPersona persona)
        {
            if (profile != null && profile.socialTraits != null && profile.socialTraits.Count > 0)
                return new Dictionary<string, string>(profile.socialTraits);
            return persona != null && persona.socialTraits != null
                ? new Dictionary<string, string>(persona.socialTraits)
                : new Dictionary<string, string>();
        }

        static List<string> ResolveGoals(GeneratedNpcProfile profile, NpcPersona persona)
        {
            if (profile != null && profile.goals != null && profile.goals.Count > 0)
                return new List<string>(profile.goals);
            return persona != null && persona.goals != null
                ? new List<string>(persona.goals)
                : new List<string>();
        }

        static List<string> ResolveCapabilities(GeneratedNpcProfile profile, NpcPersona persona, string npcType)
        {
            if (profile != null && profile.capabilities != null && profile.capabilities.Count > 0)
                return new List<string>(profile.capabilities);
            if (persona != null && persona.capabilities != null && persona.capabilities.Count > 0)
                return new List<string>(persona.capabilities);

            return npcType == "sidekick"
                ? new List<string> { "dialogue", "follow_hero", "give", "trade" }
                : npcType == "ghoul"
                    ? new List<string> { "dialogue" }
                    : new List<string> { "dialogue", "give", "trade", "guide_to_location", "refer_to_npc" };
        }

        string ResolveActivePlanContext(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return string.Empty;
            foreach (var binding in UnityEngine.Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (binding == null || binding.Definition == null || string.IsNullOrWhiteSpace(binding.Definition.npcId))
                    continue;
                if (!string.Equals(binding.Definition.npcId.Trim(), npcId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                var agent = binding.GetComponent<NpcAgentController>();
                if (agent == null)
                    return string.Empty;
                if (!agent.IsExecutingPlan)
                    return "No active scheduler plan.";
                var step = string.IsNullOrWhiteSpace(agent.CurrentPrimitiveType) ? "unknown_step" : agent.CurrentPrimitiveType.Trim();
                return $"Executing {step}; remaining_steps={agent.RemainingStepCount}.";
            }

            return string.Empty;
        }

        static string ResolveActiveGoalsContext(string npcId, GeneratedNpcProfile profile, NpcPersona persona)
        {
            var goals = ResolveGoals(profile, persona);
            if (goals == null || goals.Count == 0)
                return string.Empty;
            var key = string.IsNullOrWhiteSpace(npcId) ? "npc" : npcId.Trim();
            return $"{key} goals: {string.Join(" | ", goals)}";
        }

        bool TryHandleNarrativeDebugCommand(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("/", StringComparison.Ordinal))
                return false;
            if (line.StartsWith("/debug prompt", StringComparison.OrdinalIgnoreCase))
            {
                if (_activeNpc == null || _promptComposer == null)
                {
                    ui.AppendSystemLine("No active NPC prompt available.");
                    return true;
                }

                var world = worldState != null ? worldState.GetSnapshot() : new WorldStateSnapshot(2847);
                var npcSummary = _summaryRepo != null ? _summaryRepo.Load(_activeNpc.npcId) : null;
                var inventoryBlock = _inventory != null
                    ? _inventory.BuildPromptBlock(InventoryService.HeroActorId, _activeNpc.npcId)
                    : "(Inventory unavailable)";
                TryGetNpcProfile(_activeNpc.npcId, out var profile);
                var persona = ResolveOrCreatePersona(_activeNpc, profile);
                var npcType = ResolveNpcType(_activeNpc.npcId);
                var msgs = _promptComposer.BuildMessages(
                    _activeNpc,
                    world,
                    _session ?? new DialogueSession(6),
                    "<debug-prompt-preview>",
                    _sessionBreakInset,
                    _narrativeCanon,
                    npcSummary,
                    inventoryBlock,
                    ResolvePersonality(profile, persona),
                    ResolveSocialTraits(profile, persona),
                    ResolveGoals(profile, persona),
                    ResolveCapabilities(profile, persona, npcType),
                    ResolveActivePlanContext(_activeNpc.npcId),
                    ResolveActiveGoalsContext(_activeNpc.npcId, profile, persona));
                var systemPrompt = msgs != null && msgs.Count > 0 ? msgs[0].content ?? string.Empty : string.Empty;
                if (systemPrompt.Length > 6000)
                    systemPrompt = systemPrompt.Substring(0, 6000) + "\n\n...[truncated]";
                ui.AppendSystemLine("Showing prompt preview in dialogue body.");
                ui.AppendNpcLine(systemPrompt);
                return true;
            }
            if (line.StartsWith("/quest", StringComparison.OrdinalIgnoreCase))
            {
                var snap = _questState != null ? _questState.Snapshot() : new List<MilestoneStateEntry>();
                var msg = snap.Count == 0
                    ? "Quest state: no milestones."
                    : "Quest state: " + string.Join(" | ", snap.ConvertAll(m => $"{m.milestoneId}:{m.status}"));
                AppendDebugLine(msg);
                return true;
            }

            if (line.StartsWith("/canon", StringComparison.OrdinalIgnoreCase))
            {
                if (_narrativeCanon == null)
                {
                    AppendDebugLine("No session canon loaded.");
                }
                else
                {
                    var canonParts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    var expanded = canonParts.Length > 1
                        && (string.Equals(canonParts[1], "full", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(canonParts[1], "expand", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(canonParts[1], "npc", StringComparison.OrdinalIgnoreCase));
                    if (!expanded)
                    {
                        var msg = $"Canon: premise={_narrativeCanon.premiseId}, world={_narrativeCanon.worldId}, objective={_narrativeCanon.finalObjective}. Use /canon full for NPC consistency.";
                        AppendDebugLine(msg);
                    }
                    else
                    {
                        var report = BuildCanonNpcConsistencyReport();
                        ui.AppendSystemLine("Canon consistency report emitted.");
                        ui.AppendNpcLine(report);
                    }
                }
                return true;
            }
            if (line.StartsWith("/npc", StringComparison.OrdinalIgnoreCase))
            {
                if (_activeNpc == null)
                {
                    AppendDebugLine("No active NPC.");
                    return true;
                }
                AppendDebugLine(BuildActiveNpcDebugSummary(_activeNpc.npcId));
                return true;
            }

            if (line.StartsWith("/route", StringComparison.OrdinalIgnoreCase))
            {
                if (_narrativeCanon?.routesByMilestone == null || _narrativeCanon.routesByMilestone.Count == 0)
                {
                    AppendDebugLine("No route map available.");
                }
                else
                {
                    var parts = new List<string>();
                    foreach (var kv in _narrativeCanon.routesByMilestone)
                        parts.Add($"{kv.Key}=>{kv.Value}");
                    AppendDebugLine("Routes: " + string.Join(" | ", parts));
                }
                return true;
            }

            if (line.StartsWith("/smoke", StringComparison.OrdinalIgnoreCase))
            {
                var n = 3;
                var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 1 && int.TryParse(tokens[1], out var parsed))
                    n = Mathf.Clamp(parsed, 1, 20);
                RunSmokeChecks(n);
                return true;
            }

            return false;
        }

        bool TryHandleInventoryDebugCommand(string line)
        {
            if (_inventory == null || _activeNpc == null || string.IsNullOrWhiteSpace(line))
                return false;
            if (!line.StartsWith("/inv", StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 || string.Equals(parts[1], "help", StringComparison.OrdinalIgnoreCase))
            {
                var help =
                    "Inventory commands:\n" +
                    "- /inv list\n" +
                    "- /inv hero add <itemId> <qty>\n" +
                    "- /inv hero remove <itemId> <qty>\n" +
                    "- /inv npc add <itemId> <qty>\n" +
                    "- /inv npc remove <itemId> <qty>\n" +
                    "- /inv give <itemId> <qty>\n" +
                    "- /inv take <itemId> <qty>";
                ui.AppendSystemLine(
                    "Inventory help shown in NPC panel.");
                ui.AppendNpcLine(help);
                UpdateInventoryDebugUi(_activeNpc.npcId);
                RefreshWorldInventoryVisuals();
                return true;
            }

            if (string.Equals(parts[1], "list", StringComparison.OrdinalIgnoreCase))
            {
                UpdateInventoryDebugUi(_activeNpc.npcId);
                ui.AppendSystemLine("Inventory list refreshed.");
                ui.AppendNpcLine("Inventory list refreshed. Press I for quick actions to browse items.");
                return true;
            }

            var npcId = _activeNpc.npcId;
            if (string.Equals(parts[1], "give", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 4)
                {
                    ui.AppendSystemLine("Usage: /inv give <itemId> <qty>");
                    return true;
                }

                if (!int.TryParse(parts[3], out var qtyGive))
                    qtyGive = 1;
                var display = _inventory.GetItemDisplayName(parts[2]);
                var ok = _inventory.TryTransfer(npcId, InventoryService.HeroActorId, parts[2], qtyGive);
                var msg = ok ? $"Transfer OK: NPC -> Hero ({display} x{Mathf.Max(1, qtyGive)})." : $"Transfer failed. NPC may not have {display} x{Mathf.Max(1, qtyGive)}.";
                AppendDebugLine(msg);
                UpdateInventoryDebugUi(npcId);
                RefreshWorldInventoryVisuals();
                return true;
            }

            if (string.Equals(parts[1], "take", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 4)
                {
                    ui.AppendSystemLine("Usage: /inv take <itemId> <qty>");
                    return true;
                }

                if (!int.TryParse(parts[3], out var qtyTake))
                    qtyTake = 1;
                var transferQty = Mathf.Max(1, qtyTake);
                var accepted = EvaluateNpcWillingnessForTransfer(npcId, "receive", parts[2], transferQty, out var acceptanceReason);
                var ok = accepted && _inventory.TryTransfer(InventoryService.HeroActorId, npcId, parts[2], transferQty);
                var display = _inventory.GetItemDisplayName(parts[2]);
                var msg = !accepted
                    ? $"NPC refused {display} x{transferQty}. {acceptanceReason}"
                    : (ok
                        ? $"Transfer OK: Hero -> NPC ({display} x{transferQty}). NPC accepts: {acceptanceReason}"
                        : $"Transfer failed. Hero may not have {display} x{transferQty}.");
                AppendDebugLine(msg);
                UpdateInventoryDebugUi(npcId);
                RefreshWorldInventoryVisuals();
                return true;
            }

            if (parts.Length < 5)
            {
                ui.AppendSystemLine("Usage: /inv <hero|npc> <add|remove> <itemId> <qty>");
                return true;
            }

            var actorKey = parts[1].ToLowerInvariant();
            var op = parts[2].ToLowerInvariant();
            var itemId = parts[3];
            if (!int.TryParse(parts[4], out var qty))
                qty = 1;
            qty = Mathf.Max(1, qty);

            var actorId = actorKey == "hero" ? InventoryService.HeroActorId : actorKey == "npc" ? npcId : null;
            if (string.IsNullOrWhiteSpace(actorId))
            {
                ui.AppendSystemLine("Actor must be 'hero' or 'npc'.");
                return true;
            }

            if (!_inventory.IsKnownItem(itemId))
            {
                var msg = $"Unknown itemId: {itemId}. Use /inv list to see readable names and valid IDs.";
                AppendDebugLine(msg);
                return true;
            }

            if (op == "add")
            {
                _inventory.AddItem(actorId, itemId, qty);
                AppendDebugLine($"Added {_inventory.GetItemDisplayName(itemId)} x{qty} to {actorKey}.");
            }
            else if (op == "remove")
            {
                var ok = _inventory.RemoveItem(actorId, itemId, qty);
                AppendDebugLine(ok ? $"Removed {_inventory.GetItemDisplayName(itemId)} x{qty} from {actorKey}." : $"Remove failed. {actorKey} may not have enough quantity.");
            }
            else
            {
                ui.AppendSystemLine("Operation must be 'add' or 'remove'.");
                return true;
            }

            UpdateInventoryDebugUi(npcId);
            RefreshWorldInventoryVisuals();
            return true;
        }

        void CommitNarrativeCanon(NarrativeSessionCanon canon)
        {
            if (canon == null)
                return;
            _narrativeCanon = canon;
            RefreshNarrativeWiring();
        }

        async Task<NarrativeSessionCanon> TryAcquireNarrativeCanonFromSidecarAsync(
            int seed,
            IReadOnlyList<string> npcIds,
            CancellationToken cancellationToken)
        {
            if (_ollamaSettings == null || !_ollamaSettings.usePythonNarrativeGeneration || _pythonClient == null || _generationService == null)
                return null;

            var fallback = _generationService.BuildFallback(seed, npcIds);
            var req = new PythonNarrativeRequestDto
            {
                requestId = Guid.NewGuid().ToString("N"),
                model = _ollamaSettings.model,
                seed = seed,
                fallbackCanonJson = Newtonsoft.Json.JsonConvert.SerializeObject(fallback),
                apiToken = _ollamaSettings.providerApiToken,
                providerBaseUrl = _ollamaSettings.providerBaseUrl
            };
            var envelope = await _pythonClient.NarrativeAsync(req, cancellationToken);
            if (envelope != null && envelope.ok && envelope.narrative != null
                && !string.IsNullOrWhiteSpace(envelope.narrative.canonJson))
            {
                NarrativeSessionCanon canon = null;
                string parseError = null;
                if (ResponseValidator.TryBuildNarrativeCanonFromSidecarDto(envelope.narrative, out canon, out parseError))
                    return canon;
                if (!string.IsNullOrWhiteSpace(parseError))
                    DialogueTelemetry.Log("PythonNarrativeParseFail", parseError);
                return null;
            }

            DialogueTelemetry.Log("PythonNarrativeFail", envelope?.error?.message ?? "unknown sidecar error");
            return null;
        }

        void PersistConversationSummary(string npcId, NpcConversationSummary summary)
        {
            if (summary == null || _summaryRepo == null || string.IsNullOrWhiteSpace(npcId))
                return;
            _summaryRepo.Save(npcId, summary);
            DialogueTelemetry.Log("ConversationSummarySaved", $"npc={npcId}");
        }

        async Task<NpcConversationSummary> TryAcquireSummaryFromSidecarAsync(
            string npcId,
            IReadOnlyList<OllamaMessageDto> turns,
            CancellationToken cancellationToken)
        {
            if (_ollamaSettings == null || !_ollamaSettings.usePythonSummaryService || _pythonClient == null)
                return null;

            var req = new PythonSummaryRequestDto
            {
                requestId = Guid.NewGuid().ToString("N"),
                model = _ollamaSettings.model,
                npcId = npcId,
                turns = turns != null ? new List<OllamaMessageDto>(turns) : new List<OllamaMessageDto>(),
                apiToken = _ollamaSettings.providerApiToken,
                providerBaseUrl = _ollamaSettings.providerBaseUrl
            };
            var envelope = await _pythonClient.SummaryAsync(req, cancellationToken);
            if (envelope != null && envelope.ok && envelope.summary != null
                && ResponseValidator.TryBuildSummaryFromSidecarDto(envelope.summary, out var summary))
                return summary;

            DialogueTelemetry.Log("PythonSummaryFail", envelope?.error?.message ?? "unknown sidecar error");
            return null;
        }

        DialogueResult CommitSuccessfulTurn(string playerLine, string assistantLineForSession, string rawAssistant, AssistantModelPayload payload)
        {
            _session.AddUserLine(playerLine);
            _session.AddAssistantLine(assistantLineForSession);
            _session.Trim();
            if (_memory != null && payload.MemoryAdds.Count > 0)
                _memory.TryAppendCandidates(_activeNpc.npcId, payload.MemoryAdds);
            if (_transcriptRepo != null && _activeNpc != null)
                _transcriptRepo.Save(_activeNpc.npcId, _session.GetRecentTurnMessages(), markConversationEnded: false);
            ProcessOutcomeTelemetryAndFailForward(_activeNpc.npcId, payload);
            ApplyFailForwardForMilestones(_activeNpc.npcId);
            _sessionBreakInset = null;
            return DialogueResult.FromModel(payload.Say, rawAssistant, payload.AckYear, payload);
        }

        void ProcessOutcomeTelemetryAndFailForward(string npcId, AssistantModelPayload payload)
        {
            DialogueTurnCommitLogic.ApplyOutcomeTelemetryAndFailForward(npcId, payload, _consecutiveRejectByNpc);
        }

        public async Task<DialogueResult> SubmitPlayerLineAsync(string playerLine, CancellationToken cancellationToken)
        {
            if (_ollamaSettings == null || _ollamaClient == null || _promptComposer == null)
                return DialogueResult.FromError("Dialogue not configured (missing Ollama settings).");

            var world = worldState != null ? worldState.GetSnapshot() : new WorldStateSnapshot(2847);
            var npcSummary = _summaryRepo != null && _activeNpc != null
                ? _summaryRepo.Load(_activeNpc.npcId)
                : null;
            var inventoryBlock = _inventory != null && _activeNpc != null
                ? _inventory.BuildPromptBlock(InventoryService.HeroActorId, _activeNpc.npcId)
                : "(Inventory unavailable)";
            if (_activeNpc == null)
                return DialogueResult.FromError("No active NPC.");
            TryGetNpcProfile(_activeNpc.npcId, out var profile);
            var persona = ResolveOrCreatePersona(_activeNpc, profile);
            var type = ResolveNpcType(_activeNpc.npcId);
            var activePlanContext = ResolveActivePlanContext(_activeNpc.npcId);
            var activeGoalsContext = ResolveActiveGoalsContext(_activeNpc.npcId, profile, persona);
            if (_ollamaSettings.usePythonPolicyOrchestrator && _pythonClient != null && _activeNpc != null)
            {
                try
                {
                    var req = new PythonDialogueTurnRequestDto
                    {
                        requestId = Guid.NewGuid().ToString("N"),
                        model = string.IsNullOrWhiteSpace(_activeNpc.ollamaModelOverride) ? _ollamaSettings.model : _activeNpc.ollamaModelOverride,
                        apiToken = _ollamaSettings.providerApiToken,
                        providerBaseUrl = _ollamaSettings.providerBaseUrl,
                        npc = new PythonNpcContextDto
                        {
                            npcId = _activeNpc.npcId ?? string.Empty,
                            displayName = _activeNpc.displayName ?? string.Empty,
                            npcType = type,
                            roleSummary = _activeNpc.roleSummary ?? string.Empty,
                            toneAndVocabulary = _activeNpc.toneAndVocabulary ?? string.Empty,
                            safetyRules = _activeNpc.safetyRules ?? string.Empty,
                            personality = ResolvePersonality(profile, persona),
                            socialTraits = ResolveSocialTraits(profile, persona),
                            goals = ResolveGoals(profile, persona),
                            capabilities = ResolveCapabilities(profile, persona, type),
                            activePlanContext = activePlanContext,
                            activeGoalsContext = activeGoalsContext
                        },
                        turn = new PythonTurnContextDto
                        {
                            worldFacts = world.ToFactsBlock(),
                            memoryBlock = _memory != null ? _memory.BuildPromptBlock(_activeNpc.npcId) : string.Empty,
                            summaryBlock = BuildSummaryBlockForSidecar(npcSummary),
                            inventoryBlock = inventoryBlock,
                            surroundingsBlock = NpcSurroundingsScanner.BuildPromptBlock(_activeNpc.npcId),
                            narrativeBlock = BuildNarrativeBlockForSidecar(_narrativeCanon, _activeNpc.npcId),
                            recentTurns = _session != null ? new List<OllamaMessageDto>(_session.GetRecentTurnMessages()) : new List<OllamaMessageDto>(),
                            latestPlayerLine = playerLine ?? string.Empty
                        }
                    };
                    var envelope = await _pythonClient.DialogueTurnAsync(req, cancellationToken);
                    if (envelope != null && envelope.ok && envelope.dialogue != null && !string.IsNullOrWhiteSpace(envelope.dialogue.say))
                    {
                        var sidecarPayload = ResponseValidator.BuildPayloadFromDialogueDto(envelope.dialogue);
                        var assistantLineForSession = envelope.dialogue.rawAssistant ?? envelope.dialogue.say;
                        return CommitSuccessfulTurn(playerLine, assistantLineForSession, envelope.dialogue.rawAssistant, sidecarPayload);
                    }

                    var err = envelope?.error?.message ?? "Sidecar dialogue call failed.";
                    DialogueTelemetry.Log("PythonDialogueFail", err + " | falling back to direct Ollama call.");
                }
                catch (System.Exception ex)
                {
                    DialogueTelemetry.Log("PythonDialogueException", ex.Message + " | falling back to direct Ollama call.");
                }
            }
            var messages = _promptComposer.BuildMessages(
                _activeNpc,
                world,
                _session,
                playerLine,
                _sessionBreakInset,
                _narrativeCanon,
                npcSummary,
                inventoryBlock,
                ResolvePersonality(profile, persona),
                ResolveSocialTraits(profile, persona),
                ResolveGoals(profile, persona),
                ResolveCapabilities(profile, persona, type),
                activePlanContext,
                activeGoalsContext);

            var model = string.IsNullOrWhiteSpace(_activeNpc.ollamaModelOverride)
                ? _ollamaSettings.model
                : _activeNpc.ollamaModelOverride;

            OllamaHttpResult http;
            try
            {
                http = await _ollamaClient.ChatAsync(messages, model, cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                return DialogueResult.FromError("Cancelled.");
            }

            if (!http.IsSuccess)
                return DialogueResult.FromError(http.Error);

            var raw = http.AssistantContent.Trim();
            if (ResponseValidator.TryParseModelResponse(raw, out var payload))
                return CommitSuccessfulTurn(playerLine, raw, raw, payload);

            var preview = raw.Length > 220 ? raw.Substring(0, 220) + "…" : raw;
            return DialogueResult.FromError(
                "Model returned text we could not parse as dialogue JSON (need a \"say\" string). " +
                $"Preview: {preview}",
                raw);
        }

        static string PickFallback(NpcDefinition npc)
        {
            if (npc == null || npc.fallbackLines == null || npc.fallbackLines.Length == 0)
                return "I… need a moment. Ask again?";
            return npc.fallbackLines[UnityEngine.Random.Range(0, npc.fallbackLines.Length)];
        }

        void RunSmokeChecks(int n)
        {
            if (_generationService == null || _refs == null)
            {
                ui.AppendSystemLine("Smoke unavailable: generation/validator not ready.");
                return;
            }

            var ok = 0;
            var fail = 0;
            for (var i = 0; i < n; i++)
            {
                var seed = _runtimeGenerationSeed + i * 17;
                var canon = _generationService.BuildFallback(seed, _refsSnapshotNpcIds());
                var issues = _refs.ValidateCanon(canon);
                var routesOk = true;
                foreach (var m in canon.criticalMilestones)
                {
                    if (!canon.routesByMilestone.TryGetValue(m, out var r) || r < 2)
                    {
                        routesOk = false;
                        break;
                    }
                }

                if (issues.Count == 0 && routesOk)
                    ok++;
                else
                    fail++;
            }

            ui.AppendSystemLine($"/smoke {n}: pass={ok}, fail={fail}");
        }

        IReadOnlyList<string> _refsSnapshotNpcIds()
        {
            var ids = new List<string>();
            foreach (var b in UnityEngine.Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.Definition == null || string.IsNullOrWhiteSpace(b.Definition.npcId))
                    continue;
                if (!ids.Contains(b.Definition.npcId))
                    ids.Add(b.Definition.npcId);
            }
            return ids;
        }

        void ApplyFailForwardForMilestones(string npcId)
        {
            if (_failForward == null || _questState == null)
                return;
            var snap = _questState.Snapshot();
            _failForward.NoteTurnWithoutProgress(snap);
            var escalations = _failForward.BuildEscalationSignals(snap);
            if (escalations.Count == 0)
                return;
            _questState.ApplySignals(npcId, escalations);
            DialogueTelemetry.Log("FailForwardMilestoneEscalation", string.Join(" | ", escalations));
        }

        NarrativeReferenceValidator BuildReferenceValidator()
        {
            var npcIds = _refsSnapshotNpcIds();
            var itemIds = _inventory != null ? _inventory.GetAllKnownItemIds() : Array.Empty<string>();
            // Catalog ids (e.g. warehouse): anchors may appear later in the session; do not require a bound transform at init.
            var locIdsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_locations != null)
            {
                foreach (var id in _locations.AllCatalogLocationIds())
                    if (!string.IsNullOrWhiteSpace(id))
                        locIdsSet.Add(id.Trim());
            }
            if (_contentLibrary != null)
            {
                var cat = _contentLibrary.LoadLocationCatalog();
                if (cat?.locations != null)
                {
                    foreach (var e in cat.locations)
                    {
                        if (e != null && !string.IsNullOrWhiteSpace(e.id))
                            locIdsSet.Add(e.id.Trim());
                    }
                }
            }
            var locIds = new List<string>(locIdsSet);
            var milestoneIds = _narrativeCanon != null ? _narrativeCanon.criticalMilestones : new List<string>();
            return new NarrativeReferenceValidator(npcIds, itemIds, locIds, milestoneIds);
        }

        void RefreshNarrativeWiring()
        {
            _refs = BuildReferenceValidator();
            if (_contentLibrary != null && _ollamaClient != null)
                _generationService = new NarrativeGenerationService(_contentLibrary, _ollamaClient, _ollamaSettings, _sessionStore, _refs);
            _questState?.InitializeFromCanon(_narrativeCanon);
            _actionExecutor = new NpcActionExecutor(_inventory, _locations, _questState, _refs);
        }

        /// <summary>
        /// Refreshes the inventory debug panel (hero + NPC inventories, milestones, profile).
        /// Callers invoke this after inventory mutations during dialogue (e.g. /inv, give/take/trade).
        /// No-ops only when UI or inventory services are unavailable — not when an NPC session is active.
        /// </summary>
        void UpdateInventoryDebugUi(string npcId)
        {
            if (ui == null || _inventory == null)
                return;
            var hero = _inventory.DescribeInventory(InventoryService.HeroActorId);
            var npc = _inventory.DescribeInventory(npcId);
            var quests = _questState != null ? _questState.Snapshot() : new List<MilestoneStateEntry>();
            var questText = quests.Count == 0 ? "(none)" : string.Join(" | ", quests.ConvertAll(q => $"{q.milestoneId}:{q.status}"));
            ui.SetInventoryDebug(
                "Hero Inventory:\n" + hero +
                "\n\nNPC Inventory:\n" + npc +
                "\n\nMilestones:\n" + questText +
                "\n\nNPC Profile:\n" + BuildActiveNpcDebugSummary(npcId) +
                "\n\nLast Payload:\n" + (string.IsNullOrWhiteSpace(_lastPayloadSummary) ? "(none)" : _lastPayloadSummary));
        }

        string BuildActiveNpcDebugSummary(string npcId)
        {
            if (!TryGetNpcProfile(npcId, out var p))
                return string.IsNullOrWhiteSpace(npcId) || _narrativeCanon?.npcProfiles == null
                    ? "(no generated profile)"
                    : "(no generated profile match)";
            var traits = p.socialTraits == null || p.socialTraits.Count == 0
                ? "(none)"
                : string.Join(", ", p.socialTraits);
            var goals = p.goals == null || p.goals.Count == 0
                ? "(none)"
                : string.Join(" | ", p.goals);
            return $"occupation={p.occupation}, personality={p.personality}\ntraits={traits}\ngoals={goals}";
        }

        string BuildCanonNpcConsistencyReport()
        {
            if (_narrativeCanon == null)
                return "No session canon loaded.";
            var sb = new StringBuilder(1200);
            sb.AppendLine("=== CANON CONSISTENCY REPORT ===");
            sb.AppendLine($"premise={_narrativeCanon.premiseId} | world={_narrativeCanon.worldId}");
            sb.AppendLine($"objective={_narrativeCanon.finalObjective}");
            if (_narrativeCanon.npcProfiles == null || _narrativeCanon.npcProfiles.Count == 0)
            {
                sb.AppendLine("(No npcProfiles in canon)");
                return sb.ToString().TrimEnd();
            }

            foreach (var p in _narrativeCanon.npcProfiles)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.npcId))
                    continue;
                var type = string.IsNullOrWhiteSpace(p.npcType) ? "normal" : p.npcType.Trim().ToLowerInvariant();
                var caps = p.capabilities == null || p.capabilities.Count == 0 ? "(none)" : string.Join(", ", p.capabilities);
                var key = p.keyInformation == null || p.keyInformation.Count == 0 ? "(none)" : string.Join(" | ", p.keyInformation);
                var req = p.followerRecruitmentRequirements == null || p.followerRecruitmentRequirements.Count == 0
                    ? "(none)"
                    : string.Join(" | ", p.followerRecruitmentRequirements);
                var hasFollow = p.capabilities != null && p.capabilities.Exists(c => string.Equals(c, NpcActionTypes.FollowHero, StringComparison.OrdinalIgnoreCase));
                var hasGuide = p.capabilities != null && (p.capabilities.Exists(c => string.Equals(c, "guide_to_location", StringComparison.OrdinalIgnoreCase))
                                                          || p.capabilities.Exists(c => string.Equals(c, "refer_to_npc", StringComparison.OrdinalIgnoreCase)));

                sb.AppendLine($"NPC {p.npcId} ({type})");
                sb.AppendLine($"  capabilities: {caps}");
                sb.AppendLine($"  unique knowledge: {key}");
                sb.AppendLine($"  follower requirements: {req}");
                if (type == "sidekick")
                {
                    if (hasGuide)
                        sb.AppendLine("  warning: sidekick has guide capability in profile; runtime executor still blocks guiding.");
                    if (!hasFollow)
                        sb.AppendLine($"  warning: sidekick missing {NpcActionTypes.FollowHero} capability.");
                }
                else
                {
                    if (hasFollow)
                        sb.AppendLine($"  warning: normal NPC profile exposes {NpcActionTypes.FollowHero}; runtime executor will reject it.");
                }
            }

            return sb.ToString().TrimEnd();
        }

        string BuildGeneratedOpeningLine(string npcId)
        {
            if (GhoulMenaceController.IsGhoulStoryNpcId(npcId))
                return null;
            if (!TryGetNpcProfile(npcId, out var p))
                return null;
            var personality = string.IsNullOrWhiteSpace(p.personality) ? "reserved" : p.personality.Trim().ToLowerInvariant();

            if (personality.Contains("suspicious") || personality.Contains("wary") || personality.Contains("paranoid"))
                return "What do you want?";
            if (personality.Contains("warm") || personality.Contains("kind") || personality.Contains("friendly") || personality.Contains("helpful"))
                return "Good to see you. How can I help?";
            if (personality.Contains("cunning") || personality.Contains("trick") || personality.Contains("deceptive"))
                return "Well then... what are you really after?";
            if (personality.Contains("proud") || personality.Contains("stern") || personality.Contains("strict"))
                return "Speak clearly. What do you need?";
            if (personality.Contains("curious") || personality.Contains("naive"))
                return "Oh, hello! What brings you here?";

            return "Hello. What do you need?";
        }

        static string BuildSummaryBlockForSidecar(NpcConversationSummary summary)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.summary))
                return "(No prior conversation summary.)";
            var sb = new StringBuilder();
            sb.AppendLine(summary.summary.Trim());
            if (summary.learnedFacts != null && summary.learnedFacts.Count > 0)
            {
                sb.AppendLine("LEARNED_FACTS:");
                foreach (var fact in summary.learnedFacts)
                    sb.AppendLine("- " + fact);
            }
            if (summary.openThreads != null && summary.openThreads.Count > 0)
            {
                sb.AppendLine("OPEN_THREADS:");
                foreach (var t in summary.openThreads)
                    sb.AppendLine("- " + t);
            }
            return sb.ToString().TrimEnd();
        }

        static string BuildNarrativeBlockForSidecar(NarrativeSessionCanon canon, string npcId)
        {
            if (canon == null)
                return "(No narrative canon loaded.)";
            var sb = new StringBuilder();
            sb.AppendLine($"SESSION_ID: {canon.sessionId}");
            sb.AppendLine($"SUMMARY: {canon.summary}");
            sb.AppendLine($"FINAL_OBJECTIVE: {canon.finalObjective}");
            if (canon.criticalMilestones != null && canon.criticalMilestones.Count > 0)
            {
                sb.AppendLine("CRITICAL_MILESTONES:");
                foreach (var m in canon.criticalMilestones)
                    sb.AppendLine("- " + m);
            }

            if (TryGetNpcProfile(canon, npcId, out var profile))
            {
                sb.AppendLine("NPC_PROFILE:");
                sb.AppendLine($"- npcType={profile.npcType}");
                sb.AppendLine($"- personality={profile.personality}");
                if (profile.goals != null && profile.goals.Count > 0)
                    sb.AppendLine("- goals=" + string.Join(" | ", profile.goals));
            }
            return sb.ToString().TrimEnd();
        }

        void QueueNpcOfferForDecision(string npcId, NpcProposedAction action, AssistantModelPayload payload)
        {
            QueueTransferDecision(npcId, action, payload, npcToHero: true);
        }

        void QueueHeroGiveForDecision(string npcId, NpcProposedAction action, AssistantModelPayload payload)
        {
            QueueTransferDecision(npcId, action, payload, npcToHero: false);
        }

        void QueueTransferDecision(string npcId, NpcProposedAction action, AssistantModelPayload payload, bool npcToHero)
        {
            if (_inventory == null || action == null)
                return;
            var itemId = (action.TargetId ?? string.Empty).Trim();
            var qty = Mathf.Max(1, Mathf.RoundToInt(action.Quantity <= 0f ? 1f : action.Quantity));
            if (string.IsNullOrWhiteSpace(itemId))
                return;
            var display = _inventory.GetItemDisplayName(itemId);
            _pendingTransfer = new PendingTransferDecision
            {
                npcId = npcId,
                itemId = itemId,
                qty = qty,
                npcToHero = npcToHero,
                contextNote = string.IsNullOrWhiteSpace(payload?.InteractionOutcome) ? "unspecified" : payload.InteractionOutcome
            };

            if (npcToHero)
            {
                ui.AppendSystemLine($"Pending offer: {display} x{qty}. Accept or decline.");
                ui.AppendNpcLine($"I can give you {display} x{qty}. Do you accept?");
                ui?.ShowTransferDecision(
                    $"Accept {display} x{qty} from NPC?",
                    () => ResolvePendingTransferFromUi(true),
                    () => ResolvePendingTransferFromUi(false));
                return;
            }

            ui.AppendSystemLine($"NPC requests: {display} x{qty} from you. Accept or decline.");
            ui.AppendNpcLine($"Will you give me {display} x{qty}?");
            ui?.ShowTransferDecision(
                $"Give {display} x{qty} to NPC?",
                () => ResolvePendingTransferFromUi(true),
                () => ResolvePendingTransferFromUi(false));
        }

        bool TryHandlePendingTransferDecision(string line)
        {
            if (_pendingTransfer == null || !TransferDecisionParser.TryParsePlayerLine(line, out var accepted))
                return false;

            ResolvePendingTransferFromUi(accepted);
            return true;
        }

        public void ResolvePendingTransferFromUi(bool accept)
        {
            if (_pendingTransfer == null)
                return;
            var p = _pendingTransfer;
            _pendingTransfer = null;
            ui?.HideTransferDecision();
            if (!accept)
            {
                ui?.AppendSystemLine("You declined the pending transfer.");
                ui?.AppendNpcLine("Understood. I will hold onto it for now.");
                return;
            }

            if (_inventory == null)
            {
                ui.AppendSystemLine("Transfer failed: inventory service unavailable.");
                return;
            }

            var ok = p.npcToHero
                ? _inventory.TryTransfer(p.npcId, InventoryService.HeroActorId, p.itemId, p.qty)
                : _inventory.TryTransfer(InventoryService.HeroActorId, p.npcId, p.itemId, p.qty);
            if (p.npcToHero && !ok && _inventory.IsKnownItem(p.itemId))
            {
                // If the model committed to giving an item but inventories are out of sync, materialize it once.
                _inventory.AddItem(p.npcId, p.itemId, p.qty);
                ok = _inventory.TryTransfer(p.npcId, InventoryService.HeroActorId, p.itemId, p.qty);
                if (ok)
                    DialogueTelemetry.Log("NpcOfferMaterialized", $"npc={p.npcId}, item={p.itemId}, qty={p.qty}");
            }
            var display = _inventory.GetItemDisplayName(p.itemId);
            var msg = p.npcToHero
                ? (ok
                    ? $"Transfer accepted: NPC -> Hero ({display} x{p.qty})."
                    : $"Transfer failed: NPC could not provide {display} x{p.qty}.")
                : (ok
                    ? $"Transfer accepted: Hero -> NPC ({display} x{p.qty})."
                    : $"Transfer failed: you could not provide {display} x{p.qty}.");
            ui.AppendSystemLine(msg);
            ui.AppendNpcLine(p.npcToHero
                ? (ok ? $"Here you go: {display} x{p.qty}." : "I cannot hand that over right now.")
                : (ok ? $"Thank you for the {display}." : $"You do not have enough {display}."));
            if (ok && !p.npcToHero)
                _chickenTheftScenario.NotifyReparationIfNeeded(p.npcId);
            if (_activeNpc != null)
                UpdateInventoryDebugUi(_activeNpc.npcId);
            RefreshWorldInventoryVisuals();
        }

        bool EvaluateNpcWillingnessForTransfer(string npcId, string intent, string itemId, int qty, out string reason)
        {
            reason = "not enough trust in this context.";
            if (_inventory == null || string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(itemId))
                return false;

            if (_chickenTheftScenario.TryGetWillingnessForTransfer(npcId, intent, out reason))
                return true;

            var profile = _narrativeCanon?.npcProfiles?.Find(x =>
                x != null && !string.IsNullOrWhiteSpace(x.npcId) && string.Equals(x.npcId, npcId, StringComparison.OrdinalIgnoreCase));
            var score = 0.5f;
            if (profile?.socialTraits != null)
            {
                score += SocialLevelToDelta(profile.socialTraits, "helpfulness", +0.15f);
                score += SocialLevelToDelta(profile.socialTraits, "skepticism", -0.12f);
                score += SocialLevelToDelta(profile.socialTraits, "patience", +0.08f);
                score += SocialLevelToDelta(profile.socialTraits, "trickery", -0.08f);
            }

            score += DialogueTurnCommitLogic.InteractionOutcomeWillingnessDelta(_lastCommittedInteractionOutcome);

            var intentKey = (intent ?? string.Empty).Trim().ToLowerInvariant();
            if (intentKey == "take")
                score -= 0.14f;
            else if (intentKey == "trade")
                score -= 0.08f;
            else if (intentKey == "receive")
                score += 0.04f;

            if (profile?.goals != null && profile.goals.Count > 0)
            {
                var gtext = string.Join(" ", profile.goals).ToLowerInvariant();
                if (gtext.Contains("trade"))
                    score += 0.12f;
                if (gtext.Contains("protect") || gtext.Contains("guard") || gtext.Contains("keep"))
                    score -= 0.08f;
            }

            var accept = score >= 0.45f;
            reason = accept
                ? "this exchange fits my current stance."
                : "this exchange does not fit my current stance.";
            return accept;
        }

        static string NormalizeInteractionOutcome(string raw)
        {
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().ToLowerInvariant();
        }

        static float SocialLevelToDelta(Dictionary<string, string> traits, string key, float magnitude)
        {
            if (traits == null || !traits.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
                return 0f;
            var lvl = v.Trim().ToLowerInvariant();
            if (lvl == "high")
                return magnitude;
            if (lvl == "low")
                return -magnitude;
            return 0f;
        }

        public void OpenDebugConsoleFromShortcut()
        {
            if (ui == null || _dialogueOpen)
                return;
            _dialogueOpen = true;
            _activeNpc = null;
            _session = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            ui.Open("Debug Console");
            ui.AppendNpcLine("Debug console open. Try: /canon, /quest, /route, /smoke 5, /debug prompt");
            ui.AppendSystemLine("No active NPC selected.");
        }

        /// <summary>Hero/NPC item store; null until <see cref="ConfigureRuntime"/>.</summary>
        public InventoryService Inventory => _inventory;

        /// <summary>Social agreement lifecycle store; null until <see cref="ConfigureRuntime"/>.</summary>
        public AgreementService Agreements => _agreements;

        public string ChickenTheftIncidentVictimNpcId => _chickenTheftScenario.IncidentVictimNpcId;

        public void RegisterChickenTheftIncidentVictim(string npcId) => _chickenTheftScenario.RegisterIncidentVictim(npcId);

        public void ClearChickenTheftIncidentVictim() => _chickenTheftScenario.ClearIncidentVictim();

        /// <summary>
        /// While the hero carries a live chicken, hide it from LLM inventory text unless the speaking NPC is the
        /// registered theft victim (or no victim yet — then hide from everyone until a confrontation assigns one).
        /// </summary>
        public bool ShouldRedactLiveChickenInPromptForNpc(string speakingNpcId) =>
            _chickenTheftScenario.ShouldRedactLiveChickenInPromptForNpc(_inventory, speakingNpcId);

        public void NotifyHeroWorldInventoryChanged()
        {
            var npcId = _dialogueOpen && _activeNpc != null ? _activeNpc.npcId : string.Empty;
            UpdateInventoryDebugUi(npcId);
            RefreshWorldInventoryVisuals();
        }

        public bool TryConsumeLiveChickenForFood()
        {
            if (_inventory == null)
                return false;
            if (!_inventory.RemoveItem(InventoryService.HeroActorId, GameConstants.LiveChickenItemId, 1))
                return false;
            _chickenTheftScenario.ClearIncidentVictim();
            var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (go != null && go.TryGetComponent<HeroHunger>(out var hung))
                hung.AddFoodFractionOfMax(0.5f);
            NotifyHeroWorldInventoryChanged();
            return true;
        }

        public void ShowHudMessage(string text)
        {
            RunUi(() => ui?.ShowTransientHudMessage(text));
        }

        public bool TryDropHeroItemToWorld(string itemId)
        {
            if (_inventory == null || string.IsNullOrWhiteSpace(itemId))
                return false;
            if (!_inventory.HasAtLeast(InventoryService.HeroActorId, itemId, 1))
                return false;
            var hero = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (hero == null)
                return false;
            if (!_inventory.RemoveItem(InventoryService.HeroActorId, itemId, 1))
                return false;
            if (string.Equals(itemId.Trim(), GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
                _chickenTheftScenario.ClearIncidentVictim();
            var dropPos = hero.transform.position + hero.transform.forward * 0.65f + Vector3.up * 0.2f;
            WorldItemDropSpawner.SpawnDroppedItem(itemId.Trim(), dropPos, _contentLibrary);
            NotifyHeroWorldInventoryChanged();
            return true;
        }

        public bool TryBuildQuickActionState(out QuickActionState state)
        {
            state = null;
            if (_inventory == null)
                return false;
            var npcId = _dialogueOpen && _activeNpc != null ? _activeNpc.npcId : string.Empty;
            state = new QuickActionState
            {
                npcId = string.IsNullOrWhiteSpace(npcId) ? "(none)" : npcId,
                heroItems = _inventory.GetInventoryView(InventoryService.HeroActorId),
                npcItems = string.IsNullOrWhiteSpace(npcId)
                    ? new List<InventoryViewEntry>()
                    : _inventory.GetInventoryView(npcId)
            };
            return true;
        }

        public bool ExecuteQuickTakeFromNpc(string itemId)
        {
            if (!_dialogueOpen || _activeNpc == null || _inventory == null || string.IsNullOrWhiteSpace(itemId))
                return false;
            var npcId = _activeNpc.npcId;
            var display = _inventory.GetItemDisplayName(itemId);
            var accepted = EvaluateNpcWillingnessForTransfer(npcId, "take", itemId, 1, out var reason);
            var ok = accepted && _inventory.TryTransfer(npcId, InventoryService.HeroActorId, itemId, 1);
            if (!accepted)
            {
                ui?.AppendSystemLine($"NPC refused taking {display}. {reason}");
                ui?.AppendNpcLine($"No, I will not hand over {display}. {reason}");
                UpdateInventoryDebugUi(npcId);
                return false;
            }
            ui?.AppendSystemLine(ok ? $"Taken from NPC: {display} x1." : $"Take failed for {display}.");
            ui?.AppendNpcLine(ok ? $"You take {display}." : $"I cannot hand over {display} right now.");
            UpdateInventoryDebugUi(npcId);
            RefreshWorldInventoryVisuals();
            return ok;
        }

        public bool ExecuteQuickGiveToNpc(string itemId)
        {
            if (!_dialogueOpen || _activeNpc == null || _inventory == null || string.IsNullOrWhiteSpace(itemId))
                return false;
            var npcId = _activeNpc.npcId;
            var display = _inventory.GetItemDisplayName(itemId);
            var accepted = EvaluateNpcWillingnessForTransfer(npcId, "receive", itemId, 1, out var reason);
            var ok = accepted && _inventory.TryTransfer(InventoryService.HeroActorId, npcId, itemId, 1);
            if (!accepted)
            {
                ui?.AppendSystemLine($"NPC refused {display}. {reason}");
                ui?.AppendNpcLine($"I refuse {display}. {reason}");
                UpdateInventoryDebugUi(npcId);
                return false;
            }

            ui?.AppendSystemLine(ok ? $"Given to NPC: {display} x1. {reason}" : $"Give failed for {display}.");
            ui?.AppendNpcLine(ok ? $"I accept {display}. {reason}" : $"I cannot accept {display} right now.");
            if (ok)
                _chickenTheftScenario.NotifyReparationIfNeeded(npcId);
            UpdateInventoryDebugUi(npcId);
            RefreshWorldInventoryVisuals();
            return ok;
        }

        public bool ExecuteQuickTrade(string heroItemId, string npcItemId)
        {
            if (!_dialogueOpen || _activeNpc == null || _inventory == null
                || string.IsNullOrWhiteSpace(heroItemId) || string.IsNullOrWhiteSpace(npcItemId))
                return false;
            var npcId = _activeNpc.npcId;
            var heroDisplay = _inventory.GetItemDisplayName(heroItemId);
            var npcDisplay = _inventory.GetItemDisplayName(npcItemId);

            var acceptsReceive = EvaluateNpcWillingnessForTransfer(npcId, "receive", heroItemId, 1, out var receiveReason);
            var allowsGive = EvaluateNpcWillingnessForTransfer(npcId, "trade", npcItemId, 1, out var giveReason);
            if (!acceptsReceive || !allowsGive)
            {
                var reason = !acceptsReceive ? receiveReason : giveReason;
                ui?.AppendSystemLine($"Trade refused. {reason}");
                ui?.AppendNpcLine($"I refuse this trade. {reason}");
                UpdateInventoryDebugUi(npcId);
                return false;
            }

            var ok = _inventory.TryTrade(InventoryService.HeroActorId, npcId, heroItemId, 1, npcItemId, 1);
            ui?.AppendSystemLine(ok
                ? $"Trade complete: you gave {heroDisplay}, received {npcDisplay}."
                : $"Trade failed: missing item(s).");
            ui?.AppendNpcLine(ok
                ? $"Trade accepted. I take {heroDisplay} and give you {npcDisplay}."
                : "Trade cannot proceed right now.");
            if (ok)
                _chickenTheftScenario.NotifyReparationIfNeeded(npcId);
            UpdateInventoryDebugUi(npcId);
            RefreshWorldInventoryVisuals();
            return ok;
        }

        /// <summary>Called when narrative <see cref="NpcActionTypes.ReceiveObject"/> moves inventory from hero to NPC during dialogue.</summary>
        public void NotifyChickenTheftReparationFromNpcAction(string npcId) => _chickenTheftScenario.NotifyReparationIfNeeded(npcId);

        public Task<string> RequestChickenTheftShoutLineAsync(CancellationToken cancellationToken) =>
            _chickenTheftScenario.RequestShoutLineAsync(_ollamaClient, _ollamaSettings, cancellationToken);

        public async void GenerateHeroBookDiscoveryNarration(int bookCount, string itemId)
        {
            if (bookCount < 1 || bookCount > 3)
                return;
            var itemName = _inventory != null ? _inventory.GetItemDisplayName(itemId) : (itemId ?? "book");
            var worldSummary = _narrativeCanon != null
                ? $"{_narrativeCanon.summary} {_narrativeCanon.worldBackstory}".Trim()
                : "A dangerous island with monsters, toxic water, and a haunted castle.";
            var guidance = bookCount == 1
                ? "Write 3-4 short lines in first person. MUST explicitly say this book contains a protective spell and that it enables the hero to cast magic now."
                : "Write 3-4 short lines in first person. MUST explicitly say this book contains a stronger spell than before.";
            var system =
                "You write concise in-game narration for a hero discovering magical books. Keep immersive tone and slight poetic flavor. Be explicit about what spell the book contains and how powerful it is. No JSON.";
            var user =
                $"World context: {worldSummary}\n" +
                $"Book found: {itemName}\n" +
                $"Book level now: {bookCount}\n" +
                $"Required explicit statements:\n" +
                $"- Say this book contains a spell.\n" +
                $"- Mention current spell power tier based on book count.\n" +
                $"- If book 2 or 3 found, say this spell is stronger than previous.\n" +
                $"{guidance}";
            var fallback = bookCount == 1
                ? "This book contains a protective spell.\nI can now cast magic to shield myself from harm.\nThe words still sing like poetry, but the spell is real and ready."
                : (bookCount == 2
                    ? "This book contains a stronger spell than the first.\nMy magic is now at power level 2, and its reach is greater.\nThe verses are beautiful, but the force inside them is unmistakable."
                    : "This book contains my strongest spell yet.\nMy magic is now at power level 3, stronger than all before.\nThe lines read like a hymn, but they carry the weight of a storm.");
            var output = fallback;
            try
            {
                if (_ollamaClient != null)
                {
                    var messages = new List<OllamaMessageDto>
                    {
                        new OllamaMessageDto("system", system),
                        new OllamaMessageDto("user", user)
                    };
                    var result = await _ollamaClient.ChatAsync(messages, _ollamaSettings != null ? _ollamaSettings.model : null, CancellationToken.None);
                    if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.AssistantContent))
                        output = result.AssistantContent.Trim();
                    else
                        DialogueTelemetry.Log("BookNarrationFail", result.Error ?? "empty assistant content");
                }
            }
            catch (Exception ex)
            {
                DialogueTelemetry.Log("BookNarrationFail", ex.Message);
            }

            var overlay = UnityEngine.Object.FindFirstObjectByType<GameplayIntroOverlay>();
            if (overlay != null)
                overlay.ShowBookNarration(output);
            else
                AppendGuideNavigationSystem(output);
        }

        void QueueDialogueSpeech(string text, string speakerRole)
        {
            if (_ollamaSettings == null || !_ollamaSettings.useTtsSynthesis || _pythonClient == null || _dialogueSpeechPlayer == null)
                return;
            var cleaned = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                return;
            if (_ollamaSettings.ttsMaxCharacters > 0 && cleaned.Length > _ollamaSettings.ttsMaxCharacters)
                cleaned = cleaned.Substring(0, _ollamaSettings.ttsMaxCharacters);

            _ttsCts?.Cancel();
            _ttsCts?.Dispose();
            _ttsCts = new CancellationTokenSource();
            _ = RequestDialogueSpeechAsync(cleaned, speakerRole, _ttsCts.Token);
        }

        void EmitNpcLineWithSpeech(string uiText, string speechText = null)
        {
            if (ui == null || string.IsNullOrWhiteSpace(uiText))
                return;
            ui.AppendNpcLine(uiText);
            QueueDialogueSpeech(string.IsNullOrWhiteSpace(speechText) ? uiText : speechText, "npc");
        }

        async Task RequestDialogueSpeechAsync(string text, string speakerRole, CancellationToken cancellationToken)
        {
            try
            {
                var req = new PythonTtsSynthesizeRequestDto
                {
                    requestId = Guid.NewGuid().ToString("N"),
                    text = text,
                    voiceId = ResolveDialogueVoiceId(speakerRole),
                    language = string.IsNullOrWhiteSpace(_ollamaSettings.ttsLanguage) ? "english" : _ollamaSettings.ttsLanguage.Trim(),
                    quantize = true,
                    speakerRole = string.Equals(speakerRole, "hero", StringComparison.OrdinalIgnoreCase) ? "hero" : "npc"
                };
                var envelope = await _pythonClient.TtsSynthesizeAsync(req, cancellationToken);
                if (cancellationToken.IsCancellationRequested || envelope == null || !envelope.ok || envelope.tts == null)
                    return;
                if (string.IsNullOrWhiteSpace(envelope.tts.audioBase64))
                    return;

                byte[] wavBytes;
                try
                {
                    wavBytes = Convert.FromBase64String(envelope.tts.audioBase64);
                }
                catch (FormatException)
                {
                    return;
                }

                RunUi(() =>
                {
                    if (_dialogueSpeechPlayer == null)
                        return;
                    var clipName = $"dialogue_tts_{req.speakerRole}";
                    _dialogueSpeechPlayer.TryPlayWavBytes(wavBytes, clipName);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueTTS] {ex.GetType().Name}: {ex.Message}");
            }
        }

        string ResolveDialogueVoiceId(string speakerRole)
        {
            if (string.Equals(speakerRole, "hero", StringComparison.OrdinalIgnoreCase))
                return ResolveOrAssignHeroVoiceId();
            if (_activeNpc != null && !string.IsNullOrWhiteSpace(_activeNpc.npcId))
                return EnsureNpcVoiceAssigned(_activeNpc.npcId);
            return ResolveOrAssignHeroVoiceId();
        }

        void CancelDialogueSpeech()
        {
            _ttsCts?.Cancel();
            _ttsCts?.Dispose();
            _ttsCts = null;
            _dialogueSpeechPlayer?.Stop();
        }

        void WarmupTtsInBackground()
        {
            if (_ollamaSettings == null || !_ollamaSettings.useTtsSynthesis || _pythonClient == null)
                return;
            _ = RequestTtsWarmupAsync();
        }

        async Task RequestTtsWarmupAsync()
        {
            try
            {
                var warmup = new PythonTtsSynthesizeRequestDto
                {
                    requestId = Guid.NewGuid().ToString("N"),
                    text = "Warmup.",
                    voiceId = ResolveOrAssignHeroVoiceId(),
                    language = string.IsNullOrWhiteSpace(_ollamaSettings.ttsLanguage) ? "english" : _ollamaSettings.ttsLanguage.Trim(),
                    quantize = true,
                    speakerRole = "system"
                };
                var envelope = await _pythonClient.TtsSynthesizeAsync(warmup, CancellationToken.None);
                if (envelope == null || !envelope.ok)
                    Debug.LogWarning("[DialogueTTS] Warmup request failed.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DialogueTTS] Warmup exception: {ex.Message}");
            }
        }

        string ResolveOrAssignHeroVoiceId()
        {
            if (!string.IsNullOrWhiteSpace(_heroVoiceId))
                return _heroVoiceId;
            var configured = _ollamaSettings == null || string.IsNullOrWhiteSpace(_ollamaSettings.ttsDefaultVoiceId)
                ? "alba"
                : _ollamaSettings.ttsDefaultVoiceId.Trim();
            if (_voiceAssignments == null)
            {
                _heroVoiceId = configured;
                return _heroVoiceId;
            }

            _heroVoiceId = _voiceAssignments.GetOrAssignHeroVoice(configured, TtsEnglishVoices);
            return string.IsNullOrWhiteSpace(_heroVoiceId) ? "alba" : _heroVoiceId;
        }

        string EnsureNpcVoiceAssigned(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return ResolveOrAssignHeroVoiceId();
            if (_voiceAssignments == null)
                return ResolveOrAssignHeroVoiceId();
            var hero = ResolveOrAssignHeroVoiceId();
            return _voiceAssignments.GetOrAssignNpcVoice(npcId.Trim(), hero, TtsEnglishVoices);
        }

        void RefreshWorldInventoryVisuals()
        {
            var bootstrap = UnityEngine.Object.FindFirstObjectByType<RuntimeLevelBootstrap>();
            bootstrap?.RefreshNpcInventoryItemVisuals();
        }
    }
}
