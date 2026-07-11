using System;
using System.Collections.Generic;
using Rpg.Dialogue;

namespace Rpg.Npc
{
    public static class VillageAutonomyDebugFormatter
    {
        public static string BuildPersonaSummary(NpcPersona persona)
        {
            if (persona == null)
                return "persona unavailable";

            var personality = Clean(persona.personality);
            var goalsCount = persona.goals != null ? persona.goals.Count : 0;
            var capabilitiesCount = persona.capabilities != null ? persona.capabilities.Count : 0;
            return $"personality: {personality}; goals: {goalsCount}; capabilities: {capabilitiesCount}";
        }

        public static string BuildGoalAndPlanLine(VillageAgentSimulation.VillagerRuntimeState state)
        {
            if (state == null)
                return "goal: unknown | step: unknown";

            var goal = state.ActiveGoals != null && state.ActiveGoals.Count > 0
                ? Clean(state.ActiveGoals[0])
                : "none";

            var step = "idle";
            var controller = state.Controller;
            if (controller != null && !string.IsNullOrWhiteSpace(controller.CurrentPrimitiveType))
            {
                step = controller.CurrentPrimitiveType.Trim();
            }
            else if (state.ActivePlan != null && state.ActivePlan.Count > 0)
            {
                var nextStep = state.ActivePlan[0];
                if (nextStep != null && !string.IsNullOrWhiteSpace(nextStep.PrimitiveType))
                    step = $"queued:{nextStep.PrimitiveType.Trim()}";
            }

            return $"goal: {goal} | step: {step}";
        }

        public static string BuildAgreementStatusLine(IReadOnlyList<string> activeAgreements)
        {
            if (activeAgreements == null || activeAgreements.Count <= 0)
                return "agreements: none";

            var first = Clean(activeAgreements[0]);
            if (activeAgreements.Count == 1)
                return $"agreements: {first}";

            return $"agreements: {first} (+{activeAgreements.Count - 1} more)";
        }

        public static string BuildOpinionSnapshotLine(VillageOpinionSummary summary)
        {
            return
                $"opinion: hero={summary.OpinionTowardHero:0.0}, leadership={summary.AggregateLeadership:0.0}, piety={summary.AggregatePiety:0.0}, wealth={summary.AggregateWealth:0.0}, helpfulness={summary.AggregateHelpfulness:0.0}";
        }

        public static IReadOnlyList<string> BuildInteractionDebugLines(
            InteractionRuntimeInstance instance,
            Func<string, string> resolveDisplayName,
            Func<string, int> resolveCoinBalance)
        {
            var lines = new List<string>();
            if (instance == null)
            {
                lines.Add("interaction: (none)");
                return lines;
            }

            var initiator = FormatParticipant(instance.actorNpcId, resolveDisplayName, resolveCoinBalance);
            var target = FormatParticipant(instance.targetNpcId, resolveDisplayName, resolveCoinBalance);
            var activity = string.IsNullOrWhiteSpace(instance.interactionDisplayName)
                ? instance.interactionId
                : instance.interactionDisplayName.Trim();
            var goal = string.IsNullOrWhiteSpace(instance.interactionGoal)
                ? InteractionEffectResolver.ResolveInteractionGoalFromId(instance.interactionId)
                : instance.interactionGoal.Trim();

            lines.Add($"initiator: {initiator}");
            lines.Add($"activity: {activity}");
            lines.Add($"target: {target}");
            lines.Add($"goal: {goal}");
            lines.Add($"status: {instance.status} ({instance.statusReason})");

            if (!string.IsNullOrWhiteSpace(instance.currentActionType))
            {
                var actor = resolveDisplayName != null ? resolveDisplayName(instance.currentActionActorId) : instance.currentActionActorId;
                var actionTarget = resolveDisplayName != null ? resolveDisplayName(instance.currentActionTargetId) : instance.currentActionTargetId;
                lines.Add($"current step: {instance.phase} / {instance.currentActionType} ({actor} -> {actionTarget})");
            }

            if (!string.IsNullOrWhiteSpace(instance.assignedErrand))
                lines.Add($"errand: {instance.assignedErrand.Trim()}");

            if (instance.stepLog != null && instance.stepLog.Count > 0)
            {
                var recent = instance.stepLog[instance.stepLog.Count - 1];
                lines.Add($"last effect: {recent}");
                if (instance.stepLog.Count > 1)
                    lines.Add($"effects: {string.Join(" | ", instance.stepLog)}");
            }

            if (instance.status != InteractionRuntimeStatus.Running)
            {
                var outcome = string.IsNullOrWhiteSpace(instance.outcomeSummary)
                    ? (string.IsNullOrWhiteSpace(instance.resolvedOutcomeId) ? "(pending)" : instance.resolvedOutcomeId)
                    : instance.outcomeSummary.Trim();
                lines.Add($"outcome: {outcome}");
                if (instance.targetIsFollower)
                    lines.Add("flag: target is follower");
            }
            else
            {
                lines.Add("outcome: (in progress)");
            }

            return lines;
        }

        static string FormatParticipant(
            string actorId,
            Func<string, string> resolveDisplayName,
            Func<string, int> resolveCoinBalance)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return "(none)";
            var id = actorId.Trim();
            var name = resolveDisplayName != null ? resolveDisplayName(id) : id;
            var coins = resolveCoinBalance != null ? resolveCoinBalance(id) : 0;
            if (string.Equals(id, InventoryService.HeroActorId, StringComparison.OrdinalIgnoreCase))
                return $"{name} [hero] ({coins} coins)";
            return $"{name} ({id}, {coins} coins)";
        }

        static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "n/a" : value.Trim();
        }
    }
}
