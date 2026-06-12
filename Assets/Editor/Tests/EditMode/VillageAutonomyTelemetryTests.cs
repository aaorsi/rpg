using NUnit.Framework;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class VillageAutonomyTelemetryTests
    {
        [Test]
        public void Snapshot_ComputesFallbackRateAndPlanOutcomeCounts()
        {
            var telemetry = new VillageAutonomyTelemetry();
            telemetry.RecordDeliberationCall();
            telemetry.RecordDeliberationCall();
            telemetry.RecordDeliberationCall();
            telemetry.RecordFallback();
            telemetry.RecordPlanCompletion(true);
            telemetry.RecordPlanCompletion(false);
            telemetry.RecordPlanCompletion(true);

            var snapshot = telemetry.Snapshot();

            Assert.AreEqual(3, snapshot.DeliberationCalls);
            Assert.AreEqual(1, snapshot.FallbackCalls);
            Assert.AreEqual(2, snapshot.PlanCompletionsSucceeded);
            Assert.AreEqual(1, snapshot.PlanCompletionsFailed);
            Assert.AreEqual(3, snapshot.PlanCompletionsTotal);
            Assert.AreEqual(1f / 3f, snapshot.FallbackRate, 0.0001f);
        }

        [Test]
        public void Snapshot_WithoutDeliberations_HasZeroFallbackRate()
        {
            var snapshot = new VillageAutonomyTelemetry().Snapshot();

            Assert.AreEqual(0, snapshot.DeliberationCalls);
            Assert.AreEqual(0, snapshot.FallbackCalls);
            Assert.AreEqual(0f, snapshot.FallbackRate);
        }
    }
}
