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

        public static string BuildGroupAskLine(VillageGroupAskRecord ask)
        {
            if (ask == null)
                return "ask: unavailable";

            var askId = Clean(ask.askId);
            var title = Clean(ask.title);
            var state = Clean(ask.state);
            return $"ask: {askId} ({state}) - {title}";
        }

        static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "n/a" : value.Trim();
        }
    }
}
