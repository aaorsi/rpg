using System;
using System.Collections.Generic;
using Rpg.Dialogue;

namespace Rpg.Npc
{
    /// <summary>
    /// Writes durable NPC memories when village interactions start or finish so dialogue can reference them later.
    /// </summary>
    public sealed class NpcInteractionMemoryService
    {
        readonly NpcMemoryRepository _memory;

        public NpcInteractionMemoryService(NpcMemoryRepository memory)
        {
            _memory = memory;
        }

        public void RecordInteractionStarted(
            InteractionRuntimeInstance instance,
            InteractionDefinition definition,
            Func<string, string> resolveDisplayName)
        {
            if (instance == null || _memory == null)
                return;

            var interactionId = Normalize(instance.interactionId);
            var interactionLabel = ResolveInteractionLabel(instance, definition);
            foreach (var npcId in CollectParticipantIds(instance))
            {
                var lines = BuildStartedMemories(npcId, instance, interactionId, interactionLabel, resolveDisplayName);
                AppendHeroParticipantMemories(lines, npcId, instance, interactionLabel);
                AppendMemories(npcId, lines);
            }
        }

        public void RecordInteractionCompleted(
            InteractionRuntimeInstance instance,
            InteractionDefinition definition,
            string outcomeId,
            Func<string, string> resolveDisplayName)
        {
            if (instance == null || _memory == null)
                return;

            var interactionId = Normalize(instance.interactionId);
            var interactionLabel = ResolveInteractionLabel(instance, definition);
            var resolvedOutcome = string.IsNullOrWhiteSpace(outcomeId) ? "completed" : outcomeId.Trim();
            foreach (var npcId in CollectParticipantIds(instance))
            {
                var lines = BuildCompletedMemories(
                    npcId,
                    instance,
                    interactionId,
                    interactionLabel,
                    resolvedOutcome,
                    resolveDisplayName);
                AppendHeroParticipantMemories(lines, npcId, instance, interactionLabel);
                AppendMemories(npcId, lines);
            }
        }

        static void AppendHeroParticipantMemories(
            List<InteractionMemoryLine> lines,
            string selfNpcId,
            InteractionRuntimeInstance instance,
            string interactionLabel)
        {
            if (lines == null || instance == null || string.IsNullOrWhiteSpace(selfNpcId))
                return;
            if (IsHeroId(instance.actorNpcId) && string.Equals(selfNpcId, instance.targetNpcId, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new InteractionMemoryLine(
                    "interaction",
                    $"The traveler initiated {interactionLabel} with me.",
                    "player"));
            }
            else if (IsHeroId(instance.targetNpcId) && string.Equals(selfNpcId, instance.actorNpcId, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add(new InteractionMemoryLine(
                    "interaction",
                    $"I initiated {interactionLabel} involving the traveler.",
                    "player"));
            }
        }

        static bool IsHeroId(string npcId) =>
            string.Equals(npcId?.Trim(), InventoryService.HeroActorId, StringComparison.OrdinalIgnoreCase);

        public static string ResolveWeightedOutcomeId(InteractionDefinition definition)
        {
            if (definition?.outcomes == null || definition.outcomes.Count == 0)
                return "completed";

            var total = 0f;
            for (var i = 0; i < definition.outcomes.Count; i++)
            {
                var outcome = definition.outcomes[i];
                if (outcome == null)
                    continue;
                total += UnityEngine.Mathf.Max(0f, outcome.probability);
            }

            if (total <= 0f)
                return definition.outcomes[0]?.id ?? "completed";

            var roll = UnityEngine.Random.value * total;
            for (var i = 0; i < definition.outcomes.Count; i++)
            {
                var outcome = definition.outcomes[i];
                if (outcome == null)
                    continue;
                roll -= UnityEngine.Mathf.Max(0f, outcome.probability);
                if (roll <= 0f)
                    return string.IsNullOrWhiteSpace(outcome.id) ? "completed" : outcome.id.Trim();
            }

            var last = definition.outcomes[definition.outcomes.Count - 1];
            return last == null || string.IsNullOrWhiteSpace(last.id) ? "completed" : last.id.Trim();
        }

        static List<string> CollectParticipantIds(InteractionRuntimeInstance instance)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddParticipant(ids, seen, instance?.actorNpcId);
            AddParticipant(ids, seen, instance?.targetNpcId);
            if (instance?.extraParticipantNpcIds != null)
            {
                for (var i = 0; i < instance.extraParticipantNpcIds.Count; i++)
                    AddParticipant(ids, seen, instance.extraParticipantNpcIds[i]);
            }

            return ids;
        }

