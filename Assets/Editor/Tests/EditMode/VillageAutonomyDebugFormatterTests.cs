using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Dialogue;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class VillageAutonomyDebugFormatterTests
    {
        [Test]
        public void BuildPersonaSummary_HandlesMissingPersona()
        {
            var summary = VillageAutonomyDebugFormatter.BuildPersonaSummary(null);

            Assert.AreEqual("persona unavailable", summary);
        }

        [Test]
        public void BuildGoalAndPlanLine_ShowsFirstGoalAndQueuedStep()
        {
            var state = new VillageAgentSimulation.VillagerRuntimeState("villager_01")
            {
                ActiveGoals = new List<string> { "Guard the square", "Trade with hero" },
                ActivePlan = new List<NpcPrimitiveStep>
                {
                    new NpcPrimitiveStep
                    {
                        PrimitiveType = NpcPrimitiveTypes.WaitAt
                    }
                }
            };

            var line = VillageAutonomyDebugFormatter.BuildGoalAndPlanLine(state);

            Assert.AreEqual("goal: Guard the square | step: queued:wait_at", line);
        }

        [Test]
        public void BuildAgreementStatusLine_ShowsCountForAdditionalItems()
        {
            var line = VillageAutonomyDebugFormatter.BuildAgreementStatusLine(new List<string>
            {
                "accepted:hire:Guard gate:coins=10",
                "offered:advice:Find herbs:coins=0"
            });

            Assert.AreEqual("agreements: accepted:hire:Guard gate:coins=10 (+1 more)", line);
        }

        [Test]
        public void BuildOpinionSnapshotLine_FormatsOpinionValues()
        {
            var line = VillageAutonomyDebugFormatter.BuildOpinionSnapshotLine(new VillageOpinionSummary
            {
                OpinionTowardHero = 12.34f,
                AggregateLeadership = -4.6f,
                AggregatePiety = 0.12f,
                AggregateWealth = 7.8f,
                AggregateHelpfulness = 9.9f
            });

            Assert.AreEqual("opinion: hero=12.3, leadership=-4.6, piety=0.1, wealth=7.8, helpfulness=9.9", line);
        }

        [Test]
        public void BuildGroupAskLine_FormatsAskSummary()
        {
            var line = VillageAutonomyDebugFormatter.BuildGroupAskLine(new VillageGroupAskRecord
            {
                askId = "ask_run_for_mayor",
                state = "offered",
                title = "Run for mayor"
            });

            Assert.AreEqual("ask: ask_run_for_mayor (offered) - Run for mayor", line);
        }
    }
}
