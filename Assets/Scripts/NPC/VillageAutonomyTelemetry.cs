using UnityEngine;

namespace Rpg.Npc
{
    public sealed class VillageAutonomyTelemetry
    {
        int _deliberationCalls;
        int _fallbackCalls;
        int _planCompletionsSucceeded;
        int _planCompletionsFailed;

        public void RecordDeliberationCall()
        {
            _deliberationCalls++;
        }

        public void RecordFallback()
        {
            _fallbackCalls++;
        }

        public void RecordPlanCompletion(bool succeeded)
        {
            if (succeeded)
                _planCompletionsSucceeded++;
            else
                _planCompletionsFailed++;
        }

        public VillageAutonomyTelemetrySnapshot Snapshot()
        {
            var deliberationCalls = Mathf.Max(0, _deliberationCalls);
            var fallbackCalls = Mathf.Max(0, _fallbackCalls);
            var completionsSucceeded = Mathf.Max(0, _planCompletionsSucceeded);
            var completionsFailed = Mathf.Max(0, _planCompletionsFailed);
            return new VillageAutonomyTelemetrySnapshot(
                deliberationCalls,
                fallbackCalls,
                completionsSucceeded,
                completionsFailed);
        }
    }

    public readonly struct VillageAutonomyTelemetrySnapshot
    {
        public VillageAutonomyTelemetrySnapshot(
            int deliberationCalls,
            int fallbackCalls,
            int planCompletionsSucceeded,
            int planCompletionsFailed)
        {
            DeliberationCalls = Mathf.Max(0, deliberationCalls);
            FallbackCalls = Mathf.Max(0, fallbackCalls);
            PlanCompletionsSucceeded = Mathf.Max(0, planCompletionsSucceeded);
            PlanCompletionsFailed = Mathf.Max(0, planCompletionsFailed);
        }

        public int DeliberationCalls { get; }
        public int FallbackCalls { get; }
        public int PlanCompletionsSucceeded { get; }
        public int PlanCompletionsFailed { get; }
        public int PlanCompletionsTotal => PlanCompletionsSucceeded + PlanCompletionsFailed;
        public float FallbackRate => DeliberationCalls <= 0
            ? 0f
            : (float)FallbackCalls / DeliberationCalls;
    }
}
