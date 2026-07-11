using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Npc
{
    public static class InteractionActionTypes
    {
        public const string MoveToLocation = "move_to_location";
        public const string MoveToNpc = "move_to_npc";
        public const string MoveToHero = "move_to_hero";
        public const string EngageDialogue = "engage_dialogue";
        public const string ExchangeItem = "exchange_item";
        public const string ExchangeCoins = "exchange_coins";
    }

    [Serializable]
    public sealed class InteractionDefinitionsDoc
    {
        public int schemaVersion = 1;
        public List<string> atomicActionTypes = new List<string>();
        public List<InteractionDefinition> interactions = new List<InteractionDefinition>();
    }

    [Serializable]
    public sealed class InteractionDefinition
    {
        public string id = string.Empty;
        public string displayName = string.Empty;
        public string goal = string.Empty;
        public string source = "seed";
        public string status = "active";
        public List<string> roles = new List<string>();
        public float selectionWeight = 1f;
        public float cooldownMinutes = 0f;
        public InteractionPhases phases = new InteractionPhases();
        public List<InteractionOutcome> outcomes = new List<InteractionOutcome>();
        public InteractionExpiry expiry = new InteractionExpiry();
        public bool spawnFromChat = true;
    }

    [Serializable]
    public sealed class InteractionPhases
    {
        public List<InteractionActionStep> start = new List<InteractionActionStep>();
        public List<InteractionActionStep> loop = new List<InteractionActionStep>();
        public List<InteractionActionStep> end = new List<InteractionActionStep>();
    }

    [Serializable]
    public sealed class InteractionActionStep
    {
        public string actionType = string.Empty;
        public string actorRole = string.Empty;
        public string targetRole = string.Empty;
        public Dictionary<string, string> parameters = new Dictionary<string, string>();
    }

    [Serializable]
    public sealed class InteractionOutcome
    {
        public string id = string.Empty;
        public float probability = 0f;
        public List<string> effects = new List<string>();
    }

    [Serializable]
    public sealed class InteractionExpiry
    {
        public string type = "immediate_end_after_start";
        public float ttlHours = 0f;
    }

    public enum InteractionRuntimeStatus
    {
        Running = 0,
        Completed = 1,
        Expired = 2,
        Failed = 3
    }

    [Serializable]
    public sealed class InteractionRuntimeInstance
    {
        public string instanceId = string.Empty;
        public string interactionId = string.Empty;
        public string interactionDisplayName = string.Empty;
        public string actorNpcId = string.Empty;
        public string targetNpcId = string.Empty;
        public string phase = "start";
        public int stepIndex;
        public string currentActionType = string.Empty;
        public string currentActionActorId = string.Empty;
        public string currentActionTargetId = string.Empty;
        public float createdAtTime;
        public float updatedAtTime;
        public float expiresAtTime = -1f;
        public float nextStepAtTime;
        public int loopIteration;
        public bool pausedByHero;
        public InteractionRuntimeStatus status = InteractionRuntimeStatus.Running;
        public string statusReason = string.Empty;
        public string resolvedOutcomeId = string.Empty;
        public string interactionGoal = string.Empty;
        public string outcomeSummary = string.Empty;
        public string assignedErrand = string.Empty;
        public bool targetIsFollower;
        public readonly List<string> stepLog = new List<string>();
        public List<string> extraParticipantNpcIds = new List<string>();
        public bool heroJoinEnabled;
        public bool awaitingDialogueStep;
        public float dialogueStepDeadline;
        public readonly Dictionary<string, string> roleToNpcId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class InteractionDefinitionValidator
    {
        static readonly HashSet<string> DefaultAllowedActionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            InteractionActionTypes.MoveToLocation,
            InteractionActionTypes.MoveToNpc,
            InteractionActionTypes.MoveToHero,
            InteractionActionTypes.EngageDialogue,
            InteractionActionTypes.ExchangeItem,
            InteractionActionTypes.ExchangeCoins
        };

        static readonly HashSet<string> AllowedStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "proposed",
            "active",
            "disabled"
        };

        public List<string> Validate(InteractionDefinitionsDoc doc)
        {
            var issues = new List<string>();
            if (doc == null)
            {
                issues.Add("Document is null.");
                return issues;
            }

            var allowedActionTypes = BuildAllowedActionTypes(doc.atomicActionTypes);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var interactions = doc.interactions ?? new List<InteractionDefinition>();
            for (var i = 0; i < interactions.Count; i++)
            {
                var item = interactions[i];
                if (item == null)
                {
                    issues.Add($"interactions[{i}] is null.");
                    continue;
                }

                var id = (item.id ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add($"interactions[{i}] has empty id.");
                    continue;
                }

                if (!seenIds.Add(id))
                    issues.Add($"Duplicate interaction id '{id}'.");

                var status = (item.status ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(status) || !AllowedStatuses.Contains(status))
                    issues.Add($"Interaction '{id}' has invalid status '{item.status}'.");

                if (item.selectionWeight < 0f)
                    issues.Add($"Interaction '{id}' has negative selectionWeight.");
                if (item.cooldownMinutes < 0f)
                    issues.Add($"Interaction '{id}' has negative cooldownMinutes.");

                ValidatePhases(id, item.roles, item.phases, allowedActionTypes, issues);
                ValidateOutcomes(id, item.outcomes, issues);
                ValidateExpiry(id, item.expiry, issues);
                ValidateStartPhaseProximityOrdering(id, item.phases, issues);
            }

            return issues;
        }

        static void ValidateStartPhaseProximityOrdering(
            string interactionId,
            InteractionPhases phases,
            List<string> issues)
        {
            if (phases?.start == null || phases.start.Count == 0)
                return;

            var sawMovement = false;
            for (var i = 0; i < phases.start.Count; i++)
            {
                var step = phases.start[i];
                if (step == null || string.IsNullOrWhiteSpace(step.actionType))
                    continue;
                var actionType = step.actionType.Trim().ToLowerInvariant();
                if (actionType == InteractionActionTypes.MoveToNpc
                    || actionType == InteractionActionTypes.MoveToHero
                    || actionType == InteractionActionTypes.MoveToLocation)
                {
                    sawMovement = true;
                    continue;
                }

                if (!sawMovement
                    && (actionType == InteractionActionTypes.EngageDialogue
                        || actionType == InteractionActionTypes.ExchangeItem
                        || actionType == InteractionActionTypes.ExchangeCoins))
                {
                    issues.Add(
                        $"Interaction '{interactionId}' start phase step[{i}] ({actionType}) runs before any movement step.");
                }
            }
        }

        static HashSet<string> BuildAllowedActionTypes(List<string> configured)
        {
            var allowed = new HashSet<string>(DefaultAllowedActionTypes, StringComparer.OrdinalIgnoreCase);
            if (configured == null)
                return allowed;
            for (var i = 0; i < configured.Count; i++)
            {
                var value = configured[i];
                if (!string.IsNullOrWhiteSpace(value))
                    allowed.Add(value.Trim());
            }
            return allowed;
        }

        static void ValidatePhases(
            string interactionId,
            List<string> roles,
            InteractionPhases phases,
            HashSet<string> allowedActionTypes,
            List<string> issues)
        {
            var roleSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (roles != null)
            {
                for (var i = 0; i < roles.Count; i++)
                {
                    var role = (roles[i] ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(role))
                        roleSet.Add(role);
                }
            }

            var hasAnyStep = false;
            if (phases == null)
            {
                issues.Add($"Interaction '{interactionId}' is missing phases.");
                return;
            }

            ValidatePhaseSteps(interactionId, "start", phases.start, roleSet, allowedActionTypes, issues, ref hasAnyStep);
            ValidatePhaseSteps(interactionId, "loop", phases.loop, roleSet, allowedActionTypes, issues, ref hasAnyStep);
            ValidatePhaseSteps(interactionId, "end", phases.end, roleSet, allowedActionTypes, issues, ref hasAnyStep);
            if (!hasAnyStep)
                issues.Add($"Interaction '{interactionId}' has no action steps.");
        }

        static void ValidatePhaseSteps(
            string interactionId,
            string phaseName,
            List<InteractionActionStep> steps,
            HashSet<string> roleSet,
            HashSet<string> allowedActionTypes,
            List<string> issues,
            ref bool hasAnyStep)
        {
            if (steps == null || steps.Count == 0)
                return;
            hasAnyStep = true;
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null)
                {
                    issues.Add($"Interaction '{interactionId}' phase '{phaseName}' has null step at index {i}.");
                    continue;
                }

                var actionType = (step.actionType ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(actionType))
                    issues.Add($"Interaction '{interactionId}' phase '{phaseName}' step[{i}] has empty actionType.");
                else if (!allowedActionTypes.Contains(actionType))
                    issues.Add($"Interaction '{interactionId}' phase '{phaseName}' step[{i}] uses unsupported actionType '{actionType}'.");

                var actorRole = (step.actorRole ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(actorRole))
                {
                    issues.Add($"Interaction '{interactionId}' phase '{phaseName}' step[{i}] has empty actorRole.");
                }
                else if (roleSet.Count > 0 && !roleSet.Contains(actorRole))
                {
                    issues.Add($"Interaction '{interactionId}' phase '{phaseName}' step[{i}] actorRole '{actorRole}' is not in roles.");
                }

                var targetRole = (step.targetRole ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(targetRole) && roleSet.Count > 0 && !roleSet.Contains(targetRole))
                    issues.Add($"Interaction '{interactionId}' phase '{phaseName}' step[{i}] targetRole '{targetRole}' is not in roles.");
            }
        }

        static void ValidateOutcomes(string interactionId, List<InteractionOutcome> outcomes, List<string> issues)
        {
            if (outcomes == null || outcomes.Count == 0)
                return;

            var totalProbability = 0f;
            for (var i = 0; i < outcomes.Count; i++)
            {
                var outcome = outcomes[i];
                if (outcome == null)
                {
                    issues.Add($"Interaction '{interactionId}' has null outcome at index {i}.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(outcome.id))
                    issues.Add($"Interaction '{interactionId}' has outcome[{i}] with empty id.");

                if (outcome.probability < 0f || outcome.probability > 1f)
                    issues.Add($"Interaction '{interactionId}' outcome '{outcome.id}' has probability outside [0,1].");
                totalProbability += Mathf.Max(0f, outcome.probability);
            }

            if (totalProbability > 1.001f)
                issues.Add($"Interaction '{interactionId}' outcome probabilities sum above 1.0.");
        }

        static void ValidateExpiry(string interactionId, InteractionExpiry expiry, List<string> issues)
        {
            if (expiry == null)
                return;
            var type = (expiry.type ?? string.Empty).Trim().ToLowerInvariant();
            if (type == "ttl_hours" && expiry.ttlHours <= 0f)
                issues.Add($"Interaction '{interactionId}' uses ttl_hours but ttlHours <= 0.");
        }
    }

    public sealed class InteractionDefinitionRegistry
    {
        readonly string _seedFilePath;
        readonly string _runtimeRootDirectory;
        readonly string _runtimeFilePath;
        readonly InteractionDefinitionValidator _validator = new InteractionDefinitionValidator();

        public InteractionDefinitionRegistry(string seedFilePath = null, string runtimeRootDirectory = null)
        {
            _seedFilePath = string.IsNullOrWhiteSpace(seedFilePath)
                ? Path.Combine(Application.streamingAssetsPath, "Dialogue", "interactions.seed.json")
                : seedFilePath;
            _runtimeRootDirectory = string.IsNullOrWhiteSpace(runtimeRootDirectory)
                ? Path.Combine(Application.persistentDataPath, "RpgVillageInteractions")
                : runtimeRootDirectory;
            _runtimeFilePath = Path.Combine(_runtimeRootDirectory, "interactions_runtime.json");
        }

        public InteractionDefinitionsDoc LoadEffective(out List<string> issues)
        {
            issues = new List<string>();
            var seed = LoadSeedOrFallback();
            var runtime = LoadRuntime();
            var merged = Merge(seed, runtime);
            issues.AddRange(_validator.Validate(merged));
            return merged;
        }

        public InteractionDefinitionsDoc LoadRuntime()
        {
            if (!File.Exists(_runtimeFilePath))
                return new InteractionDefinitionsDoc();
            try
            {
                var parsed = JsonConvert.DeserializeObject<InteractionDefinitionsDoc>(File.ReadAllText(_runtimeFilePath));
                return parsed ?? new InteractionDefinitionsDoc();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InteractionDefinitionRegistry] Failed to load runtime definitions: {ex.Message}");
                return new InteractionDefinitionsDoc();
            }
        }

        public void SaveRuntime(InteractionDefinitionsDoc runtimeDoc)
        {
            if (runtimeDoc == null)
                return;
            try
            {
                Directory.CreateDirectory(_runtimeRootDirectory);
                File.WriteAllText(_runtimeFilePath, JsonConvert.SerializeObject(runtimeDoc, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InteractionDefinitionRegistry] Failed to save runtime definitions: {ex.Message}");
            }
        }

        public bool TryAddOrUpdateProposed(InteractionDefinition candidate, out string error)
        {
            error = string.Empty;
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.id))
            {
                error = "candidate_missing_id";
                return false;
            }

            var runtime = LoadRuntime() ?? new InteractionDefinitionsDoc();
            runtime.interactions ??= new List<InteractionDefinition>();
            candidate.status = "proposed";
            var id = candidate.id.Trim();
            var replaced = false;
            for (var i = 0; i < runtime.interactions.Count; i++)
            {
                var existing = runtime.interactions[i];
                if (existing == null || string.IsNullOrWhiteSpace(existing.id))
                    continue;
                if (!string.Equals(existing.id.Trim(), id, StringComparison.OrdinalIgnoreCase))
                    continue;
                runtime.interactions[i] = candidate;
                replaced = true;
                break;
            }

            if (!replaced)
                runtime.interactions.Add(candidate);

            var effective = Merge(LoadSeedOrFallback(), runtime);
            var issues = _validator.Validate(effective);
            if (issues.Count > 0)
            {
                error = string.Join(" | ", issues);
                return false;
            }

            SaveRuntime(runtime);
            return true;
        }

        public bool TryPromoteProposedToActive(string interactionId, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(interactionId))
            {
                error = "missing_id";
                return false;
            }

            var runtime = LoadRuntime() ?? new InteractionDefinitionsDoc();
            runtime.interactions ??= new List<InteractionDefinition>();
            var id = interactionId.Trim();
            InteractionDefinition match = null;
            for (var i = 0; i < runtime.interactions.Count; i++)
            {
                var item = runtime.interactions[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                if (string.Equals(item.id.Trim(), id, StringComparison.OrdinalIgnoreCase))
                {
                    match = item;
                    break;
                }
            }

            if (match == null)
            {
                error = "not_in_runtime";
                return false;
            }

            if (!string.Equals(match.status, "proposed", StringComparison.OrdinalIgnoreCase))
            {
                error = "not_proposed";
                return false;
            }

            match.status = "active";
            var effective = Merge(LoadSeedOrFallback(), runtime);
            var issues = _validator.Validate(effective);
            if (issues.Count > 0)
            {
                error = string.Join(" | ", issues);
                match.status = "proposed";
                return false;
            }

            SaveRuntime(runtime);
            return true;
        }

        static InteractionDefinitionsDoc Merge(InteractionDefinitionsDoc seed, InteractionDefinitionsDoc runtime)
        {
            var merged = seed != null
                ? JsonConvert.DeserializeObject<InteractionDefinitionsDoc>(JsonConvert.SerializeObject(seed))
                : new InteractionDefinitionsDoc();
            if (merged == null)
                merged = new InteractionDefinitionsDoc();
            merged.atomicActionTypes ??= new List<string>();
            merged.interactions ??= new List<InteractionDefinition>();

            var byId = new Dictionary<string, InteractionDefinition>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < merged.interactions.Count; i++)
            {
                var item = merged.interactions[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                byId[item.id.Trim()] = item;
            }

            var runtimeInteractions = runtime != null && runtime.interactions != null
                ? runtime.interactions
                : new List<InteractionDefinition>();
            for (var i = 0; i < runtimeInteractions.Count; i++)
            {
                var item = runtimeInteractions[i];
                if (item == null || string.IsNullOrWhiteSpace(item.id))
                    continue;
                var id = item.id.Trim();
                byId[id] = item;
            }

            merged.interactions = new List<InteractionDefinition>(byId.Values);
            return merged;
        }

        InteractionDefinitionsDoc LoadSeedOrFallback()
        {
            if (!File.Exists(_seedFilePath))
                return BuildFallbackSeed();

            try
            {
                var parsed = JsonConvert.DeserializeObject<InteractionDefinitionsDoc>(File.ReadAllText(_seedFilePath));
                return parsed ?? BuildFallbackSeed();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InteractionDefinitionRegistry] Failed to parse seed definitions: {ex.Message}");
                return BuildFallbackSeed();
            }
        }

        static InteractionDefinitionsDoc BuildFallbackSeed()
        {
            return new InteractionDefinitionsDoc
            {
                schemaVersion = 1,
                atomicActionTypes = new List<string>
                {
                    InteractionActionTypes.MoveToLocation,
                    InteractionActionTypes.MoveToNpc,
                    InteractionActionTypes.MoveToHero,
                    InteractionActionTypes.EngageDialogue,
                    InteractionActionTypes.ExchangeItem,
                    InteractionActionTypes.ExchangeCoins
                },
                interactions = new List<InteractionDefinition>()
            };
        }
    }
}