        static void AddParticipant(List<string> ids, HashSet<string> seen, string npcId)
        {
            if (ids == null || seen == null || string.IsNullOrWhiteSpace(npcId))
                return;
            if (string.Equals(npcId.Trim(), InventoryService.HeroActorId, StringComparison.OrdinalIgnoreCase))
                return;
            var trimmed = npcId.Trim();
            if (!seen.Add(trimmed))
                return;
            ids.Add(trimmed);
        }

        void AppendMemories(string npcId, IReadOnlyList<InteractionMemoryLine> lines)
        {
            if (string.IsNullOrWhiteSpace(npcId) || lines == null || lines.Count == 0)
                return;

            var candidates = new List<NpcMemoryCandidate>(lines.Count);
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null || string.IsNullOrWhiteSpace(line.Summary))
                    continue;
                candidates.Add(new NpcMemoryCandidate(
                    string.IsNullOrWhiteSpace(line.Kind) ? "interaction" : line.Kind.Trim(),
                    line.Summary.Trim(),
                    string.IsNullOrWhiteSpace(line.SubjectCharacterId) ? "player" : line.SubjectCharacterId.Trim()));
            }

            if (candidates.Count > 0)
                _memory.TryAppendCandidates(npcId.Trim(), candidates);
        }

        static List<InteractionMemoryLine> BuildStartedMemories(
            string selfNpcId,
            InteractionRuntimeInstance instance,
            string interactionId,
            string interactionLabel,
            Func<string, string> resolveDisplayName)
        {
            var actorId = instance.actorNpcId ?? string.Empty;
            var targetId = instance.targetNpcId ?? string.Empty;
            var actorName = resolveDisplayName != null ? resolveDisplayName(actorId) : actorId;
            var targetName = resolveDisplayName != null ? resolveDisplayName(targetId) : targetId;
            var lines = new List<InteractionMemoryLine>();

            switch (interactionId)
            {
                case "romantic_relationship":
                    if (string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"I have been spending romantic time with {targetName}.",
                            targetId));
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"I am developing a romantic relationship with {targetName}.",
                            "player"));
                    }
                    else if (string.Equals(selfNpcId, targetId, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"{actorName} has been courting me and we may be falling in love.",
                            actorId));
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"There is a budding romance between me and {actorName}.",
                            "player"));
                    }
                    break;
                case "start_cult":
                case "cult_conversion":
                    if (string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"I am recruiting villagers into a new belief circle.",
                            "player"));
                    }
                    else
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"{actorName} is trying to persuade me to join a new cult-like belief.",
                            actorId));
                    }
                    break;
                case "elect_mayor":
                    if (string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            "I called a village meeting to convince others that the traveler should become mayor.",
                            "player"));
                    }
                    else
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"We are debating whether the traveler should be elected mayor. {actorName} is leading the pitch.",
                            "player"));
                    }
                    break;
                default:
                    lines.Add(new InteractionMemoryLine(
                        "interaction",
                        $"I was recently involved in {interactionLabel} with {OtherParticipantName(selfNpcId, actorId, targetId, actorName, targetName)}.",
                        "player"));
                    break;
            }

            return lines;
        }

        static List<InteractionMemoryLine> BuildCompletedMemories(
            string selfNpcId,
            InteractionRuntimeInstance instance,
            string interactionId,
            string interactionLabel,
            string outcomeId,
            Func<string, string> resolveDisplayName)
        {
            var actorId = instance.actorNpcId ?? string.Empty;
            var targetId = instance.targetNpcId ?? string.Empty;
            var actorName = resolveDisplayName != null ? resolveDisplayName(actorId) : actorId;
            var targetName = resolveDisplayName != null ? resolveDisplayName(targetId) : targetId;
            var lines = new List<InteractionMemoryLine>();

            switch (interactionId)
            {
                case "romantic_relationship":
                    if (string.Equals(outcomeId, "mutual_bond", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add(new InteractionMemoryLine(
                                "interaction",
                                $"I am in a romantic relationship with {targetName}.",
                                targetId));
                            lines.Add(new InteractionMemoryLine(
                                "interaction",
                                $"I am in love with {targetName} and would tell the traveler about it if asked.",
                                "player"));
                        }
                        else if (string.Equals(selfNpcId, targetId, StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add(new InteractionMemoryLine(
                                "interaction",
                                $"I accepted a romantic bond with {actorName}.",
                                actorId));
                            lines.Add(new InteractionMemoryLine(
                                "interaction",
                                $"I am in love with {actorName} and would tell the traveler about it if asked.",
                                "player"));
                        }
                    }
                    else if (string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            $"My romantic advance toward {targetName} was rejected.",
                            targetId));
                    }
                    break;
                case "start_cult":
                case "cult_conversion":
                    if (string.Equals(outcomeId, "cult_forms", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(outcomeId, "converted", StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase)
                                ? "A new cult-like following has formed around my leadership."
                                : $"I now belong to the belief circle led by {actorName}.",
                            string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase) ? "player" : actorId));
                    }
                    else
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase)
                                ? "My attempt to form a cult failed to gain traction."
                                : $"I resisted {actorName}'s cult recruitment pitch.",
                            string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase) ? "player" : actorId));
                    }
                    break;
                case "elect_mayor":
                    if (string.Equals(outcomeId, "mayor_elected", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(outcomeId, "hero_chosen", StringComparison.OrdinalIgnoreCase))
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            "The village agreed that the traveler should become mayor.",
                            "player"));
                    }
                    else
                    {
                        lines.Add(new InteractionMemoryLine(
                            "interaction",
                            "The village meeting about electing the traveler as mayor ended without consensus.",
                            "player"));
                    }
                    break;
                default:
                    lines.Add(new InteractionMemoryLine(
                        "interaction",
                        $"The {interactionLabel} interaction ended ({outcomeId}).",
                        "player"));
                    break;
            }

            return lines;
        }

        static string OtherParticipantName(
            string selfNpcId,
            string actorId,
            string targetId,
            string actorName,
            string targetName)
        {
            if (string.Equals(selfNpcId, actorId, StringComparison.OrdinalIgnoreCase))
                return targetName;
            if (string.Equals(selfNpcId, targetId, StringComparison.OrdinalIgnoreCase))
                return actorName;
            return !string.IsNullOrWhiteSpace(targetName) ? targetName : actorName;
        }

        static string ResolveInteractionLabel(InteractionRuntimeInstance instance, InteractionDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(instance?.interactionDisplayName))
                return instance.interactionDisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(definition?.displayName))
                return definition.displayName.Trim();
            return string.IsNullOrWhiteSpace(instance?.interactionId) ? "an interaction" : instance.interactionId.Trim();
        }

        static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

        sealed class InteractionMemoryLine
        {
            public InteractionMemoryLine(string kind, string summary, string subjectCharacterId)
            {
                Kind = kind;
                Summary = summary;
                SubjectCharacterId = subjectCharacterId;
            }

            public string Kind { get; }
            public string Summary { get; }
            public string SubjectCharacterId { get; }
        }
    }
}
