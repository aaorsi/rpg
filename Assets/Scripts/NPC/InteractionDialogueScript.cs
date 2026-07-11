using System;

namespace Rpg.Npc
{
    public static class InteractionDialogueScript
    {
        public static string ResolveBeatInstruction(
            InteractionRuntimeInstance instance,
            InteractionActionStep step,
            string phase)
        {
            var topic = ResolveTopic(step, instance);
            var interactionId = instance?.interactionId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(topic))
            {
                switch (topic)
                {
                    case "flirt":
                        return "Open with a warm flirtatious line — show romantic interest, not generic small talk.";
                    case "bonding":
                        return "Deepen trust — share something personal or invite closeness; stay romantic.";
                    case "recruitment_pitch":
                    case "conversion_pitch":
                        return "Pitch your cause with conviction — explain what followers gain and ask for commitment.";
                    case "belief_affirmation":
                    case "belief_debate":
                        return "Respond to the pitch — show doubt, curiosity, or growing belief; stay on the cult topic.";
                    case "covert_offer":
                        return "Quietly offer coins for a discreet favor — make the bribe sound practical, not threatening.";
                    case "errand_instructions":
                        return "State the paid errand clearly: where to go, what to fetch, and who to deliver it to.";
                    case "service_offer":
                        return "Offer a concrete service you can perform and hint at your price.";
                    case "service_progress":
                        return "Report progress on the service you are performing — stay specific.";
                    case "mayor_pitch":
                        return "Argue why the traveler should become mayor — give one concrete village reason.";
                    case "mayor_debate":
                        return "Weigh the mayor idea — agree, disagree, or ask a pointed question about leadership.";
                }
            }

            if (string.Equals(interactionId, "steal", StringComparison.OrdinalIgnoreCase))
                return "Do not speak — this interaction has no dialogue beat here.";
            if (string.Equals(interactionId, "bribe", StringComparison.OrdinalIgnoreCase)
                && string.Equals(phase, "start", StringComparison.OrdinalIgnoreCase))
                return "Negotiate the bribe — propose payment in exchange for an errand.";

            return $"Advance the {instance?.interactionDisplayName ?? interactionId} scene with one focused line.";
        }

        public static string ResolveBackgroundBeatLine(
            InteractionRuntimeInstance instance,
            string speakerName,
            string targetName,
            string phase,
            InteractionActionStep step)
        {
            var beat = ResolveBeatInstruction(instance, step, phase);
            if (beat.IndexOf("Do not speak", StringComparison.OrdinalIgnoreCase) >= 0)
                return string.Empty;

            var interactionLabel = string.IsNullOrWhiteSpace(instance?.interactionDisplayName)
                ? instance?.interactionId ?? "interaction"
                : instance.interactionDisplayName.Trim();
            var phaseLabel = string.IsNullOrWhiteSpace(phase) ? "scene" : phase.Trim();
            return $"{speakerName} and {targetName} continue {interactionLabel} ({phaseLabel}).";
        }

        public static string BuildDialoguePrompt(
            InteractionRuntimeInstance instance,
            string speakerName,
            string targetName,
            string topic,
            string phase,
            InteractionActionStep step)
        {
            var goal = string.IsNullOrWhiteSpace(instance?.interactionGoal)
                ? InteractionEffectResolver.ResolveInteractionGoalFromId(instance?.interactionId)
                : instance.interactionGoal.Trim();
            var beat = ResolveBeatInstruction(instance, step, phase);
            var interactionLabel = string.IsNullOrWhiteSpace(instance?.interactionDisplayName)
                ? instance?.interactionId ?? "interaction"
                : instance.interactionDisplayName.Trim();
            var errand = string.IsNullOrWhiteSpace(instance?.assignedErrand)
                ? string.Empty
                : $" ERRAND_CONTEXT: {instance.assignedErrand.Trim()}";

            return
                $"[VILLAGE_INTERACTION id={instance?.interactionId}; type={interactionLabel}; phase={phase}; topic={topic}. " +
                $"INTERACTION_GOAL: {goal}. " +
                $"DIALOGUE_BEAT: {beat}. " +
                $"You are {speakerName} speaking to {targetName}.{errand} " +
                "Deliver exactly one short in-character line that advances this beat toward the goal. " +
                "Do not mention unrelated topics, other villagers, or meta-game language.]";
        }

        static string ResolveTopic(InteractionActionStep step, InteractionRuntimeInstance instance)
        {
            if (step?.parameters != null && step.parameters.TryGetValue("topic", out var topic) && !string.IsNullOrWhiteSpace(topic))
                return topic.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(instance?.interactionDisplayName))
                return instance.interactionDisplayName.Trim().ToLowerInvariant();
            return instance?.interactionId ?? string.Empty;
        }
    }
}
