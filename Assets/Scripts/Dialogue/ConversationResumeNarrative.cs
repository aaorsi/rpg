using System;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>Builds player-facing and LLM-facing text when reopening a saved NPC transcript.</summary>
    public static class ConversationResumeNarrative
    {
        public static bool TryBuild(NpcTranscriptSnapshot snap, out string systemPromptInset, out string uiStatusLine)
        {
            systemPromptInset = null;
            uiStatusLine = null;
            if (snap == null || snap.Messages == null || snap.Messages.Count == 0)
                return false;

            if (string.IsNullOrWhiteSpace(snap.LastConversationEndedUtc))
            {
                systemPromptInset =
                    "CONVERSATION_BREAK:\n" +
                    "The dialogue was resumed from a saved transcript, but no end timestamp was recorded (older save). " +
                    "Treat this as the traveler returning after an unspecified pause — not as one uninterrupted moment.";
                uiStatusLine = "Resuming a saved chat (no exact pause time was recorded).";
                return true;
            }

            if (!DateTime.TryParse(
                    snap.LastConversationEndedUtc,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var endedUtc))
            {
                endedUtc = DateTime.UtcNow;
            }

            endedUtc = DateTime.SpecifyKind(endedUtc, DateTimeKind.Utc);
            var wall = DateTime.UtcNow - endedUtc;
            if (wall < TimeSpan.Zero)
                wall = TimeSpan.Zero;

            var wallPhrase = DescribeApproximate(wall);
            var samePlayInstance = !string.IsNullOrEmpty(snap.DialoguePlayInstanceId) &&
                string.Equals(snap.DialoguePlayInstanceId, DialogueRuntimeSession.PlayInstanceId, StringComparison.Ordinal);

            if (samePlayInstance && snap.LastConversationEndedGameplayTime.HasValue)
            {
                var gt = Time.time - snap.LastConversationEndedGameplayTime.Value;
                if (gt < 0f)
                    gt = 0f;
                var gameSpan = TimeSpan.FromSeconds(gt);
                var gamePhrase = DescribeApproximate(gameSpan);

                systemPromptInset =
                    "CONVERSATION_BREAK:\n" +
                    "The in-game dialogue with this traveler was closed and later reopened **without restarting this play session**. " +
                    $"About {gamePhrase} of **gameplay time** passed (Unity Time.time since load; respects timeScale). " +
                    $"About {wallPhrase} passed on the **real clock** since the last line was exchanged. " +
                    "Acknowledge the gap naturally — do not roleplay as if you were mid-sentence without interruption.";

                uiStatusLine =
                    "This conversation had ended. Roughly " + gamePhrase + " of gameplay time passed, and " + wallPhrase +
                    " on the real-world clock, before you returned (same play session).";
            }
            else
            {
                systemPromptInset =
                    "CONVERSATION_BREAK:\n" +
                    "The dialogue with this traveler ended earlier (the game may have been stopped or reloaded since then). " +
                    $"About {wallPhrase} of **real time** has passed since your last exchange (UTC clock). " +
                    "Welcome them back as after a real break, not as a seamless continuation of the same beat.";

                uiStatusLine =
                    "This conversation had ended earlier. Roughly " + wallPhrase +
                    " of real-world time passed since then (new play session or app restart).";
            }

            return true;
        }

        static string DescribeApproximate(TimeSpan ts)
        {
            if (ts.TotalSeconds < 20)
                return "only a few seconds";
            if (ts.TotalSeconds < 90)
                return "about a minute";
            if (ts.TotalMinutes < 45)
                return $"about {Math.Max(1, (int)Math.Round(ts.TotalMinutes))} minutes";
            if (ts.TotalHours < 36)
                return $"about {Math.Max(1, (int)Math.Round(ts.TotalHours))} hours";
            return $"about {Math.Max(1, (int)Math.Round(ts.TotalDays))} days";
        }
    }
}
