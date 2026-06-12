using System.Collections.Generic;

namespace Rpg.Dialogue
{
    /// <summary>Pure dialogue turn commit helpers extracted from <see cref="DialogueManager"/> for EditMode testing.</summary>
    public static class DialogueTurnCommitLogic
    {
        public const int FailForwardRejectThreshold = 3;
        public const string FailForwardDefaultSay = "I still have doubts, but here is one useful lead you can follow.";

        public static float InteractionOutcomeWillingnessDelta(string normalizedOutcome)
        {
            if (string.IsNullOrWhiteSpace(normalizedOutcome))
                return 0f;
            if (normalizedOutcome == "cooperate")
                return 0.15f;
            if (normalizedOutcome == "reject")
                return -0.2f;
            if (normalizedOutcome == "counter_offer")
                return -0.1f;
            return 0f;
        }

        /// <summary>
        /// Tracks consecutive reject/defer outcomes and escalates to partial cooperation after the threshold.
        /// Returns true when fail-forward escalation was applied to the payload.
        /// </summary>
        public static bool ApplyOutcomeTelemetryAndFailForward(
            string npcId,
            AssistantModelPayload payload,
            Dictionary<string, int> consecutiveRejectByNpc)
        {
            if (string.IsNullOrWhiteSpace(npcId) || payload == null || consecutiveRejectByNpc == null)
                return false;

            var outcome = string.IsNullOrWhiteSpace(payload.InteractionOutcome)
                ? "unspecified"
                : payload.InteractionOutcome.Trim().ToLowerInvariant();
            DialogueTelemetry.Log("DialogueOutcome", $"npc={npcId}, outcome={outcome}");

            if (outcome == "reject" || outcome == "defer")
            {
                consecutiveRejectByNpc.TryGetValue(npcId, out var c);
                c++;
                consecutiveRejectByNpc[npcId] = c;
                if (c < FailForwardRejectThreshold)
                    return false;

                payload.InteractionOutcome = "partial";
                if (string.IsNullOrWhiteSpace(payload.Say))
                    payload.Say = FailForwardDefaultSay;
                DialogueTelemetry.Log("FailForwardEscalation", $"npc={npcId}, rejects={c}");
                return true;
            }

            consecutiveRejectByNpc[npcId] = 0;
            return false;
        }
    }
}
