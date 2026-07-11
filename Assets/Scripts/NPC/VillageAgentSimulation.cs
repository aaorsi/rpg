using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Rpg.Core;
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
        [SerializeField, Range(10f, 30f)] float npcReferenceRefreshSeconds = 10f;
        [SerializeField] bool enableInteractionRunner = true;
        [SerializeField] float interactionStepDurationSeconds = 3f;
        [SerializeField] float interactionApproachMaxSeconds = 45f;
        [SerializeField] float interactionEngageRangeMeters = 2.5f;
        [SerializeField] float interactionDialogueTimeoutSeconds = 25f;
        [SerializeField] float interactionPairCooldownSeconds = 12f;
        [SerializeField] int maxActiveInteractions = 16;
        [SerializeField] int maxLoopIterationsPerInteraction = 3;
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
        InteractionDefinitionRegistry _interactionRegistry;
        InteractionDefinitionsDoc _interactionDefinitions;
        NpcInteractionMemoryService _interactionMemory;
        LocationBindingRegistry _locationRegistry;
        readonly Dictionary<string, VillagerRuntimeState> _stateByNpcId =
            new Dictionary<string, VillagerRuntimeState>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _participantCache = new List<string>();
        readonly List<string> _removeScratch = new List<string>();
        readonly Dictionary<string, Vector3> _npcPositionById = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _staticLocationIds = new List<string>();
        readonly List<string> _knownWorkIds = new List<string>();
        readonly List<InteractionRuntimeInstance> _activeInteractions = new List<InteractionRuntimeInstance>();
        readonly Dictionary<string, float> _interactionPairCooldownByKey =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _interactionLockByNpcId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Task<VillagerDeliberationEnvelope> _inFlight;
        string _inFlightNpcId;
        string _inFlightReason;
        float _nextVillagerRefreshAt;
        float _nextNpcReferenceRefreshAt;
        float _nextInteractionPositionRefreshAt;
        bool _initialized;
        bool _walletsSeeded;
        bool _autoCompleteInteractionDialogue;
        VillageWorldReferenceSnapshot _worldReferenceSnapshot;
        string _lastDialoguePhase = string.Empty;

        public IReadOnlyDictionary<string, VillagerRuntimeState> States => _stateByNpcId;
        public VillageOpinionService OpinionService => _opinionService;
        public InteractionDefinitionsDoc InteractionDefinitions => _interactionDefinitions;
        public VillageWorldReferenceSnapshot WorldReferenceSnapshot => _worldReferenceSnapshot;
        public IReadOnlyList<InteractionRuntimeInstance> ActiveInteractions => _activeInteractions;
        public bool HasRunningInteractions
        {
            get
            {
                for (var i = 0; i < _activeInteractions.Count; i++)
                {
                    var item = _activeInteractions[i];
                    if (item != null && item.status == InteractionRuntimeStatus.Running)
                        return true;
                }

                return false;
            }
        }
        public int GetActorCoinBalance(string actorId)
        {
            var inventory = ResolveInventory();
            return inventory != null ? inventory.GetCoinBalance(actorId) : 0;
        }

        public IReadOnlyList<string> BuildVillagerDebugLines(string npcId)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(npcId) || IsHeroParticipant(npcId))
                return lines;
            if (!TryGetRuntimeState(npcId, out var state) || state == null)
            {
                lines.Add("villager: unknown");
                return lines;
            }

            var binding = state.Binding;
            lines.Add(VillageAutonomyDebugFormatter.BuildGoalAndPlanLine(state));
            if (binding?.Persona != null)
                lines.Add(VillageAutonomyDebugFormatter.BuildPersonaSummary(binding.Persona));
            if (_opinionService != null)
                lines.Add(VillageAutonomyDebugFormatter.BuildOpinionSnapshotLine(_opinionService.GetSummary(npcId)));
            return lines;
        }

        public string PreviewNextInteractionStage(string interactionId, string actorNpcId, string targetNpcId)
        {
            var definition = FindInteractionDefinition(interactionId);
            if (definition == null)
                return "(unknown interaction)";
            var probe = CreateInteractionInstance(definition, actorNpcId, targetNpcId, Time.time);
            return FormatInteractionNextStageForDebug(probe);
        }

        public IReadOnlyList<string> SnapshotOpenGroupAskIds()
        {
            var ids = new List<string>();
            if (_opinionService == null)
                return ids;
            var asks = _opinionService.SnapshotGroupAsks();
            for (var i = 0; i < asks.Count; i++)
            {
                var ask = asks[i];
                if (ask == null || string.IsNullOrWhiteSpace(ask.askId))
                    continue;
                if (string.Equals(ask.state, "offered", StringComparison.OrdinalIgnoreCase))
                    ids.Add(ask.askId.Trim());
            }
            return ids;
        }

        public IReadOnlyList<string> BuildInteractionDebugLines(InteractionRuntimeInstance instance)
        {
            return VillageAutonomyDebugFormatter.BuildInteractionDebugLines(
                instance,
                ResolveDisplayNameFromState,
                GetActorCoinBalance);
        }

        InventoryService ResolveInventory() => DialogueManager.Instance != null ? DialogueManager.Instance.Inventory : null;

        AgreementService ResolveAgreements() => DialogueManager.Instance != null ? DialogueManager.Instance.Agreements : null;

        public VillageAutonomyTelemetrySnapshot TelemetrySnapshot =>
            _telemetry != null ? _telemetry.Snapshot() : default;

        public readonly struct DebugNpcEntry
        {
            public DebugNpcEntry(string npcId, string displayName, bool isSidekick, bool isHero = false)
            {
                NpcId = npcId ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                IsSidekick = isSidekick;
                IsHero = isHero;
            }

            public string NpcId { get; }
            public string DisplayName { get; }
            public bool IsSidekick { get; }
            public bool IsHero { get; }
        }

        public void BuildDebugNpcEntryList(List<DebugNpcEntry> target)
        {
            if (target == null)
                return;
            target.Clear();
            target.Add(new DebugNpcEntry(InventoryService.HeroActorId, "Traveler (Hero)", false, true));
            foreach (var kvp in _stateByNpcId)
            {
                var state = kvp.Value;
                if (state == null || string.IsNullOrWhiteSpace(state.NpcId))
                    continue;
                var npcId = state.NpcId.Trim();
                var displayName = ResolveDisplayName(npcId, state.Binding);
                var isSidekick = state.Binding != null && SidekickCompanion.FindForNpcBindingRoot(state.Binding.gameObject) != null;
                target.Add(new DebugNpcEntry(npcId, displayName, isSidekick));
            }

            target.Sort((a, b) =>
            {
                var byId = string.Compare(a.NpcId, b.NpcId, StringComparison.OrdinalIgnoreCase);
                return byId != 0 ? byId : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
        }

        public sealed class HeroInteractionJoinContext
        {
            public string InteractionInstanceId;
            public string InteractionId;
            public string InteractionDisplayName;
            public string PrimaryNpcId;
            public string SecondaryNpcId;
            public List<string> ParticipantNpcIds = new List<string>();
        }

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

        public bool TryApplyHeroImpactForDebug(
            string observerNpcId,
            float opinionDelta,
            float leadershipDelta,
            float pietyDelta,
            float wealthDelta,
            float helpfulnessDelta,
            out string error)
        {
            EnsureInitialized();
            if (_opinionService == null)
            {
                error = "opinion_service_unavailable";
                return false;
            }

            if (!TryGetRuntimeState(observerNpcId, out var state))
            {
                error = "unknown_villager";
                return false;
            }

            _opinionService.ApplyHeroImpact(
                state.NpcId,
                opinionDelta,
                leadershipDelta,
                pietyDelta,
                wealthDelta,
                helpfulnessDelta);
            _scheduler?.RequestImmediate(state.NpcId, "debug_hero_impact");
            error = string.Empty;
            return true;
        }

        public bool TryQueueGossipForDebug(string npcA, string npcB, out string error)
        {
            EnsureInitialized();
            if (_opinionService == null)
            {
                error = "opinion_service_unavailable";
                return false;
            }

            if (!TryGetRuntimeState(npcA, out var left) || !TryGetRuntimeState(npcB, out var right))
            {
                error = "unknown_villager";
                return false;
            }

            if (string.Equals(left.NpcId, right.NpcId, StringComparison.OrdinalIgnoreCase))
            {
                error = "same_villager";
                return false;
            }

            _opinionService.QueueInteraction(left.NpcId, right.NpcId);
            _scheduler?.RequestImmediate(left.NpcId, "debug_gossip");
            _scheduler?.RequestImmediate(right.NpcId, "debug_gossip");
            error = string.Empty;
            return true;
        }

        public int ProcessGossipForDebug(int maxInteractions = 1)
        {
            EnsureInitialized();
            if (_opinionService == null)
                return 0;
            return _opinionService.ProcessGossip(Mathf.Max(0, maxInteractions));
        }

        public bool TryForceChatPlanForDebug(string speakerNpcId, string targetNpcId, float durationSeconds, out string error)
        {
            EnsureInitialized();
            if (!TryGetRuntimeState(speakerNpcId, out var speaker) || speaker.Controller == null)
            {
                error = "unknown_speaker";
                return false;
            }

            if (!TryGetRuntimeState(targetNpcId, out var target))
            {
                error = "unknown_target";
                return false;
            }

            var worldLocation = target.Binding != null
                ? target.Binding.transform.position
                : speaker.Controller.transform.position;
            var plan = new List<NpcPrimitiveStep>
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.ChatWithNpc,
                    TargetNpcId = target.NpcId,
                    WorldLocation = worldLocation,
                    DurationSeconds = Mathf.Max(0.2f, durationSeconds),
                    StopDistanceMeters = 1.6f
                }
            };

            speaker.ActivePlan = ClampAndCloneSteps(plan);
            speaker.Controller.SetPlan(plan);
            speaker.LastDeliberationReason = "debug_force_chat";
            speaker.LastDeliberationSource = "debug";
            speaker.LastError = string.Empty;
            speaker.LastDeliberatedAtUtc = DateTime.UtcNow.ToString("o");
            _opinionService?.QueueInteraction(speaker.NpcId, target.NpcId);
            _scheduler?.RequestImmediate(speaker.NpcId, "debug_force_chat");
            error = string.Empty;
            return true;
        }

        public bool TryRespondToGroupAskForDebug(
            string askId,
            bool accept,
            string responderNpcId,
            out List<string> milestoneSignals,
            out string error)
        {
            EnsureInitialized();
            milestoneSignals = new List<string>();
            if (_opinionService == null)
            {
                error = "opinion_service_unavailable";
                return false;
            }

            if (string.IsNullOrWhiteSpace(responderNpcId) || !TryGetRuntimeState(responderNpcId, out var responder))
            {
                error = "unknown_responder";
                return false;
            }

            if (!_opinionService.TryRespondToGroupAsk(askId, accept, responder.NpcId, out milestoneSignals))
            {
                error = "ask_not_offered";
                return false;
            }

            RequestGlobalRedeliberation("debug_group_ask_response");
            error = string.Empty;
            return true;
        }

        public bool TryStartInteractionForDebug(string interactionId, string actorNpcId, string targetNpcId, out string error)
        {
            EnsureInitialized();
            error = string.Empty;
            if (!enableInteractionRunner)
            {
                error = "interaction_runner_disabled";
                return false;
            }

            if (string.IsNullOrWhiteSpace(interactionId))
            {
                error = "missing_interaction_id";
                return false;
            }

            if (!IsValidInteractionParticipant(actorNpcId))
            {
                error = "unknown_actor";
                return false;
            }

            if (!IsValidInteractionParticipant(targetNpcId))
            {
                error = "unknown_target";
                return false;
            }

            if (string.Equals(actorNpcId, targetNpcId, StringComparison.OrdinalIgnoreCase))
            {
                error = "actor_equals_target";
                return false;
            }

            var definition = FindInteractionDefinition(interactionId);
            if (definition == null)
            {
                error = "unknown_interaction";
                return false;
            }

            var pairKey = BuildInteractionPairKey(actorNpcId, targetNpcId);
            if (HasActiveInteractionForPair(pairKey))
            {
                error = "pair_already_active";
                return false;
            }

            var now = Time.time;
            var instance = CreateInteractionInstance(definition, actorNpcId, targetNpcId, now);
            _activeInteractions.Add(instance);
            _interactionPairCooldownByKey[pairKey] = now + Mathf.Max(0f, interactionPairCooldownSeconds);
            _interactionMemory?.RecordInteractionStarted(instance, definition, ResolveDisplayNameFromState);
            BeginInteraction(instance);
            return true;
        }

        public bool TryStartGroupInteractionForDebug(
            string interactionId,
            string convenerNpcId,
            IReadOnlyList<string> audienceNpcIds,
            out string error)
        {
            EnsureInitialized();
            error = string.Empty;
            if (!enableInteractionRunner)
            {
                error = "interaction_runner_disabled";
                return false;
            }

            if (string.IsNullOrWhiteSpace(interactionId))
            {
                error = "missing_interaction_id";
                return false;
            }

            if (!IsValidInteractionParticipant(convenerNpcId))
            {
                error = "unknown_convener";
                return false;
            }

            var audience = new List<string>();
            if (audienceNpcIds != null)
            {
                for (var i = 0; i < audienceNpcIds.Count; i++)
                {
                    var id = audienceNpcIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var trimmed = id.Trim();
                    if (string.Equals(trimmed, convenerNpcId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!IsValidInteractionParticipant(trimmed))
                        continue;
                    if (!audience.Contains(trimmed))
                        audience.Add(trimmed);
                }
            }

            if (audience.Count == 0)
            {
                error = "missing_audience";
                return false;
            }

            var definition = FindInteractionDefinition(interactionId);
            if (definition == null)
            {
                error = "unknown_interaction";
                return false;
            }

            var primaryAudience = audience[0];
            var pairKey = BuildInteractionPairKey(convenerNpcId, primaryAudience);
            if (HasActiveInteractionForPair(pairKey))
            {
                error = "pair_already_active";
                return false;
            }

            var now = Time.time;
            var instance = CreateInteractionInstance(definition, convenerNpcId, primaryAudience, now);
            for (var i = 1; i < audience.Count; i++)
                instance.extraParticipantNpcIds.Add(audience[i]);
            instance.heroJoinEnabled = IsHeroJoinInteraction(definition);
            _activeInteractions.Add(instance);
            _interactionPairCooldownByKey[pairKey] = now + Mathf.Max(0f, interactionPairCooldownSeconds);
            _interactionMemory?.RecordInteractionStarted(instance, definition, ResolveDisplayNameFromState);
            BeginInteraction(instance);
            return true;
        }

        public IReadOnlyList<string> GetInteractionParticipantNpcIds(string interactionInstanceId)
        {
            var interaction = FindRuntimeInteraction(interactionInstanceId);
            if (interaction == null)
                return Array.Empty<string>();
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(interaction.actorNpcId) && seen.Add(interaction.actorNpcId.Trim()))
                ids.Add(interaction.actorNpcId.Trim());
            if (!string.IsNullOrWhiteSpace(interaction.targetNpcId) && seen.Add(interaction.targetNpcId.Trim()))
                ids.Add(interaction.targetNpcId.Trim());
            if (interaction.extraParticipantNpcIds != null)
            {
                for (var i = 0; i < interaction.extraParticipantNpcIds.Count; i++)
                {
                    var id = interaction.extraParticipantNpcIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var trimmed = id.Trim();
                    if (seen.Add(trimmed))
                        ids.Add(trimmed);
                }
            }

            return ids;
        }

        public string FormatInteractionParticipantsForDebug(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return "(none)";
            var parts = new List<string>();
            AppendParticipantLabel(parts, instance.actorNpcId, "initiator");
            AppendParticipantLabel(parts, instance.targetNpcId, "target");
            if (instance.extraParticipantNpcIds != null)
            {
                for (var i = 0; i < instance.extraParticipantNpcIds.Count; i++)
                    AppendParticipantLabel(parts, instance.extraParticipantNpcIds[i], "extra");
            }

            return parts.Count == 0 ? "(none)" : string.Join(", ", parts);
        }

        public string FormatInteractionActionForDebug(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return "(none)";
            if (string.IsNullOrWhiteSpace(instance.currentActionType))
                return $"(waiting) phase={instance.phase} step={instance.stepIndex + 1}";
            var actor = ResolveDisplayNameFromState(instance.currentActionActorId);
            var target = string.IsNullOrWhiteSpace(instance.currentActionTargetId)
                ? string.Empty
                : ResolveDisplayNameFromState(instance.currentActionTargetId);
            if (string.IsNullOrWhiteSpace(target))
                return $"{instance.phase} / step {instance.stepIndex + 1}: {instance.currentActionType} ({actor})";
            return $"{instance.phase} / step {instance.stepIndex + 1}: {instance.currentActionType} ({actor} -> {target})";
        }

        public string FormatInteractionTypeForDebug(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return "(unknown)";
            if (!string.IsNullOrWhiteSpace(instance.interactionDisplayName))
                return instance.interactionDisplayName.Trim();
            return string.IsNullOrWhiteSpace(instance.interactionId) ? "(unknown)" : instance.interactionId.Trim();
        }

        public string FormatInteractionNextStageForDebug(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return "(none)";
            if (!TryPeekNextInteractionStep(instance, out var phase, out var stepIndex, out var step))
                return instance.status == InteractionRuntimeStatus.Running ? "(interaction ending)" : "(finished)";
            var actor = ResolveInteractionRoleNpcId(instance, step?.actorRole, preferActor: true);
            var target = ResolveInteractionRoleNpcId(instance, step?.targetRole, preferActor: false);
            if (string.IsNullOrWhiteSpace(target))
            {
                target = string.Equals(actor, instance.actorNpcId, StringComparison.OrdinalIgnoreCase)
                    ? instance.targetNpcId
                    : instance.actorNpcId;
            }

            var action = step != null && !string.IsNullOrWhiteSpace(step.actionType) ? step.actionType.Trim() : "(unknown)";
            return $"{phase} / step {stepIndex + 1}: {action} ({ResolveDisplayNameFromState(actor)} -> {ResolveDisplayNameFromState(target)})";
        }

        bool TryPeekNextInteractionStep(
            InteractionRuntimeInstance instance,
            out string phase,
            out int stepIndex,
            out InteractionActionStep step)
        {
            phase = string.Empty;
            stepIndex = -1;
            step = null;
            if (instance == null || instance.status != InteractionRuntimeStatus.Running)
                return false;
            var definition = FindInteractionDefinition(instance.interactionId);
            if (definition == null || definition.phases == null)
                return false;

            if (TryGetPhaseSteps(definition, instance.phase, out var phaseSteps)
                && phaseSteps != null
                && instance.stepIndex < phaseSteps.Count)
            {
                phase = instance.phase;
                stepIndex = instance.stepIndex;
                step = phaseSteps[stepIndex];
                return step != null;
            }

            var probePhase = instance.phase;
            var probeStepIndex = instance.stepIndex;
            var probeLoop = instance.loopIteration;
            var probe = new InteractionRuntimeInstance
            {
                interactionId = instance.interactionId,
                actorNpcId = instance.actorNpcId,
                targetNpcId = instance.targetNpcId,
                phase = probePhase,
                stepIndex = probeStepIndex,
                loopIteration = probeLoop,
                extraParticipantNpcIds = instance.extraParticipantNpcIds
            };
            if (!TryAdvanceToNextPhase(definition, probe, Time.time))
                return false;
            if (!TryGetPhaseSteps(definition, probe.phase, out var nextPhaseSteps)
                || nextPhaseSteps == null
                || nextPhaseSteps.Count == 0)
                return false;

            phase = probe.phase;
            stepIndex = 0;
            step = nextPhaseSteps[0];
            return step != null;
        }

        static string ResolveInteractionStepTopic(InteractionActionStep step, InteractionRuntimeInstance instance)
        {
            if (step?.parameters != null && step.parameters.TryGetValue("topic", out var topic) && !string.IsNullOrWhiteSpace(topic))
                return topic.Trim();
            if (!string.IsNullOrWhiteSpace(instance?.interactionDisplayName))
                return instance.interactionDisplayName.Trim();
            return instance?.interactionId ?? "village interaction";
        }

        void AppendParticipantLabel(List<string> parts, string npcId, string role)
        {
            if (parts == null || string.IsNullOrWhiteSpace(npcId))
                return;
            var trimmed = npcId.Trim();
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i].StartsWith(trimmed + " ", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            var label = IsHeroParticipant(trimmed)
                ? $"{trimmed} (hero)"
                : $"{trimmed} ({ResolveDisplayNameFromState(trimmed)})";
            parts.Add($"{label} [{role}]");
        }

        void BeginInteraction(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return;
            LockNpcsForInteraction(instance);
            KickoffInitiatorApproach(instance);
        }

        static bool InstanceInvolvesHero(InteractionRuntimeInstance instance) =>
            IsHeroParticipant(instance?.actorNpcId) || IsHeroParticipant(instance?.targetNpcId);

        void KickoffInitiatorApproach(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return;
            _nextNpcReferenceRefreshAt = -1f;
            RefreshNpcReferenceSnapshot(Time.time);
            DispatchInitiatorApproach(instance);
        }

        void DispatchInitiatorApproach(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return;
            var initiator = instance.actorNpcId;
            var target = instance.targetNpcId;
            if (string.IsNullOrWhiteSpace(initiator) || string.IsNullOrWhiteSpace(target))
                return;

            if (IsHeroParticipant(initiator))
            {
                if (TryResolveHeroTransform(out var hero))
                    TryDispatchMoveToHero(target, hero.position);
                return;
            }

            if (IsHeroParticipant(target) && TryResolveHeroTransform(out var heroTarget))
            {
                TryDispatchMoveToHero(initiator, heroTarget.position);
                return;
            }

            TryDispatchMoveToNpc(initiator, target);
        }

        void TryDispatchMoveToHero(string npcId, Vector3 heroPosition)
        {
            if (IsHeroParticipant(npcId) || string.IsNullOrWhiteSpace(npcId))
                return;
            if (!TryGetRuntimeState(npcId, out var state) || state == null || state.Controller == null)
                return;
            AssignNpcMovePlan(state, heroPosition, interactionApproachMaxSeconds, 1.6f);
        }

        void TryDispatchMoveToNpc(string moverNpcId, string targetNpcId)
        {
            if (IsHeroParticipant(moverNpcId) || string.IsNullOrWhiteSpace(moverNpcId) || string.IsNullOrWhiteSpace(targetNpcId))
                return;
            if (!TryGetRuntimeState(moverNpcId, out var moverState) || moverState == null || moverState.Controller == null)
                return;
            if (IsHeroParticipant(targetNpcId) && TryResolveHeroTransform(out var hero))
            {
                AssignNpcMovePlan(moverState, hero.position, interactionApproachMaxSeconds, 1.6f);
                return;
            }

            var plan = new List<NpcPrimitiveStep>
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.GotoNpc,
                    TargetNpcId = targetNpcId,
                    DurationSeconds = Mathf.Max(0.2f, interactionApproachMaxSeconds),
                    StopDistanceMeters = 1.6f
                }
            };
            moverState.ActivePlan = plan;
            moverState.Controller.SetPlan(plan);
        }

        void LockNpcsForInteraction(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return;
            LockNpcForInteraction(instance.actorNpcId, instance.instanceId);
            LockNpcForInteraction(instance.targetNpcId, instance.instanceId);
        }

        void LockNpcForInteraction(string npcId, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(instanceId) || IsHeroParticipant(npcId))
                return;
            _interactionLockByNpcId[npcId.Trim()] = instanceId.Trim();
        }

        void UnlockNpcsForInteraction(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return;
            UnlockNpcForInteraction(instance.actorNpcId, instance.instanceId);
            UnlockNpcForInteraction(instance.targetNpcId, instance.instanceId);
        }

        void UnlockNpcForInteraction(string npcId, string instanceId)
        {
            if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(instanceId))
                return;
            var trimmed = npcId.Trim();
            if (_interactionLockByNpcId.TryGetValue(trimmed, out var lockedInstanceId)
                && string.Equals(lockedInstanceId, instanceId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _interactionLockByNpcId.Remove(trimmed);
            }
        }

        bool IsNpcLockedByActiveInteraction(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return false;
            return _interactionLockByNpcId.ContainsKey(npcId.Trim());
        }

        bool AreInteractionActorsInRange(string actorNpcId, string targetNpcId)
        {
            if (!TryGetNpcPosition(actorNpcId, out var actorPos) || !TryGetNpcPosition(targetNpcId, out var targetPos))
                return false;
            var delta = new Vector2(actorPos.x - targetPos.x, actorPos.z - targetPos.z);
            return delta.magnitude <= Mathf.Max(0.5f, interactionEngageRangeMeters);
        }

        void MaintainInteractionApproaches(float nowTime)
        {
            for (var i = 0; i < _activeInteractions.Count; i++)
            {
                var instance = _activeInteractions[i];
                if (instance == null || instance.status != InteractionRuntimeStatus.Running || instance.pausedByHero)
                    continue;
                if (AreInteractionActorsInRange(instance.actorNpcId, instance.targetNpcId))
                    continue;
                if (!InteractionNeedsActorProximity(instance))
                    continue;
                DispatchInitiatorApproach(instance);
            }
        }

        bool InteractionNeedsActorProximity(InteractionRuntimeInstance instance)
        {
            if (instance == null)
                return false;
            var definition = FindInteractionDefinition(instance.interactionId);
            if (definition == null || definition.phases == null)
                return true;
            if (!TryGetPhaseSteps(definition, instance.phase, out var phaseSteps)
                || phaseSteps == null
                || instance.stepIndex < 0
                || instance.stepIndex >= phaseSteps.Count)
            {
                return true;
            }

            var step = phaseSteps[instance.stepIndex];
            if (step == null || string.IsNullOrWhiteSpace(step.actionType))
                return true;
            var actionType = step.actionType.Trim().ToLowerInvariant();
            return actionType == InteractionActionTypes.MoveToNpc
                || actionType == InteractionActionTypes.MoveToHero
                || actionType == InteractionActionTypes.MoveToLocation
                || actionType == InteractionActionTypes.EngageDialogue;
        }

        void RefreshInteractionApproachPositions(float nowTime)
        {
            if (nowTime < _nextInteractionPositionRefreshAt)
                return;
            _nextInteractionPositionRefreshAt = nowTime + Mathf.Clamp(npcReferenceRefreshSeconds, 10f, 30f);
            RefreshNpcReferenceSnapshot(nowTime);
            if (!TryResolveHeroTransform(out var hero))
                return;

            var heroPos = hero.position;
            for (var i = 0; i < _activeInteractions.Count; i++)
            {
                var instance = _activeInteractions[i];
                if (instance == null || instance.status != InteractionRuntimeStatus.Running || !InstanceInvolvesHero(instance))
                    continue;
                DispatchInitiatorApproach(instance);
                RefreshActiveHeroMoveTargets(instance, heroPos);
            }
        }

        void RefreshActiveHeroMoveTargets(InteractionRuntimeInstance instance, Vector3 heroPosition)
        {
            if (instance == null)
                return;
            void refresh(string npcId)
            {
                if (IsHeroParticipant(npcId) || string.IsNullOrWhiteSpace(npcId))
                    return;
                if (!TryGetRuntimeState(npcId, out var state) || state?.Controller == null)
                    return;
                state.Controller.RefreshActiveMoveTarget(heroPosition);
            }

            refresh(instance.actorNpcId);
            refresh(instance.targetNpcId);
            if (instance.extraParticipantNpcIds == null)
                return;
            for (var i = 0; i < instance.extraParticipantNpcIds.Count; i++)
                refresh(instance.extraParticipantNpcIds[i]);
        }

        List<string> BuildJoinParticipantList(InteractionRuntimeInstance interaction)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (interaction == null)
                return ids;
            if (!string.IsNullOrWhiteSpace(interaction.actorNpcId) && seen.Add(interaction.actorNpcId.Trim()))
                ids.Add(interaction.actorNpcId.Trim());
            if (!string.IsNullOrWhiteSpace(interaction.targetNpcId) && seen.Add(interaction.targetNpcId.Trim()))
                ids.Add(interaction.targetNpcId.Trim());
            if (interaction.extraParticipantNpcIds != null)
            {
                for (var i = 0; i < interaction.extraParticipantNpcIds.Count; i++)
                {
                    var id = interaction.extraParticipantNpcIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    var trimmed = id.Trim();
                    if (seen.Add(trimmed))
                        ids.Add(trimmed);
                }
            }

            return ids;
        }

        public bool TryAcquireHeroJoinContext(Vector3 heroWorldPosition, float joinRadiusMeters, out HeroInteractionJoinContext context)
        {
            EnsureInitialized();
            context = null;
            var best = default(InteractionRuntimeInstance);
            var bestDistanceSq = Mathf.Max(0.1f, joinRadiusMeters) * Mathf.Max(0.1f, joinRadiusMeters);
            for (var i = 0; i < _activeInteractions.Count; i++)
            {
                var interaction = _activeInteractions[i];
                if (interaction == null || interaction.status != InteractionRuntimeStatus.Running || interaction.pausedByHero)
                    continue;
                if (!interaction.heroJoinEnabled
                    && !IsHeroParticipant(interaction.actorNpcId)
                    && !IsHeroParticipant(interaction.targetNpcId))
                    continue;
                if (!TryGetNpcPosition(interaction.actorNpcId, out var actorPos))
                    continue;
                if (!TryGetNpcPosition(interaction.targetNpcId, out var targetPos))
                    continue;
                var actorSq = (actorPos - heroWorldPosition).sqrMagnitude;
                var targetSq = (targetPos - heroWorldPosition).sqrMagnitude;
                var closestSq = Mathf.Min(actorSq, targetSq);
                if (closestSq > bestDistanceSq)
                    continue;
                best = interaction;
                bestDistanceSq = closestSq;
            }

            if (best == null)
                return false;

            context = new HeroInteractionJoinContext
            {
                InteractionInstanceId = best.instanceId,
                InteractionId = best.interactionId,
                InteractionDisplayName = best.interactionDisplayName,
                PrimaryNpcId = best.actorNpcId,
                SecondaryNpcId = best.targetNpcId,
                ParticipantNpcIds = BuildJoinParticipantList(best)
            };
            var midpoint = Vector3.zero;
            if (TryGetNpcPosition(best.actorNpcId, out var joinActorPos) && TryGetNpcPosition(best.targetNpcId, out var joinTargetPos))
                midpoint = (joinActorPos + joinTargetPos) * 0.5f;
            var maxBystanderSq = Mathf.Max(0.5f, joinRadiusMeters) * Mathf.Max(0.5f, joinRadiusMeters);
            var participantCap = best.extraParticipantNpcIds != null && best.extraParticipantNpcIds.Count > 0 ? 6 : 4;
            for (var i = 0; i < _participantCache.Count && context.ParticipantNpcIds.Count < participantCap; i++)
            {
                var npcId = _participantCache[i];
                if (string.IsNullOrWhiteSpace(npcId)
                    || context.ParticipantNpcIds.Contains(npcId)
                    || !TryGetNpcPosition(npcId, out var pos))
                {
                    continue;
                }

                if ((pos - midpoint).sqrMagnitude > maxBystanderSq)
                    continue;
                context.ParticipantNpcIds.Add(npcId);
            }
            return true;
        }

        public bool TryMarkInteractionJoinedByHero(string interactionInstanceId, out string error)
        {
            EnsureInitialized();
            error = string.Empty;
            var interaction = FindRuntimeInteraction(interactionInstanceId);
            if (interaction == null)
            {
                error = "interaction_not_found";
                return false;
            }

            if (interaction.status != InteractionRuntimeStatus.Running)
            {
                error = "interaction_not_running";
                return false;
            }

            interaction.pausedByHero = true;
            interaction.statusReason = "paused_for_hero_dialogue";
            interaction.updatedAtTime = Time.time;
            return true;
        }

        public void NotifyHeroInteractionDialogueClosed(string interactionInstanceId)
        {
            EnsureInitialized();
            var interaction = FindRuntimeInteraction(interactionInstanceId);
            if (interaction == null)
                return;
            if (interaction.status != InteractionRuntimeStatus.Running)
                return;

            interaction.pausedByHero = false;
            interaction.statusReason = "resumed_after_hero_dialogue";
            interaction.nextStepAtTime = Time.time + Mathf.Max(0.2f, interactionStepDurationSeconds);
            interaction.updatedAtTime = Time.time;
        }

        public void NotifyInteractionDialogueStepCompleted(string interactionInstanceId, bool succeeded)
        {
            EnsureInitialized();
            var interaction = FindRuntimeInteraction(interactionInstanceId);
            if (interaction == null || !interaction.awaitingDialogueStep)
                return;

            interaction.awaitingDialogueStep = false;
            if (!succeeded)
                RecordInteractionStepResult(interaction, new InteractionEffectResolver.StepResult(false, "dialogue line failed"));
            interaction.stepIndex++;
            interaction.nextStepAtTime = Time.time + Mathf.Max(0.2f, interactionStepDurationSeconds);
            interaction.updatedAtTime = Time.time;
        }

        public void ConfigureForTests(
            IVillageDeliberationTransport transport,
            WorldStateService worldState = null,
            string askStatePath = null)
        {
            _transport = transport;
            _worldState = worldState;
            var testAskPath = string.IsNullOrWhiteSpace(askStatePath)
                ? Path.Combine(Path.GetTempPath(), $"rpg_village_asks_test_{Guid.NewGuid():N}.json")
                : askStatePath;
            _opinionService = new VillageOpinionService(testAskPath);
            _autoCompleteInteractionDialogue = true;
            _initialized = false;
            EnsureInitialized();
        }

        public bool TryPromoteProposedInteractionForDebug(string interactionId, out string error)
        {
            EnsureInitialized();
            if (_interactionRegistry == null)
            {
                error = "registry_unavailable";
                return false;
            }

            if (!_interactionRegistry.TryPromoteProposedToActive(interactionId, out error))
                return false;

            _interactionDefinitions = _interactionRegistry.LoadEffective(out var issues);
            if (issues != null && issues.Count > 0)
            {
                error = string.Join(" | ", issues);
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void TickSimulation(float nowTime)
        {
            EnsureInitialized();
            if (!simulationEnabled)
                return;

            RefreshVillagerRegistry(nowTime);
            QueueInteractionGossip(nowTime);
            _opinionService.ProcessGossip(maxGossipInteractionsPerTick);
            TickInteractions(nowTime);
            TryFinalizeInFlightResult();
            if (_inFlight != null)
                return;

            if (_scheduler == null || !_scheduler.TryAcquire(nowTime, out var npcId, out var reason))
                return;
            if (IsNpcLockedByActiveInteraction(npcId))
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
            if (_interactionRegistry == null)
                _interactionRegistry = new InteractionDefinitionRegistry();
            if (_interactionMemory == null)
                _interactionMemory = new NpcInteractionMemoryService(new NpcMemoryRepository());
            if (_locationRegistry == null)
            {
                var library = new NarrativeContentLibrary();
                _locationRegistry = new LocationBindingRegistry(library.LoadLocationCatalog());
            }
            _interactionDefinitions = _interactionRegistry.LoadEffective(out var interactionIssues);
            if (interactionIssues != null && interactionIssues.Count > 0)
            {
                for (var i = 0; i < interactionIssues.Count; i++)
                    Debug.LogWarning($"[VillageAgentSimulation] Interaction definition issue: {interactionIssues[i]}");
            }
            _nextVillagerRefreshAt = -1f;
            _nextNpcReferenceRefreshAt = -1f;
            RefreshStaticWorldReferences();
            RefreshWorldReferenceSnapshot(Time.time);
            _initialized = true;
        }

        void RefreshWorldReferenceSnapshot(float nowTime)
        {
            var snapshot = new VillageWorldReferenceSnapshot { capturedAtTime = nowTime };
            for (var i = 0; i < _staticLocationIds.Count; i++)
            {
                var id = _staticLocationIds[i];
                if (!string.IsNullOrWhiteSpace(id))
                    snapshot.locationIds.Add(id);
            }

            for (var i = 0; i < _knownWorkIds.Count; i++)
            {
                var id = _knownWorkIds[i];
                if (!string.IsNullOrWhiteSpace(id))
                    snapshot.workIds.Add(id);
            }

            foreach (var kvp in _stateByNpcId)
            {
                var state = kvp.Value;
                if (state == null || string.IsNullOrWhiteSpace(state.NpcId))
                    continue;
                snapshot.npcIds.Add(state.NpcId);
                if (!TryGetNpcPosition(state.NpcId, out var pos))
                    pos = Vector3.zero;
                snapshot.npcs.Add(new VillageNpcReferenceEntry
                {
                    npcId = state.NpcId,
                    displayName = ResolveDisplayName(state.NpcId, state.Binding),
                    positionX = pos.x,
                    positionY = pos.y,
                    positionZ = pos.z
                });
            }

            _worldReferenceSnapshot = snapshot;
        }

        void RefreshVillagerRegistry(float nowTime)
        {
            if (nowTime < _nextVillagerRefreshAt)
                return;
            _nextVillagerRefreshAt = nowTime + Mathf.Max(0.25f, villagerRefreshSeconds);

            _participantCache.Clear();
            var seenNpcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!TryResolveInteractableNpcEntry(binding, out var npcId, out var controller))
                    continue;
                if (!seenNpcIds.Add(npcId))
                    continue;

                if (!_stateByNpcId.TryGetValue(npcId, out var state) || state == null)
                {
                    state = new VillagerRuntimeState(npcId);
                    _stateByNpcId[npcId] = state;
                }

                state.Binding = binding;
                state.Controller = controller;
                state.ActiveGoals = ResolveSeedGoals(binding, state.ActiveGoals);
                if (IsAutonomyParticipant(binding, npcId) && !_participantCache.Contains(npcId))
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
            EnsureNpcWalletsSeeded();
            UpdateLiveNpcPositions();
            RefreshNpcReferenceSnapshot(nowTime);
        }

        void EnsureNpcWalletsSeeded()
        {
            if (_walletsSeeded || _participantCache.Count == 0)
                return;
            var inventory = ResolveInventory();
            if (inventory == null)
                return;
            inventory.EnsureVillageNpcWallets(_participantCache);
            _walletsSeeded = true;
        }

        void UpdateLiveNpcPositions()
        {
            foreach (var kvp in _stateByNpcId)
            {
                var state = kvp.Value;
                if (state?.Binding != null)
                    _npcPositionById[state.NpcId] = state.Binding.transform.position;
            }
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

        void TickInteractions(float nowTime)
        {
            if (!enableInteractionRunner)
                return;

            RefreshInteractionApproachPositions(nowTime);
            MaintainInteractionApproaches(nowTime);
            TrySpawnInteractionsFromCurrentChats(nowTime);
            for (var i = _activeInteractions.Count - 1; i >= 0; i--)
            {
                var instance = _activeInteractions[i];
                if (instance == null)
                {
                    _activeInteractions.RemoveAt(i);
                    continue;
                }

                if (instance.status != InteractionRuntimeStatus.Running)
                {
                    if (nowTime - instance.updatedAtTime > 4f)
                        _activeInteractions.RemoveAt(i);
                    continue;
                }
                if (instance.pausedByHero)
                    continue;

                if (instance.expiresAtTime > 0f && nowTime >= instance.expiresAtTime)
                {
                    CompleteInteraction(instance, InteractionRuntimeStatus.Expired, "expired", nowTime);
                    continue;
                }

                if (nowTime < instance.nextStepAtTime)
                    continue;

                AdvanceInteractionStep(instance, nowTime);
            }
        }

        void TrySpawnInteractionsFromCurrentChats(float nowTime)
        {
            if (_activeInteractions.Count >= Mathf.Max(1, maxActiveInteractions))
                return;
            if (_interactionDefinitions == null || _interactionDefinitions.interactions == null || _interactionDefinitions.interactions.Count == 0)
                return;

            foreach (var kvp in _stateByNpcId)
            {
                if (_activeInteractions.Count >= Mathf.Max(1, maxActiveInteractions))
                    break;

                var state = kvp.Value;
                if (state == null || state.Controller == null)
                    continue;
                if (!string.Equals(state.Controller.CurrentPrimitiveType, NpcPrimitiveTypes.ChatWithNpc, StringComparison.Ordinal))
                    continue;
                var actorNpcId = state.NpcId;
                var targetNpcId = state.Controller.CurrentTargetNpcId;
                if (string.IsNullOrWhiteSpace(targetNpcId) || string.Equals(actorNpcId, targetNpcId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!_stateByNpcId.ContainsKey(targetNpcId))
                    continue;

                var pairKey = BuildInteractionPairKey(actorNpcId, targetNpcId);
                if (HasActiveInteractionForPair(pairKey))
                    continue;
                if (_interactionPairCooldownByKey.TryGetValue(pairKey, out var cooldownUntil) && nowTime < cooldownUntil)
                    continue;

                var definition = PickSpawnableInteractionDefinition();
                if (definition == null)
                    continue;

                var instance = CreateInteractionInstance(definition, actorNpcId, targetNpcId, nowTime);
                _activeInteractions.Add(instance);
                _interactionPairCooldownByKey[pairKey] = nowTime + Mathf.Max(0f, interactionPairCooldownSeconds);
                BeginInteraction(instance);
                DialogueTelemetry.Log(
                    "VillageInteractionStarted",
                    $"instance={instance.instanceId}, interaction={instance.interactionId}, actor={actorNpcId}, target={targetNpcId}");
            }
        }

        static string BuildInteractionPairKey(string npcA, string npcB)
        {
            var left = string.IsNullOrWhiteSpace(npcA) ? string.Empty : npcA.Trim().ToLowerInvariant();
            var right = string.IsNullOrWhiteSpace(npcB) ? string.Empty : npcB.Trim().ToLowerInvariant();
            return string.CompareOrdinal(left, right) <= 0 ? left + "|" + right : right + "|" + left;
        }

        bool TryGetNpcPosition(string npcId, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrWhiteSpace(npcId))
                return false;
            if (IsHeroParticipant(npcId))
            {
                if (TryResolveHeroTransform(out var hero))
                {
                    position = hero.position;
                    return true;
                }

                return false;
            }
            if (TryGetRuntimeState(npcId, out var state) && state != null && state.Binding != null)
            {
                position = state.Binding.transform.position;
                return true;
            }
            if (_npcPositionById.TryGetValue(npcId, out position))
                return true;
            return false;
        }

        static bool TryResolveHeroTransform(out Transform hero)
        {
            hero = null;
            var go = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (go == null)
                return false;
            hero = go.transform;
            return hero != null;
        }

        static void AssignNpcMovePlan(VillagerRuntimeState state, Vector3 worldLocation, float durationSeconds, float stopDistanceMeters)
        {
            if (state == null || state.Controller == null)
                return;
            var plan = new List<NpcPrimitiveStep>
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.GotoLocation,
                    WorldLocation = worldLocation,
                    DurationSeconds = Mathf.Max(0.2f, durationSeconds),
                    StopDistanceMeters = stopDistanceMeters
                }
            };
            state.ActivePlan = plan;
            state.Controller.SetPlan(plan);
        }

        static void AssignNpcChatPlan(
            VillagerRuntimeState state,
            string targetNpcId,
            Vector3 worldLocation,
            float durationSeconds,
            float stopDistanceMeters)
        {
            if (state == null || state.Controller == null)
                return;
            var plan = new List<NpcPrimitiveStep>
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.ChatWithNpc,
                    TargetNpcId = targetNpcId ?? string.Empty,
                    WorldLocation = worldLocation,
                    DurationSeconds = Mathf.Max(0.2f, durationSeconds),
                    StopDistanceMeters = stopDistanceMeters
                }
            };
            state.ActivePlan = plan;
            state.Controller.SetPlan(plan);
        }

        InteractionRuntimeInstance FindRuntimeInteraction(string interactionInstanceId)
        {
            if (string.IsNullOrWhiteSpace(interactionInstanceId))
                return null;
            for (var i = 0; i < _activeInteractions.Count; i++)
            {
                var interaction = _activeInteractions[i];
                if (interaction == null || string.IsNullOrWhiteSpace(interaction.instanceId))
                    continue;
                if (string.Equals(interaction.instanceId, interactionInstanceId, StringComparison.OrdinalIgnoreCase))
                    return interaction;
            }
            return null;
        }

        bool HasActiveInteractionForPair(string pairKey)
        {
            if (string.IsNullOrWhiteSpace(pairKey))
                return false;
            for (var i = 0; i < _activeInteractions.Count; i++)
            {
                var item = _activeInteractions[i];
                if (item == null || item.status != InteractionRuntimeStatus.Running)
                    continue;
                if (string.Equals(BuildInteractionPairKey(item.actorNpcId, item.targetNpcId), pairKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        InteractionDefinition PickSpawnableInteractionDefinition()
        {
            if (_interactionDefinitions == null || _interactionDefinitions.interactions == null)
                return null;

            var candidates = new List<InteractionDefinition>();
            var totalWeight = 0f;
            for (var i = 0; i < _interactionDefinitions.interactions.Count; i++)
            {
                var definition = _interactionDefinitions.interactions[i];
                if (definition == null)
                    continue;
                if (!string.Equals(definition.status, "active", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!definition.spawnFromChat)
                    continue;
                if (definition.phases == null)
                    continue;
                var hasAnySteps = (definition.phases.start != null && definition.phases.start.Count > 0)
                    || (definition.phases.loop != null && definition.phases.loop.Count > 0)
                    || (definition.phases.end != null && definition.phases.end.Count > 0);
                if (!hasAnySteps)
                    continue;

                candidates.Add(definition);
                totalWeight += Mathf.Max(0.01f, definition.selectionWeight <= 0f ? 1f : definition.selectionWeight);
            }

            if (candidates.Count == 0)
                return null;

            var random = UnityEngine.Random.value * totalWeight;
            for (var i = 0; i < candidates.Count; i++)
            {
                var definition = candidates[i];
                random -= Mathf.Max(0.01f, definition.selectionWeight <= 0f ? 1f : definition.selectionWeight);
                if (random <= 0f)
                    return definition;
            }
            return candidates[candidates.Count - 1];
        }

        InteractionRuntimeInstance CreateInteractionInstance(
            InteractionDefinition definition,
            string actorNpcId,
            string targetNpcId,
            float nowTime)
        {
            var instance = new InteractionRuntimeInstance
            {
                instanceId = Guid.NewGuid().ToString("N"),
                interactionId = definition.id ?? string.Empty,
                interactionDisplayName = string.IsNullOrWhiteSpace(definition.displayName) ? definition.id ?? string.Empty : definition.displayName.Trim(),
                interactionGoal = InteractionEffectResolver.ResolveInteractionGoal(definition),
                actorNpcId = actorNpcId ?? string.Empty,
                targetNpcId = targetNpcId ?? string.Empty,
                phase = "start",
                stepIndex = 0,
                createdAtTime = nowTime,
                updatedAtTime = nowTime,
                nextStepAtTime = nowTime,
                status = InteractionRuntimeStatus.Running
            };

            if (definition.expiry != null && string.Equals(definition.expiry.type, "ttl_hours", StringComparison.OrdinalIgnoreCase))
            {
                if (definition.expiry.ttlHours > 0f)
                    instance.expiresAtTime = nowTime + (definition.expiry.ttlHours * 3600f);
            }

            instance.heroJoinEnabled = IsHeroJoinInteraction(definition);
            PopulateRoleAssignments(instance, definition);
            return instance;
        }

        static void PopulateRoleAssignments(InteractionRuntimeInstance instance, InteractionDefinition definition)
        {
            if (instance == null)
                return;
            instance.roleToNpcId.Clear();
            if (definition?.roles == null || definition.roles.Count == 0)
            {
                instance.roleToNpcId["initiator"] = instance.actorNpcId;
                instance.roleToNpcId["target"] = instance.targetNpcId;
                return;
            }

            for (var i = 0; i < definition.roles.Count; i++)
            {
                var role = definition.roles[i];
                if (string.IsNullOrWhiteSpace(role))
                    continue;
                var trimmed = role.Trim();
                if (i == 0)
                    instance.roleToNpcId[trimmed] = instance.actorNpcId;
                else if (i == 1)
                    instance.roleToNpcId[trimmed] = instance.targetNpcId;
            }

            instance.roleToNpcId["hero"] = InventoryService.HeroActorId;
            instance.roleToNpcId["traveler"] = InventoryService.HeroActorId;
            instance.roleToNpcId["player"] = InventoryService.HeroActorId;
            if (instance.extraParticipantNpcIds != null)
            {
                for (var i = 0; i < instance.extraParticipantNpcIds.Count; i++)
                {
                    var extraId = instance.extraParticipantNpcIds[i];
                    if (string.IsNullOrWhiteSpace(extraId))
                        continue;
                    instance.roleToNpcId[$"audience_{i + 2}"] = extraId.Trim();
                    instance.roleToNpcId[$"extra_{i + 1}"] = extraId.Trim();
                }
            }
        }

        static bool IsHeroJoinInteraction(InteractionDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.id))
                return false;
            switch (definition.id.Trim().ToLowerInvariant())
            {
                case "romantic_relationship":
                case "start_cult":
                case "cult_conversion":
                case "elect_mayor":
                case "group_persuasion":
                    return true;
                default:
                    return false;
            }
        }

        void AdvanceInteractionStep(InteractionRuntimeInstance instance, float nowTime)
        {
            var definition = FindInteractionDefinition(instance.interactionId);
            if (definition == null || definition.phases == null)
            {
                CompleteInteraction(instance, InteractionRuntimeStatus.Failed, "missing_definition", nowTime);
                return;
            }

            if (instance.awaitingDialogueStep)
            {
                if (nowTime < instance.dialogueStepDeadline)
                    return;
                instance.awaitingDialogueStep = false;
                RecordInteractionStepResult(instance, new InteractionEffectResolver.StepResult(false, "dialogue timed out"));
                instance.stepIndex++;
                instance.nextStepAtTime = nowTime + Mathf.Max(0.2f, interactionStepDurationSeconds);
                instance.updatedAtTime = nowTime;
                return;
            }

            if (!TryGetPhaseSteps(definition, instance.phase, out var phaseSteps))
            {
                CompleteInteraction(instance, InteractionRuntimeStatus.Failed, "invalid_phase", nowTime);
                return;
            }

            if (phaseSteps == null || phaseSteps.Count == 0)
            {
                if (!TryAdvanceToNextPhase(definition, instance, nowTime))
                    CompleteInteraction(instance, InteractionRuntimeStatus.Completed, "phase_empty_complete", nowTime);
                return;
            }

            if (instance.stepIndex < 0 || instance.stepIndex >= phaseSteps.Count)
            {
                if (!TryAdvanceToNextPhase(definition, instance, nowTime))
                    CompleteInteraction(instance, InteractionRuntimeStatus.Completed, "phase_complete", nowTime);
                return;
            }

            var step = phaseSteps[instance.stepIndex];
            var actorNpcId = ResolveInteractionRoleNpcId(instance, step?.actorRole, preferActor: true);
            var targetNpcId = ResolveInteractionRoleNpcId(instance, step?.targetRole, preferActor: false);
            if (string.IsNullOrWhiteSpace(targetNpcId))
            {
                targetNpcId = string.Equals(actorNpcId, instance.actorNpcId, StringComparison.OrdinalIgnoreCase)
                    ? instance.targetNpcId
                    : instance.actorNpcId;
            }

            instance.currentActionType = step != null && !string.IsNullOrWhiteSpace(step.actionType)
                ? step.actionType.Trim()
                : string.Empty;
            instance.currentActionActorId = actorNpcId ?? string.Empty;
            instance.currentActionTargetId = targetNpcId ?? string.Empty;
            instance.updatedAtTime = nowTime;

            var actionType = step != null && !string.IsNullOrWhiteSpace(step.actionType)
                ? step.actionType.Trim().ToLowerInvariant()
                : string.Empty;

            if (!TryApplyInteractionStep(instance, step, actorNpcId, targetNpcId, nowTime))
            {
                instance.nextStepAtTime = nowTime + 0.5f;
                return;
            }

            if (actionType == InteractionActionTypes.EngageDialogue)
            {
                instance.awaitingDialogueStep = true;
                instance.dialogueStepDeadline = nowTime + Mathf.Max(5f, interactionDialogueTimeoutSeconds);
                instance.nextStepAtTime = nowTime + Mathf.Max(5f, interactionDialogueTimeoutSeconds);
                return;
            }

            instance.nextStepAtTime = nowTime + Mathf.Max(0.2f, interactionStepDurationSeconds);
            instance.stepIndex++;
        }

        bool TryApplyInteractionStep(
            InteractionRuntimeInstance instance,
            InteractionActionStep step,
            string actorNpcId,
            string targetNpcId,
            float nowTime)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.actionType))
                return true;

            var actionType = step.actionType.Trim().ToLowerInvariant();
            if (actionType == InteractionActionTypes.MoveToLocation)
            {
                if (!IsMoveToLocationSatisfied(instance, step, actorNpcId))
                {
                    ApplyMoveToLocation(instance, step, actorNpcId);
                    return false;
                }
            }
            else if ((actionType == InteractionActionTypes.MoveToNpc || actionType == InteractionActionTypes.MoveToHero)
                && !AreInteractionActorsInRange(actorNpcId, targetNpcId))
            {
                DispatchInitiatorApproach(instance);
                return false;
            }

            if (actionType == InteractionActionTypes.EngageDialogue
                && !AreInteractionActorsInRange(actorNpcId, targetNpcId))
            {
                DispatchInitiatorApproach(instance);
                return false;
            }

            if ((actionType == InteractionActionTypes.ExchangeItem
                    || actionType == InteractionActionTypes.ExchangeCoins)
                && !AreInteractionActorsInRange(actorNpcId, targetNpcId))
            {
                DispatchInitiatorApproach(instance);
                return false;
            }

            if (!ValidateInteractionStepReferences(instance, step, actorNpcId, targetNpcId, out var refError))
            {
                RecordInteractionStepResult(instance, new InteractionEffectResolver.StepResult(false, refError));
                return true;
            }

            ApplyInteractionStep(instance, step, nowTime);
            return true;
        }

        bool ValidateInteractionStepReferences(
            InteractionRuntimeInstance instance,
            InteractionActionStep step,
            string actorNpcId,
            string targetNpcId,
            out string error)
        {
            error = string.Empty;
            if (step == null || string.IsNullOrWhiteSpace(step.actionType))
                return true;

            var actionType = step.actionType.Trim().ToLowerInvariant();
            if (actionType == InteractionActionTypes.MoveToNpc
                || actionType == InteractionActionTypes.EngageDialogue
                || actionType == InteractionActionTypes.ExchangeItem
                || actionType == InteractionActionTypes.ExchangeCoins)
            {
                if (!IsHeroParticipant(actorNpcId) && !TryGetRuntimeState(actorNpcId, out _))
                {
                    error = "invalid actor ref";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(targetNpcId)
                    && !IsHeroParticipant(targetNpcId)
                    && !TryGetRuntimeState(targetNpcId, out _))
                {
                    error = "invalid target ref";
                    return false;
                }
            }

            if (actionType == InteractionActionTypes.MoveToLocation)
            {
                var locationRef = ResolveStepParameter(step, "locationRef");
                if (string.IsNullOrWhiteSpace(locationRef))
                {
                    error = "missing locationRef";
                    return false;
                }

                if (!string.Equals(locationRef, "escape_nearby", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(locationRef, "service_location", StringComparison.OrdinalIgnoreCase)
                    && !_staticLocationIds.Exists(id => string.Equals(id, locationRef, StringComparison.OrdinalIgnoreCase)))
                {
                    error = $"unknown locationRef '{locationRef}'";
                    return false;
                }
            }

            return true;
        }

        bool TryAdvanceToNextPhase(InteractionDefinition definition, InteractionRuntimeInstance instance, float nowTime)
        {
            var currentPhase = instance.phase ?? "start";
            if (string.Equals(currentPhase, "start", StringComparison.OrdinalIgnoreCase))
            {
                if (definition.phases.loop != null && definition.phases.loop.Count > 0)
                {
                    instance.phase = "loop";
                    instance.stepIndex = 0;
                    instance.updatedAtTime = nowTime;
                    return true;
                }

                if (definition.phases.end != null && definition.phases.end.Count > 0)
                {
                    instance.phase = "end";
                    instance.stepIndex = 0;
                    instance.updatedAtTime = nowTime;
                    return true;
                }

                return false;
            }

            if (string.Equals(currentPhase, "loop", StringComparison.OrdinalIgnoreCase))
            {
                instance.loopIteration++;
                if (instance.loopIteration < Mathf.Max(0, maxLoopIterationsPerInteraction))
                {
                    instance.stepIndex = 0;
                    instance.updatedAtTime = nowTime;
                    return true;
                }

                if (definition.phases.end != null && definition.phases.end.Count > 0)
                {
                    instance.phase = "end";
                    instance.stepIndex = 0;
                    instance.updatedAtTime = nowTime;
                    return true;
                }

                return false;
            }

            if (string.Equals(currentPhase, "end", StringComparison.OrdinalIgnoreCase))
                return false;

            return false;
        }

        static bool TryGetPhaseSteps(InteractionDefinition definition, string phase, out List<InteractionActionStep> steps)
        {
            steps = null;
            if (definition == null || definition.phases == null)
                return false;
            var key = string.IsNullOrWhiteSpace(phase) ? "start" : phase.Trim().ToLowerInvariant();
            switch (key)
            {
                case "start":
                    steps = definition.phases.start;
                    return true;
                case "loop":
                    steps = definition.phases.loop;
                    return true;
                case "end":
                    steps = definition.phases.end;
                    return true;
                default:
                    return false;
            }
        }

        void ApplyInteractionStep(InteractionRuntimeInstance instance, InteractionActionStep step, float nowTime)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.actionType))
                return;

            var actionType = step.actionType.Trim().ToLowerInvariant();
            var actorNpcId = ResolveInteractionRoleNpcId(instance, step.actorRole, preferActor: true);
            var targetNpcId = ResolveInteractionRoleNpcId(instance, step.targetRole, preferActor: false);
            if (string.IsNullOrWhiteSpace(targetNpcId))
                targetNpcId = string.Equals(actorNpcId, instance.actorNpcId, StringComparison.OrdinalIgnoreCase)
                    ? instance.targetNpcId
                    : instance.actorNpcId;

            switch (actionType)
            {
                case InteractionActionTypes.MoveToLocation:
                    ApplyMoveToLocation(instance, step, actorNpcId);
                    break;
                case InteractionActionTypes.MoveToHero:
                    if (!IsHeroParticipant(targetNpcId))
                        targetNpcId = InventoryService.HeroActorId;
                    if (!IsHeroParticipant(targetNpcId) || IsHeroParticipant(actorNpcId))
                        break;
                    if (TryGetRuntimeState(actorNpcId, out var heroMoverState)
                        && heroMoverState != null
                        && heroMoverState.Controller != null
                        && TryResolveHeroTransform(out var heroTransform))
                    {
                        AssignNpcMovePlan(heroMoverState, heroTransform.position, interactionApproachMaxSeconds, 1.6f);
                    }
                    break;
                case InteractionActionTypes.MoveToNpc:
                    if (IsHeroParticipant(targetNpcId) && TryResolveHeroTransform(out var heroMoveTarget))
                    {
                        if (TryGetRuntimeState(actorNpcId, out var heroApproachState)
                            && heroApproachState != null
                            && heroApproachState.Controller != null)
                        {
                            AssignNpcMovePlan(
                                heroApproachState,
                                heroMoveTarget.position,
                                interactionApproachMaxSeconds,
                                1.6f);
                        }
                        break;
                    }
                    if (TryGetRuntimeState(actorNpcId, out var moverState)
                        && moverState != null
                        && moverState.Controller != null
                        && !string.IsNullOrWhiteSpace(targetNpcId))
                    {
                        var plan = new List<NpcPrimitiveStep>
                        {
                            new NpcPrimitiveStep
                            {
                                PrimitiveType = NpcPrimitiveTypes.GotoNpc,
                                TargetNpcId = targetNpcId,
                                DurationSeconds = Mathf.Max(0.2f, interactionApproachMaxSeconds),
                                StopDistanceMeters = 1.6f
                            }
                        };
                        moverState.ActivePlan = plan;
                        moverState.Controller.SetPlan(plan);
                    }
                    break;
                case InteractionActionTypes.EngageDialogue:
                    if (IsHeroParticipant(targetNpcId) && TryResolveHeroTransform(out var heroChatTarget))
                    {
                        if (TryGetRuntimeState(actorNpcId, out var heroSpeakerState)
                            && heroSpeakerState != null
                            && heroSpeakerState.Controller != null)
                        {
                            AssignNpcChatPlan(heroSpeakerState, InventoryService.HeroActorId, heroChatTarget.position, interactionStepDurationSeconds, 1.6f);
                        }
                    }
                    else if (IsHeroParticipant(actorNpcId)
                        && TryResolveHeroTransform(out var heroActorTarget)
                        && TryGetRuntimeState(targetNpcId, out var heroTargetSpeaker)
                        && heroTargetSpeaker != null
                        && heroTargetSpeaker.Controller != null)
                    {
                        AssignNpcChatPlan(
                            heroTargetSpeaker,
                            InventoryService.HeroActorId,
                            heroActorTarget.position,
                            interactionStepDurationSeconds,
                            1.6f);
                    }
                    else if (TryGetRuntimeState(actorNpcId, out var speakerState)
                        && speakerState != null
                        && speakerState.Controller != null
                        && !string.IsNullOrWhiteSpace(targetNpcId)
                        && TryGetRuntimeState(targetNpcId, out var targetState)
                        && targetState != null)
                    {
                        var worldLocation = targetState.Binding != null
                            ? targetState.Binding.transform.position
                            : speakerState.Controller.transform.position;
                        AssignNpcChatPlan(speakerState, targetNpcId, worldLocation, interactionStepDurationSeconds, 1.6f);
                    }
                    if (_opinionService != null
                        && !string.IsNullOrWhiteSpace(actorNpcId)
                        && !string.IsNullOrWhiteSpace(targetNpcId)
                        && !IsHeroParticipant(actorNpcId)
                        && !IsHeroParticipant(targetNpcId))
                    {
                        _opinionService.QueueInteraction(actorNpcId, targetNpcId);
                    }

                    var topic = ResolveInteractionStepTopic(step, instance);
                    _lastDialoguePhase = instance.phase ?? "start";
                    var manager = DialogueManager.Instance;
                    if (manager != null)
                    {
                        manager.NotifyInteractionEngageDialogue(
                            this,
                            instance,
                            actorNpcId,
                            targetNpcId,
                            topic,
                            _lastDialoguePhase,
                            step);
                    }
                    else if (_autoCompleteInteractionDialogue)
                    {
                        NotifyInteractionDialogueStepCompleted(instance.instanceId, true);
                    }
                    break;
                case InteractionActionTypes.ExchangeItem:
                    var itemResult = InteractionEffectResolver.ApplyExchangeItem(
                        ResolveInventory(),
                        instance,
                        step,
                        actorNpcId,
                        targetNpcId);
                    RecordInteractionStepResult(instance, itemResult);
                    break;
                case InteractionActionTypes.ExchangeCoins:
                    var coinResult = InteractionEffectResolver.ApplyExchangeCoins(
                        ResolveInventory(),
                        instance,
                        FindInteractionDefinition(instance.interactionId),
                        step,
                        actorNpcId,
                        targetNpcId,
                        _staticLocationIds,
                        ResolveAgreements());
                    RecordInteractionStepResult(instance, coinResult);
                    if (_opinionService != null
                        && !string.IsNullOrWhiteSpace(actorNpcId)
                        && !string.IsNullOrWhiteSpace(targetNpcId))
                    {
                        _opinionService.QueueInteraction(actorNpcId, targetNpcId);
                    }
                    break;
            }

            instance.updatedAtTime = nowTime;
            DialogueTelemetry.Log(
                "VillageInteractionStep",
                $"instance={instance.instanceId}, interaction={instance.interactionId}, phase={instance.phase}, step={instance.stepIndex}, action={actionType}, actor={instance.actorNpcId}, target={instance.targetNpcId}");
        }

        void RecordInteractionStepResult(InteractionRuntimeInstance instance, InteractionEffectResolver.StepResult result)
        {
            if (instance == null || string.IsNullOrWhiteSpace(result.Summary))
                return;
            instance.stepLog.Add(result.Summary);
            if (instance.stepLog.Count > 12)
                instance.stepLog.RemoveAt(0);
        }

        static string ResolveInteractionRoleNpcId(InteractionRuntimeInstance instance, string role, bool preferActor)
        {
            if (instance == null)
                return string.Empty;
            var actor = instance.actorNpcId ?? string.Empty;
            var target = instance.targetNpcId ?? string.Empty;
            var key = string.IsNullOrWhiteSpace(role) ? string.Empty : role.Trim();
            if (string.IsNullOrWhiteSpace(key))
                return preferActor ? actor : target;
            if (instance.roleToNpcId != null
                && instance.roleToNpcId.TryGetValue(key, out var mapped)
                && !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            if (string.Equals(key, "audience", StringComparison.OrdinalIgnoreCase)
                && instance.extraParticipantNpcIds != null
                && instance.extraParticipantNpcIds.Count > 0
                && instance.loopIteration > 0)
            {
                var idx = (instance.loopIteration - 1) % instance.extraParticipantNpcIds.Count;
                return instance.extraParticipantNpcIds[idx];
            }

            if (key.StartsWith("audience_", StringComparison.OrdinalIgnoreCase)
                && instance.roleToNpcId != null
                && instance.roleToNpcId.TryGetValue(key, out mapped)
                && !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped;
            }

            if (key.IndexOf("hero", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("traveler", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return InventoryService.HeroActorId;
            }

            return preferActor ? actor : target;
        }

        bool IsMoveToLocationSatisfied(
            InteractionRuntimeInstance instance,
            InteractionActionStep step,
            string actorNpcId)
        {
            if (IsHeroParticipant(actorNpcId))
                return true;
            if (!TryResolveMoveToLocationPosition(instance, step, actorNpcId, out var destination))
                return true;
            if (!TryGetNpcPosition(actorNpcId, out var actorPos))
                return false;
            var delta = new Vector2(actorPos.x - destination.x, actorPos.z - destination.z);
            return delta.magnitude <= Mathf.Max(0.5f, interactionEngageRangeMeters);
        }

        void ApplyMoveToLocation(InteractionRuntimeInstance instance, InteractionActionStep step, string actorNpcId)
        {
            if (IsHeroParticipant(actorNpcId))
                return;
            if (!TryGetRuntimeState(actorNpcId, out var state) || state?.Controller == null)
                return;
            if (!TryResolveMoveToLocationPosition(instance, step, actorNpcId, out var destination))
                return;
            AssignNpcMovePlan(state, destination, interactionApproachMaxSeconds, 1.6f);
        }

        bool TryResolveMoveToLocationPosition(
            InteractionRuntimeInstance instance,
            InteractionActionStep step,
            string actorNpcId,
            out Vector3 position)
        {
            position = Vector3.zero;
            var locationRef = ResolveStepParameter(step, "locationRef");
            if (string.IsNullOrWhiteSpace(locationRef))
                return false;

            if (string.Equals(locationRef, "escape_nearby", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetNpcPosition(actorNpcId, out var actorPos))
                    return false;
                if (TryGetNpcPosition(instance?.targetNpcId, out var targetPos))
                {
                    var away = actorPos - targetPos;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f)
                        away = Vector3.forward;
                    position = actorPos + away.normalized * 8f;
                    return true;
                }

                position = actorPos + Vector3.forward * 8f;
                return true;
            }

            if (string.Equals(locationRef, "service_location", StringComparison.OrdinalIgnoreCase))
            {
                if (_locationRegistry != null && _locationRegistry.TryResolve("warehouse", out var warehouse) && warehouse != null)
                {
                    position = warehouse.position;
                    return true;
                }
            }

            if (_locationRegistry != null && _locationRegistry.TryResolve(locationRef, out var anchor) && anchor != null)
            {
                position = anchor.position;
                return true;
            }

            for (var i = 0; i < _staticLocationIds.Count; i++)
            {
                var id = _staticLocationIds[i];
                if (!string.Equals(id, locationRef, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_locationRegistry != null && _locationRegistry.TryResolve(id, out anchor) && anchor != null)
                {
                    position = anchor.position;
                    return true;
                }
            }

            return false;
        }

        static string ResolveStepParameter(InteractionActionStep step, string key)
        {
            if (step?.parameters == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;
            return step.parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : string.Empty;
        }

        void CompleteInteraction(InteractionRuntimeInstance instance, InteractionRuntimeStatus status, string reason, float nowTime)
        {
            if (instance == null)
                return;
            var definition = FindInteractionDefinition(instance.interactionId);
            if (status == InteractionRuntimeStatus.Completed)
            {
                var outcomeId = InteractionEffectResolver.ResolveOutcomeId(definition, instance);
                instance.resolvedOutcomeId = outcomeId;
                InteractionEffectResolver.ApplyOutcomeState(instance, definition, outcomeId, _opinionService);
                _interactionMemory?.RecordInteractionCompleted(instance, definition, outcomeId, ResolveDisplayNameFromState);
            }

            instance.status = status;
            instance.statusReason = reason ?? string.Empty;
            instance.updatedAtTime = nowTime;
            if (status != InteractionRuntimeStatus.Running)
                UnlockNpcsForInteraction(instance);
            DialogueTelemetry.Log(
                "VillageInteractionCompleted",
                $"instance={instance.instanceId}, interaction={instance.interactionId}, status={status}, reason={instance.statusReason}, outcome={instance.resolvedOutcomeId}, actor={instance.actorNpcId}, target={instance.targetNpcId}");
        }

        InteractionDefinition FindInteractionDefinition(string interactionId)
        {
            if (_interactionDefinitions == null || _interactionDefinitions.interactions == null || string.IsNullOrWhiteSpace(interactionId))
                return null;
            for (var i = 0; i < _interactionDefinitions.interactions.Count; i++)
            {
                var item = _interactionDefinitions.interactions[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                if (string.Equals(item.id.Trim(), interactionId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
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

            PersistProposedInteractions(envelope.ProposedInteractions);

            if (envelope.PlanSteps == null || envelope.PlanSteps.Count == 0)
                return BuildFallbackEnvelope(state, "empty_sidecar_plan");

            envelope.PlanSteps = ClampAndCloneSteps(envelope.PlanSteps);
            if (!ValidatePlanReferences(state.NpcId, envelope.PlanSteps, out var planRefError))
                return BuildFallbackEnvelope(state, "invalid_plan_refs:" + planRefError);
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
                npcId = npcId,
                goal = ResolvePrimaryGoal(state.ActiveGoals, reason),
                maxSteps = Mathf.Max(1, maxPlanStepsPerDeliberation),
                targets = BuildDeliberationTargets(npcId),
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
                    worldFacts = BuildWorldFactsBlock(world, npcId),
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

        void RefreshStaticWorldReferences()
        {
            _staticLocationIds.Clear();
            var library = new NarrativeContentLibrary();
            var locationCatalog = library.LoadLocationCatalog();
            if (locationCatalog == null || locationCatalog.locations == null)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < locationCatalog.locations.Count; i++)
            {
                var entry = locationCatalog.locations[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                    continue;
                var id = entry.id.Trim();
                if (seen.Add(id))
                    _staticLocationIds.Add(id);
            }
        }

        void RefreshNpcReferenceSnapshot(float nowTime)
        {
            if (nowTime < _nextNpcReferenceRefreshAt)
                return;

            _nextNpcReferenceRefreshAt = nowTime + Mathf.Clamp(npcReferenceRefreshSeconds, 10f, 30f);
            _npcPositionById.Clear();
            _knownWorkIds.Clear();
            var seenWorkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _stateByNpcId)
            {
                var state = kvp.Value;
                if (state == null)
                    continue;
                if (state.Binding != null)
                    _npcPositionById[state.NpcId] = state.Binding.transform.position;

                if (state.ActivePlan == null)
                    continue;
                for (var i = 0; i < state.ActivePlan.Count; i++)
                {
                    var step = state.ActivePlan[i];
                    if (step == null || string.IsNullOrWhiteSpace(step.WorkId))
                        continue;
                    var workId = step.WorkId.Trim();
                    if (seenWorkIds.Add(workId))
                        _knownWorkIds.Add(workId);
                }
            }

            RefreshWorldReferenceSnapshot(nowTime);
        }

        PythonDeliberationTargetsDto BuildDeliberationTargets(string requesterNpcId)
        {
            var targets = new PythonDeliberationTargetsDto();
            for (var i = 0; i < _staticLocationIds.Count; i++)
                targets.locationIds.Add(_staticLocationIds[i]);
            for (var i = 0; i < _participantCache.Count; i++)
            {
                var npcId = _participantCache[i];
                if (!string.IsNullOrWhiteSpace(npcId))
                    targets.npcIds.Add(npcId);
            }
            for (var i = 0; i < _knownWorkIds.Count; i++)
                targets.workIds.Add(_knownWorkIds[i]);
            if (!string.IsNullOrWhiteSpace(requesterNpcId) && !targets.npcIds.Contains(requesterNpcId))
                targets.npcIds.Add(requesterNpcId);
            return targets;
        }

        string BuildWorldFactsBlock(WorldStateSnapshot world, string requesterNpcId)
        {
            var lines = new List<string> { world.ToFactsBlock() };
            if (_participantCache.Count > 0)
            {
                lines.Add("Known NPCs (authoritative ids, names, positions):");
                for (var i = 0; i < _participantCache.Count; i++)
                {
                    var npcId = _participantCache[i];
                    if (string.IsNullOrWhiteSpace(npcId))
                        continue;
                    var displayName = ResolveDisplayNameFromState(npcId);
                    var relationship = _opinionService != null
                        ? _opinionService.GetSummary(npcId).OpinionTowardHero.ToString("F0")
                        : "0";
                    var position = _npcPositionById.TryGetValue(npcId, out var p)
                        ? $"({p.x:F1},{p.y:F1},{p.z:F1})"
                        : "(unknown)";
                    lines.Add($"- {npcId} | {displayName} | pos={position} | opinionTowardHero={relationship}");
                }
            }

            lines.Add("Known valid locationIds:");
            lines.Add(_staticLocationIds.Count > 0
                ? "- " + string.Join(", ", _staticLocationIds)
                : "- (none)");

            if (_knownWorkIds.Count > 0)
            {
                lines.Add("Known valid workIds:");
                lines.Add("- " + string.Join(", ", _knownWorkIds));
            }

            lines.Add($"Requesting npcId: {requesterNpcId}");
            return string.Join("\n", lines);
        }

        void PersistProposedInteractions(List<InteractionDefinition> candidates)
        {
            if (_interactionRegistry == null || candidates == null || candidates.Count == 0)
                return;

            var persisted = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.id))
                    continue;
                if (_interactionRegistry.TryAddOrUpdateProposed(candidate, out var error))
                {
                    persisted++;
                    DialogueTelemetry.Log("VillageInteractionProposed", $"id={candidate.id}");
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    DialogueTelemetry.Log("VillageInteractionProposalRejected", $"id={candidate.id}, reason={error}");
                }
            }

            if (persisted > 0)
            {
                _interactionDefinitions = _interactionRegistry.LoadEffective(out var issues);
                if (issues != null && issues.Count > 0)
                {
                    for (var i = 0; i < issues.Count; i++)
                        Debug.LogWarning($"[VillageAgentSimulation] Interaction definition issue after proposal merge: {issues[i]}");
                }
            }
        }

        string ResolveDisplayNameFromState(string npcId)
        {
            if (IsHeroParticipant(npcId))
                return "the traveler";
            if (!TryGetRuntimeState(npcId, out var state) || state == null)
                return npcId;
            var binding = state.Binding;
            if (binding != null && binding.Definition != null && !string.IsNullOrWhiteSpace(binding.Definition.displayName))
                return binding.Definition.displayName.Trim();
            return npcId;
        }

        static bool IsHeroParticipant(string npcId) =>
            string.Equals(npcId?.Trim(), InventoryService.HeroActorId, StringComparison.OrdinalIgnoreCase);

        bool IsValidInteractionParticipant(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return false;
            if (IsHeroParticipant(npcId))
                return true;
            return TryGetRuntimeState(npcId, out _);
        }

        static string ResolvePrimaryGoal(List<string> goals, string fallbackReason)
        {
            if (goals != null)
            {
                for (var i = 0; i < goals.Count; i++)
                {
                    var candidate = goals[i];
                    if (!string.IsNullOrWhiteSpace(candidate))
                        return candidate.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackReason))
                return fallbackReason.Trim();
            return "continue current routine";
        }

        bool ValidatePlanReferences(string selfNpcId, List<NpcPrimitiveStep> plan, out string error)
        {
            error = string.Empty;
            if (plan == null || plan.Count == 0)
                return true;

            for (var i = 0; i < plan.Count; i++)
            {
                var step = plan[i];
                if (step == null)
                    continue;
                if (!NpcPrimitiveTypes.TryParse(step.PrimitiveType, out var kind))
                {
                    error = $"unknown_primitive@{i}:{step.PrimitiveType}";
                    return false;
                }

                var targetNpcId = step.TargetNpcId != null ? step.TargetNpcId.Trim() : string.Empty;
                var workId = step.WorkId != null ? step.WorkId.Trim() : string.Empty;
                switch (kind)
                {
                    case NpcPrimitiveKind.GotoLocation:
                    case NpcPrimitiveKind.WaitAt:
                        if (!string.IsNullOrWhiteSpace(targetNpcId) && !_staticLocationIds.Contains(targetNpcId))
                        {
                            error = $"invalid_location@{i}:{targetNpcId}";
                            return false;
                        }
                        break;
                    case NpcPrimitiveKind.GotoNpc:
                    case NpcPrimitiveKind.ChatWithNpc:
                        if (string.IsNullOrWhiteSpace(targetNpcId) || !_participantCache.Contains(targetNpcId))
                        {
                            error = $"invalid_npc@{i}:{targetNpcId}";
                            return false;
                        }
                        if (!string.IsNullOrWhiteSpace(selfNpcId)
                            && string.Equals(targetNpcId, selfNpcId, StringComparison.OrdinalIgnoreCase))
                        {
                            error = $"self_target@{i}:{targetNpcId}";
                            return false;
                        }
                        break;
                    case NpcPrimitiveKind.PerformWork:
                        if (string.IsNullOrWhiteSpace(workId) || !_knownWorkIds.Contains(workId))
                        {
                            error = $"invalid_work@{i}:{workId}";
                            return false;
                        }
                        break;
                }
            }

            return true;
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
            if (!string.IsNullOrWhiteSpace(state.LastError)
                && state.LastError.StartsWith("invalid_plan_refs:", StringComparison.OrdinalIgnoreCase))
            {
                DialogueTelemetry.Log("VillageDeliberationRejected", $"npc={npcId} reason={state.LastError}");
                _scheduler?.RequestImmediate(npcId, "invalid_plan_refs_replan");
            }
            if (envelope != null && envelope.UsedFallback)
                _telemetry?.RecordFallback();
            _telemetry?.RecordPlanCompletion(envelope != null && envelope.Success);

            if (plan.Count > 0 && state.Controller != null && !IsNpcLockedByActiveInteraction(npcId))
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

        static bool TryResolveInteractableNpcEntry(NpcDialogueBinding binding, out string npcId, out NpcAgentController controller)
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
            if (GhoulMenaceController.IsGhoulStoryNpcId(id))
                return false;

            npcId = id.Trim();
            return true;
        }

        static bool IsAutonomyParticipant(NpcDialogueBinding binding, string npcId)
        {
            if (binding == null || string.IsNullOrWhiteSpace(npcId))
                return false;
            if (SidekickCompanion.FindForNpcBindingRoot(binding.gameObject) != null)
                return false;
            return true;
        }

        static bool TryResolveVillagerEntry(NpcDialogueBinding binding, out string npcId, out NpcAgentController controller)
        {
            if (!TryResolveInteractableNpcEntry(binding, out npcId, out controller))
                return false;
            return IsAutonomyParticipant(binding, npcId);
        }

        bool TryGetRuntimeState(string npcId, out VillagerRuntimeState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(npcId))
                return false;
            return _stateByNpcId.TryGetValue(npcId.Trim(), out state) && state != null;
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
            return NarrativePhantomReferenceFilter.SanitizeGoalList(normalized);
        }

        static void AddUniqueGoals(List<string> target, List<string> source)
        {
            if (target == null || source == null)
                return;
            for (var i = 0; i < source.Count; i++)
            {
                var g = source[i];
                if (string.IsNullOrWhiteSpace(g) || NarrativePhantomReferenceFilter.ContainsPhantomReference(g))
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
                    PlanSteps = plan,
                    ProposedInteractions = envelope.deliberation.proposedInteractions != null
                        ? new List<InteractionDefinition>(envelope.deliberation.proposedInteractions)
                        : new List<InteractionDefinition>()
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
            public List<InteractionDefinition> ProposedInteractions = new List<InteractionDefinition>();

            public static VillagerDeliberationEnvelope Failure(string error, bool usedFallback)
            {
                return new VillagerDeliberationEnvelope
                {
                    Success = false,
                    UsedFallback = usedFallback,
                    Error = error ?? string.Empty,
                    ProposedInteractions = new List<InteractionDefinition>()
                };
            }
        }
    }
}
