using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpg.Dialogue;
using Rpg.GameState;
using UnityEngine;

namespace Rpg.Npc
{
    [DefaultExecutionOrder(18)]
    public sealed class VillageAgentSimulation : MonoBehaviour
    {
        [SerializeField] bool simulationEnabled = true;
        [SerializeField] bool enableSidecarDeliberation = true;
        [SerializeField] float deliberationCadenceSeconds = 2f;
        [SerializeField] float villagerRefreshSeconds = 4f;
        [SerializeField] int maxGossipInteractionsPerTick = 2;
        [SerializeField] int maxPlanStepsPerDeliberation = 5;
        [SerializeField] bool logDeliberationTelemetry = true;
        [SerializeField] bool enableAmbientNpcChatter = true;
        [SerializeField] float ambientChatterCooldownSeconds = 5f;
        [SerializeField] float ambientChatterPairCooldownSeconds = 16f;
        [SerializeField, Range(0, 100)] int ambientChatterRichVariantPercent = 12;

        WorldStateService _worldState;
        OllamaSettings _settings;
        IVillageDeliberationTransport _transport;
        VillageDeliberationScheduler _scheduler;
        VillageOpinionService _opinionService;
        AmbientNpcChatterService _ambientChatterService;
        VillageAutonomyTelemetry _telemetry;
        readonly Dictionary<string, VillagerRuntimeState> _stateByNpcId =
            new Dictionary<string, VillagerRuntimeState>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _participantCache = new List<string>();
        readonly List<string> _removeScratch = new List<string>();

        Task<VillagerDeliberationEnvelope> _inFlight;
        string _inFlightNpcId;
        string _inFlightReason;
        float _nextVillagerRefreshAt;
        bool _initialized;

        public IReadOnlyDictionary<string, VillagerRuntimeState> States => _stateByNpcId;
        public VillageOpinionService OpinionService => _opinionService;
        public VillageAutonomyTelemetrySnapshot TelemetrySnapshot =>
            _telemetry != null ? _telemetry.Snapshot() : default;

        void Awake()
        {
            EnsureInitialized();
        }

        void Update()
        {
            TickSimulation(Time.time);
        }

        public void ConfigureRuntime(WorldStateService worldState, OllamaSettings settings)
        {
            _worldState = worldState;
            _settings = settings;
            _transport = new SidecarVillageDeliberationTransport(settings);
            _initialized = false;
            EnsureInitialized();
        }

        public void RequestRedeliberation(string npcId, string reason = "event")
        {
            EnsureInitialized();
            _scheduler?.RequestImmediate(npcId, reason);
        }

        public void RequestGlobalRedeliberation(string reason = "event")
        {
            EnsureInitialized();
            for (var i = 0; i < _participantCache.Count; i++)
                _scheduler?.RequestImmediate(_participantCache[i], reason);
        }

        public bool TryDebugApplyHeroImpact(
            string npcId,
            float opinionDelta,
            float leadershipDelta,
            float pietyDelta,
            float wealthDelta,
            float helpfulnessDelta)
        {
            EnsureDebugStateReady();
            if (_opinionService == null || string.IsNullOrWhiteSpace(npcId))
                return false;
            if (!_stateByNpcId.ContainsKey(npcId))
                return false;

            _opinionService.ApplyHeroImpact(npcId, opinionDelta, leadershipDelta, pietyDelta, wealthDelta, helpfulnessDelta);
            return true;
        }

        public bool TryDebugQueueGossip(string npcA, string npcB)
        {
            EnsureDebugStateReady();
            if (_opinionService == null || string.IsNullOrWhiteSpace(npcA) || string.IsNullOrWhiteSpace(npcB))
                return false;
            if (string.Equals(npcA, npcB, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!_stateByNpcId.ContainsKey(npcA) || !_stateByNpcId.ContainsKey(npcB))
                return false;

            _opinionService.QueueInteraction(npcA, npcB);
            return true;
        }

        public bool TryDebugForceChat(string speakerNpcId, string targetNpcId, float chatDurationSeconds = 2f)
        {
            EnsureDebugStateReady();
            if (string.IsNullOrWhiteSpace(speakerNpcId) || string.IsNullOrWhiteSpace(targetNpcId))
                return false;
            if (string.Equals(speakerNpcId, targetNpcId, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!_stateByNpcId.TryGetValue(speakerNpcId, out var speaker) || speaker == null || speaker.Controller == null)
                return false;
            if (!_stateByNpcId.TryGetValue(targetNpcId, out var target) || target == null || target.Controller == null)
                return false;

            var duration = Mathf.Max(0.25f, chatDurationSeconds);
            speaker.Controller.SetPlan(new[]
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.ChatWithNpc,
                    TargetNpcId = targetNpcId,
                    DurationSeconds = duration
                }
            });
            target.Controller.SetPlan(new[]
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.ChatWithNpc,
                    TargetNpcId = speakerNpcId,
                    DurationSeconds = duration
                }
            });

            _opinionService?.QueueInteraction(speakerNpcId, targetNpcId);
            return true;
        }

        public List<VillageGroupAskRecord> DebugSnapshotGroupAsks()
        {
            EnsureDebugStateReady();
            return _opinionService != null ? _opinionService.SnapshotGroupAsks() : new List<VillageGroupAskRecord>();
        }

        public bool TryDebugRespondToGroupAsk(string askId, bool accept, string responderNpcId, out List<string> signals)
        {
            EnsureDebugStateReady();
            signals = new List<string>();
            if (_opinionService == null)
                return false;
            return _opinionService.TryRespondToGroupAsk(askId, accept, responderNpcId, out signals);
        }

        public void ConfigureForTests(IVillageDeliberationTransport transport, WorldStateService worldState = null)
        {
            _transport = transport;
            _worldState = worldState;
            _initialized = false;
            EnsureInitialized();
        }

        public void TickSimulation(float nowTime)
        {
            EnsureInitialized();
            if (!simulationEnabled)
                return;

            RefreshVillagerRegistry(nowTime);
            QueueInteractionGossip(nowTime);
            _opinionService.ProcessGossip(maxGossipInteractionsPerTick);
            TryFinalizeInFlightResult();
            if (_inFlight != null)
                return;

            if (_scheduler == null || !_scheduler.TryAcquire(nowTime, out var npcId, out var reason))
                return;
            if (!_stateByNpcId.TryGetValue(npcId, out var state) || state == null)
                return;

            _telemetry?.RecordDeliberationCall();
            _inFlightNpcId = npcId;
            _inFlightReason = reason;
            _inFlight = DeliberateVillagerAsync(state, reason, CancellationToken.None);
        }

        void EnsureInitialized()
        {
            if (_initialized)
                return;

            _scheduler = new VillageDeliberationScheduler(deliberationCadenceSeconds);
            if (_opinionService == null)
                _opinionService = new VillageOpinionService();
            if (_ambientChatterService == null)
                _ambientChatterService = new AmbientNpcChatterService();
            if (_telemetry == null)
                _telemetry = new VillageAutonomyTelemetry();
            if (_transport == null)
                _transport = new SidecarVillageDeliberationTransport(_settings);
            _nextVillagerRefreshAt = -1f;
            _initialized = true;
        }

        void EnsureDebugStateReady()
        {
            EnsureInitialized();
            RefreshVillagerRegistry(Time.time);
        }

        void RefreshVillagerRegistry(float nowTime)
        {
            if (nowTime < _nextVillagerRefreshAt)
                return;
            _nextVillagerRefreshAt = nowTime + Mathf.Max(0.25f, villagerRefreshSeconds);

            _participantCache.Clear();
            foreach (var binding in FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!TryResolveVillagerEntry(binding, out var npcId, out var controller))
                    continue;

                if (!_stateByNpcId.TryGetValue(npcId, out var state) || state == null)
                {
                    state = new VillagerRuntimeState(npcId);
                    _stateByNpcId[npcId] = state;
                }

                state.Binding = binding;
                state.Controller = controller;
                state.ActiveGoals = ResolveSeedGoals(binding, state.ActiveGoals);
                _participantCache.Add(npcId);
            }

            _removeScratch.Clear();
            foreach (var kvp in _stateByNpcId)
            {
                if (!_participantCache.Contains(kvp.Key))
                    _removeScratch.Add(kvp.Key);
            }

            for (var i = 0; i < _removeScratch.Count; i++)
                _stateByNpcId.Remove(_removeScratch[i]);

            _scheduler.CadenceSeconds = deliberationCadenceSeconds;
            _scheduler.SetParticipants(_participantCache);
            _opinionService.SetParticipants(_participantCache);
            _ambientChatterService.SetParticipants(_participantCache);
        }

        void QueueInteractionGossip(float nowTime)
        {
            if (_opinionService == null || _stateByNpcId.Count == 0)
                return;

            foreach (var kvp in _stateByNpcId)
            {
                var state = kvp.Value;
                if (state == null || state.Controller == null)
                    continue;
                if (!string.Equals(state.Controller.CurrentPrimitiveType, NpcPrimitiveTypes.ChatWithNpc, StringComparison.Ordinal))
                    continue;
                if (string.IsNullOrWhiteSpace(state.Controller.CurrentTargetNpcId))
                    continue;

                var targetNpcId = state.Controller.CurrentTargetNpcId;
                _opinionService.QueueInteraction(state.NpcId, targetNpcId);
                TryEmitAmbientNpcChatter(nowTime, state, targetNpcId);
            }
        }

        void TryEmitAmbientNpcChatter(float nowTime, VillagerRuntimeState speakerState, string targetNpcId)
        {
            if (!enableAmbientNpcChatter || _ambientChatterService == null || speakerState == null)
                return;
            if (!_stateByNpcId.TryGetValue(targetNpcId, out var targetState) || targetState == null)
                return;

            var speakerBinding = speakerState.Binding;
            var targetBinding = targetState.Binding;
            var speakerOpinionTowardHero = _opinionService.GetOpinionTowardHero(speakerState.NpcId);
            if (!_ambientChatterService.TryBuildEvent(
                    nowTime,
                    speakerState.NpcId,
                    targetState.NpcId,
                    ResolveDisplayName(speakerState.NpcId, speakerBinding),
                    ResolveDisplayName(targetState.NpcId, targetBinding),
                    speakerBinding != null && speakerBinding.Persona != null ? speakerBinding.Persona.personality : string.Empty,
                    targetBinding != null && targetBinding.Persona != null ? targetBinding.Persona.personality : string.Empty,
                    speakerBinding != null && speakerBinding.Persona != null ? speakerBinding.Persona.goals : null,
                    speakerOpinionTowardHero,
                    ambientChatterCooldownSeconds,
                    ambientChatterPairCooldownSeconds,
                    ambientChatterRichVariantPercent,
                    out var chatter))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(chatter.Text))
                return;

            var manager = DialogueManager.Instance;
            if (manager != null)
                manager.ShowHudMessage(chatter.Text);
            DialogueTelemetry.Log("VillageAmbientChatter", chatter.Text);
        }

        static string ResolveDisplayName(string npcId, NpcDialogueBinding binding)
        {
            if (binding != null && binding.Definition != null && !string.IsNullOrWhiteSpace(binding.Definition.displayName))
                return binding.Definition.displayName.Trim();
            return string.IsNullOrWhiteSpace(npcId) ? "villager" : npcId.Trim();
        }

        void TryFinalizeInFlightResult()
        {
            if (_inFlight == null || !_inFlight.IsCompleted)
                return;

            var npcId = _inFlightNpcId;
            var reason = _inFlightReason;
            try
            {
                var envelope = _inFlight.Status == TaskStatus.RanToCompletion
                    ? _inFlight.Result
                    : VillagerDeliberationEnvelope.Failure("Deliberation task did not complete successfully.", true);
                ApplyDeliberationResult(npcId, reason, envelope);
            }
            catch (Exception ex)
            {
                ApplyDeliberationResult(npcId, reason, VillagerDeliberationEnvelope.Failure(ex.Message, true));
            }
            finally
            {
                _inFlight = null;
                _inFlightNpcId = string.Empty;
                _inFlightReason = string.Empty;
            }
        }

        async Task<VillagerDeliberationEnvelope> DeliberateVillagerAsync(
            VillagerRuntimeState state,
            string reason,
            CancellationToken cancellationToken)
        {
            if (state == null || state.Binding == null || state.Controller == null)
                return VillagerDeliberationEnvelope.Failure("Missing villager components.", true);

            if (!enableSidecarDeliberation || _transport == null)
                return BuildFallbackEnvelope(state, "sidecar_disabled");

            var request = BuildRequest(state, reason);
            var envelope = await _transport.DeliberateAsync(request, cancellationToken);
            if (envelope == null)
                return BuildFallbackEnvelope(state, "empty_sidecar_envelope");

            if (!envelope.Success)
                return BuildFallbackEnvelope(state, envelope.Error ?? "sidecar_failed");

            if (envelope.PlanSteps == null || envelope.PlanSteps.Count == 0)
                return BuildFallbackEnvelope(state, "empty_sidecar_plan");

            envelope.PlanSteps = ClampAndCloneSteps(envelope.PlanSteps);
            envelope.Goals = NormalizeGoals(envelope.Goals, state.ActiveGoals);
            envelope.UsedFallback = false;
            return envelope;
        }

        PythonNpcDeliberationRequestDto BuildRequest(VillagerRuntimeState state, string reason)
        {
            var binding = state.Binding;
            var def = binding != null ? binding.Definition : null;
            var persona = binding != null ? binding.Persona : null;
            var world = _worldState != null ? _worldState.GetSnapshot() : new WorldStateSnapshot(2847);
            var type = "normal";
            var npcId = state.NpcId;
            var displayName = def != null ? def.displayName : npcId;

            var request = new PythonNpcDeliberationRequestDto
            {
                requestId = Guid.NewGuid().ToString("N"),
                model = _settings != null ? _settings.model : string.Empty,
                apiToken = _settings != null ? _settings.providerApiToken : string.Empty,
                providerBaseUrl = _settings != null ? _settings.providerBaseUrl : string.Empty,
                reason = reason,
                npc = new PythonNpcDeliberationNpcDto
                {
                    npcId = npcId,
                    displayName = displayName ?? string.Empty,
                    npcType = type,
                    personality = persona != null ? persona.personality ?? string.Empty : string.Empty,
                    socialTraits = persona != null && persona.socialTraits != null
                        ? new Dictionary<string, string>(persona.socialTraits)
                        : new Dictionary<string, string>(),
                    capabilities = persona != null && persona.capabilities != null
                        ? new List<string>(persona.capabilities)
                        : new List<string>()
                },
                world = new PythonNpcDeliberationWorldDto
                {
                    worldFacts = world.ToFactsBlock(),
                    currentYear = world.CurrentYear,
                    currentDay = world.HasClockData ? world.Clock.CurrentDay : 0,
                    currentHour24 = world.HasClockData ? world.Clock.Hour24 : 0f,
                    surroundingsBlock = NpcSurroundingsScanner.BuildPromptBlock(npcId)
                },
                currentGoals = state.ActiveGoals != null ? new List<string>(state.ActiveGoals) : new List<string>(),
                currentPlan = ConvertPlanToDto(state.ActivePlan),
                agreements = _opinionService != null
                    ? _opinionService.BuildDeliberationContext(npcId)
                    : new List<string>()
            };
            return request;
        }

        void ApplyDeliberationResult(string npcId, string reason, VillagerDeliberationEnvelope envelope)
        {
            if (!_stateByNpcId.TryGetValue(npcId, out var state) || state == null)
                return;

            var plan = envelope != null && envelope.PlanSteps != null
                ? ClampAndCloneSteps(envelope.PlanSteps)
                : new List<NpcPrimitiveStep>();
            var goals = NormalizeGoals(envelope != null ? envelope.Goals : null, state.ActiveGoals);

            state.ActiveGoals = goals;
            state.LastDeliberationReason = reason ?? string.Empty;
            state.LastDeliberationSource = envelope != null && envelope.UsedFallback ? "fallback" : "sidecar";
            state.LastError = envelope != null ? envelope.Error ?? string.Empty : "unknown";
            state.LastDeliberatedAtUtc = DateTime.UtcNow.ToString("o");
            if (envelope != null && envelope.UsedFallback)
                _telemetry?.RecordFallback();
            _telemetry?.RecordPlanCompletion(envelope != null && envelope.Success);

            if (plan.Count > 0 && state.Controller != null)
            {
                state.ActivePlan = plan;
                state.Controller.SetPlan(plan);
            }
            else if (state.ActivePlan == null)
            {
                state.ActivePlan = new List<NpcPrimitiveStep>();
            }

            if (logDeliberationTelemetry)
            {
                var detail = $"npc={npcId}, reason={state.LastDeliberationReason}, source={state.LastDeliberationSource}, " +
                             $"steps={plan.Count}, error={state.LastError}";
                DialogueTelemetry.Log("VillageDeliberation", detail);
            }
        }

        VillagerDeliberationEnvelope BuildFallbackEnvelope(VillagerRuntimeState state, string reason)
        {
            var goals = NormalizeGoals(null, state != null ? state.ActiveGoals : null);
            var plan = state != null && state.ActivePlan != null
                ? ClampAndCloneSteps(state.ActivePlan)
                : new List<NpcPrimitiveStep>();
            return new VillagerDeliberationEnvelope
            {
                Success = false,
                UsedFallback = true,
                Error = reason ?? "fallback",
                Goals = goals,
                PlanSteps = plan
            };
        }

        static bool TryResolveVillagerEntry(NpcDialogueBinding binding, out string npcId, out NpcAgentController controller)
        {
            npcId = string.Empty;
            controller = null;
            if (binding == null || binding.Definition == null)
                return false;
            if (!binding.TryGetComponent(out controller))
                return false;

            var id = binding.Definition.npcId;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            id = id.Trim();
            if (!id.StartsWith("villager_", StringComparison.OrdinalIgnoreCase))
                return false;

            npcId = id;
            return true;
        }

        static List<string> ResolveSeedGoals(NpcDialogueBinding binding, List<string> existingGoals)
        {
            if (existingGoals != null && existingGoals.Count > 0)
                return existingGoals;
            if (binding == null || binding.Persona == null || binding.Persona.goals == null)
                return new List<string>();
            return NormalizeGoals(binding.Persona.goals, null);
        }

        List<PythonNpcPlanStepDto> ConvertPlanToDto(List<NpcPrimitiveStep> plan)
        {
            var list = new List<PythonNpcPlanStepDto>();
            if (plan == null)
                return list;

            var max = Mathf.Max(1, maxPlanStepsPerDeliberation);
            for (var i = 0; i < plan.Count && i < max; i++)
            {
                var s = plan[i];
                if (s == null)
                    continue;

                list.Add(new PythonNpcPlanStepDto
                {
                    primitiveType = s.PrimitiveType ?? string.Empty,
                    worldLocation = new PythonVector3Dto
                    {
                        x = s.WorldLocation.x,
                        y = s.WorldLocation.y,
                        z = s.WorldLocation.z
                    },
                    targetNpcId = s.TargetNpcId ?? string.Empty,
                    durationSeconds = s.DurationSeconds,
                    stopDistanceMeters = s.StopDistanceMeters,
                    workId = s.WorkId ?? string.Empty
                });
            }

            return list;
        }

        List<NpcPrimitiveStep> ClampAndCloneSteps(List<NpcPrimitiveStep> source)
        {
            var list = new List<NpcPrimitiveStep>();
            if (source == null)
                return list;

            var max = Mathf.Max(1, maxPlanStepsPerDeliberation);
            for (var i = 0; i < source.Count && i < max; i++)
            {
                var step = source[i];
                if (step == null)
                    continue;
                list.Add(step.Clone());
            }

            return list;
        }

        static List<string> NormalizeGoals(List<string> goals, List<string> fallback)
        {
            var normalized = new List<string>();
            AddUniqueGoals(normalized, goals);
            AddUniqueGoals(normalized, fallback);
            return normalized;
        }

        static void AddUniqueGoals(List<string> target, List<string> source)
        {
            if (target == null || source == null)
                return;
            for (var i = 0; i < source.Count; i++)
            {
                var g = source[i];
                if (string.IsNullOrWhiteSpace(g))
                    continue;
                var clean = g.Trim();
                if (!target.Contains(clean))
                    target.Add(clean);
            }
        }

        public sealed class VillagerRuntimeState
        {
            public VillagerRuntimeState(string npcId)
            {
                NpcId = string.IsNullOrWhiteSpace(npcId) ? string.Empty : npcId.Trim();
                ActiveGoals = new List<string>();
                ActivePlan = new List<NpcPrimitiveStep>();
                LastDeliberationReason = string.Empty;
                LastDeliberationSource = string.Empty;
                LastError = string.Empty;
                LastDeliberatedAtUtc = string.Empty;
            }

            public string NpcId { get; }
            public NpcDialogueBinding Binding { get; set; }
            public NpcAgentController Controller { get; set; }
            public List<string> ActiveGoals { get; set; }
            public List<NpcPrimitiveStep> ActivePlan { get; set; }
            public string LastDeliberationReason { get; set; }
            public string LastDeliberationSource { get; set; }
            public string LastError { get; set; }
            public string LastDeliberatedAtUtc { get; set; }
        }

        public interface IVillageDeliberationTransport
        {
            Task<VillagerDeliberationEnvelope> DeliberateAsync(PythonNpcDeliberationRequestDto request, CancellationToken token);
        }

        sealed class SidecarVillageDeliberationTransport : IVillageDeliberationTransport
        {
            readonly PythonPolicyClient _client;

            public SidecarVillageDeliberationTransport(OllamaSettings settings)
            {
                _client = settings != null ? new PythonPolicyClient(settings) : null;
            }

            public async Task<VillagerDeliberationEnvelope> DeliberateAsync(PythonNpcDeliberationRequestDto request, CancellationToken token)
            {
                if (_client == null)
                    return VillagerDeliberationEnvelope.Failure("missing_policy_client", true);

                var envelope = await _client.NpcDeliberationAsync(request, token);
                if (envelope == null)
                    return VillagerDeliberationEnvelope.Failure("empty_envelope", true);
                if (!envelope.ok || envelope.deliberation == null)
                    return VillagerDeliberationEnvelope.Failure(envelope.error != null ? envelope.error.message : "deliberation_failed", true);

                var plan = ConvertStepsFromDto(envelope.deliberation.planSteps);
                return new VillagerDeliberationEnvelope
                {
                    Success = plan.Count > 0,
                    UsedFallback = false,
                    Error = plan.Count > 0 ? string.Empty : "sidecar_plan_empty",
                    Goals = envelope.deliberation.goals != null
                        ? new List<string>(envelope.deliberation.goals)
                        : new List<string>(),
                    PlanSteps = plan
                };
            }

            static List<NpcPrimitiveStep> ConvertStepsFromDto(List<PythonNpcPlanStepDto> steps)
            {
                var result = new List<NpcPrimitiveStep>();
                if (steps == null)
                    return result;

                for (var i = 0; i < steps.Count; i++)
                {
                    var dto = steps[i];
                    if (dto == null)
                        continue;

                    var primitiveType = string.IsNullOrWhiteSpace(dto.primitiveType)
                        ? dto.stepType
                        : dto.primitiveType;
                    if (string.IsNullOrWhiteSpace(primitiveType))
                        continue;

                    Vector3 worldLocation;
                    if (dto.worldLocation != null)
                    {
                        worldLocation = new Vector3(dto.worldLocation.x, dto.worldLocation.y, dto.worldLocation.z);
                    }
                    else
                    {
                        worldLocation = new Vector3(dto.worldX, dto.worldY, dto.worldZ);
                    }

                    result.Add(new NpcPrimitiveStep
                    {
                        PrimitiveType = primitiveType,
                        WorldLocation = worldLocation,
                        TargetNpcId = dto.targetNpcId ?? string.Empty,
                        DurationSeconds = dto.durationSeconds,
                        StopDistanceMeters = dto.stopDistanceMeters,
                        WorkId = dto.workId ?? string.Empty
                    });
                }

                return result;
            }
        }

        public sealed class VillagerDeliberationEnvelope
        {
            public bool Success;
            public bool UsedFallback;
            public string Error;
            public List<string> Goals = new List<string>();
            public List<NpcPrimitiveStep> PlanSteps = new List<NpcPrimitiveStep>();

            public static VillagerDeliberationEnvelope Failure(string error, bool usedFallback)
            {
                return new VillagerDeliberationEnvelope
                {
                    Success = false,
                    UsedFallback = usedFallback,
                    Error = error ?? string.Empty
                };
            }
        }
    }
}
