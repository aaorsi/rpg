using NUnit.Framework;
using Rpg.Npc;
using System;
using System.Collections.Generic;
using System.IO;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class VillageOpinionServiceTests
    {
        readonly List<string> _tempAskStatePaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _tempAskStatePaths.Count; i++)
            {
                var path = _tempAskStatePaths[i];
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch
                {
                    // Best-effort cleanup for test temp files.
                }
            }

            _tempAskStatePaths.Clear();
        }

        VillageOpinionService NewService()
        {
            var path = Path.Combine(Path.GetTempPath(), $"village_asks_{Guid.NewGuid():N}.json");
            _tempAskStatePaths.Add(path);
            return new VillageOpinionService(path);
        }

        [Test]
        public void ApplyHeroImpact_UpdatesNpcOpinionAndAggregateTracks()
        {
            var service = NewService();
            service.SetParticipants(new[] { "villager_a", "villager_b" });

            service.ApplyHeroImpact("villager_a", 20f, 10f, -5f, 4f, 14f);

            var summary = service.GetSummary("villager_a");
            Assert.AreEqual(20f, summary.OpinionTowardHero, 0.001f);
            Assert.AreEqual(5f, summary.AggregateLeadership, 0.001f);
            Assert.AreEqual(-2.5f, summary.AggregatePiety, 0.001f);
            Assert.AreEqual(2f, summary.AggregateWealth, 0.001f);
            Assert.AreEqual(7f, summary.AggregateHelpfulness, 0.001f);
        }

        [Test]
        public void ApplyHeroImpact_ClampsValuesWithinConfiguredBounds()
        {
            var service = NewService();
            service.SetParticipants(new[] { "villager_a" });

            service.ApplyHeroImpact("villager_a", 250f, 125f, -190f, 130f, -160f);
            var high = service.GetSummary("villager_a");
            Assert.AreEqual(VillageOpinionService.MaxOpinion, high.OpinionTowardHero, 0.001f);
            Assert.AreEqual(VillageOpinionService.MaxOpinion, high.AggregateLeadership, 0.001f);
            Assert.AreEqual(VillageOpinionService.MinOpinion, high.AggregatePiety, 0.001f);

            service.ApplyHeroImpact("villager_a", -400f, -300f, 260f, -250f, 400f);
            var low = service.GetSummary("villager_a");
            Assert.AreEqual(VillageOpinionService.MinOpinion, low.OpinionTowardHero, 0.001f);
            Assert.AreEqual(VillageOpinionService.MinOpinion, low.AggregateLeadership, 0.001f);
            Assert.AreEqual(VillageOpinionService.MaxOpinion, low.AggregatePiety, 0.001f);
            Assert.AreEqual(VillageOpinionService.MinOpinion, low.AggregateWealth, 0.001f);
            Assert.AreEqual(VillageOpinionService.MaxOpinion, low.AggregateHelpfulness, 0.001f);
        }

        [Test]
        public void ProcessGossip_RespectsBudgetAndPropagatesSentiment()
        {
            var service = NewService();
            service.SetParticipants(new[] { "villager_a", "villager_b", "villager_c" });
            service.ApplyHeroImpact("villager_a", 80f, 40f, 0f, 0f, 0f);
            service.ApplyHeroImpact("villager_b", -40f, -20f, 0f, 0f, 0f);
            service.ApplyHeroImpact("villager_c", 0f, 0f, 0f, 0f, 0f);

            service.QueueInteraction("villager_a", "villager_b");
            service.QueueInteraction("villager_b", "villager_c");
            Assert.AreEqual(2, service.PendingGossipCount);

            var processedFirst = service.ProcessGossip(1);
            Assert.AreEqual(1, processedFirst);
            Assert.AreEqual(1, service.PendingGossipCount);

            var bAfterFirst = service.GetSummary("villager_b").OpinionTowardHero;
            Assert.Greater(bAfterFirst, -40f);
            Assert.Less(bAfterFirst, 0f);

            var processedSecond = service.ProcessGossip(4);
            Assert.AreEqual(1, processedSecond);
            Assert.AreEqual(0, service.PendingGossipCount);

            var cAfterSecond = service.GetSummary("villager_c").OpinionTowardHero;
            Assert.Less(cAfterSecond, 0f);
            Assert.Greater(cAfterSecond, -40f);
        }

        [Test]
        public void QueueInteraction_DeduplicatesPairsUntilProcessed()
        {
            var service = NewService();
            service.SetParticipants(new[] { "villager_a", "villager_b", "villager_c" });

            service.QueueInteraction("villager_a", "villager_b");
            service.QueueInteraction("villager_b", "villager_a");
            service.QueueInteraction("villager_a", "villager_b");
            Assert.AreEqual(1, service.PendingGossipCount);

            Assert.AreEqual(1, service.ProcessGossip(1));
            Assert.AreEqual(0, service.PendingGossipCount);

            service.QueueInteraction("villager_b", "villager_a");
            Assert.AreEqual(1, service.PendingGossipCount);
        }

        [Test]
        public void BuildDeliberationContext_ReportsSignedTracksAndPendingCount()
        {
            var service = NewService();
            service.SetParticipants(new[] { "villager_a", "villager_b" });
            service.ApplyHeroImpact("villager_a", 12f, 8f, -4f, 2f, 0f);
            service.ApplyHeroImpact("villager_b", -2f, 0f, 6f, -2f, 2f);
            service.QueueInteraction("villager_a", "villager_b");

            var lines = service.BuildDeliberationContext("villager_a");

            Assert.GreaterOrEqual(lines.Count, 3);
            StringAssert.Contains("leadership +4.0", lines[0]);
            StringAssert.Contains("piety +1.0", lines[0]);
            StringAssert.Contains("Local opinion for villager_a toward hero: +12.0", lines[1]);
            StringAssert.Contains("Pending gossip interactions: 1.", lines[2]);
        }

        [Test]
        public void GroupAsks_TriggerFromThresholds_AndAppearInDeliberationContext()
        {
            var service = NewService();
            service.SetParticipants(new[] { "villager_a", "villager_b" });

            service.ApplyHeroImpact("villager_a", 0f, 90f, 0f, 0f, 40f);
            service.ApplyHeroImpact("villager_b", 0f, 0f, 0f, 0f, 0f);

            var asksAfterLeadership = service.SnapshotGroupAsks();
            Assert.IsTrue(asksAfterLeadership.Exists(a => string.Equals(a.askId, "ask_run_for_mayor", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(asksAfterLeadership.Exists(a => string.Equals(a.askId, "ask_religious_figure", StringComparison.OrdinalIgnoreCase)));

            service.ApplyHeroImpact("villager_a", 0f, 0f, 80f, 0f, 0f);
            var asksAfterPiety = service.SnapshotGroupAsks();
            Assert.IsTrue(asksAfterPiety.Exists(a => string.Equals(a.askId, "ask_religious_figure", StringComparison.OrdinalIgnoreCase)));

            var context = service.BuildDeliberationContext("villager_a");
            Assert.IsTrue(context.Exists(line => line.IndexOf("Group asks awaiting response", StringComparison.OrdinalIgnoreCase) >= 0));
            Assert.IsTrue(context.Exists(line => line.IndexOf("ask_run_for_mayor", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void GroupAsks_PersistAcceptState_AndEmitMilestoneSignals()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"village_asks_{Guid.NewGuid():N}.json");
            try
            {
                var service = new VillageOpinionService(tempPath);
                service.SetParticipants(new[] { "villager_a", "villager_b" });
                service.ApplyHeroImpact("villager_a", 0f, 90f, 0f, 0f, 50f);

                var beforeAccept = service.GetSummary("villager_a");
                Assert.IsTrue(service.TryRespondToGroupAsk("ask_run_for_mayor", true, "villager_a", out var acceptSignals));
                CollectionAssert.Contains(acceptSignals, "unlock:m_village_mayor_arc");

                var pendingSignals = service.ConsumePendingMilestoneSignals();
                CollectionAssert.Contains(pendingSignals, "hint:m_village_mayor_arc");
                CollectionAssert.Contains(pendingSignals, "unlock:m_village_mayor_arc");

                var afterAccept = service.GetSummary("villager_a");
                Assert.Greater(afterAccept.AggregateLeadership, beforeAccept.AggregateLeadership);

                service.ApplyHeroImpact("villager_a", 0f, 0f, 80f, 0f, 0f);
                var beforeDecline = service.GetSummary("villager_a");
                Assert.IsTrue(service.TryRespondToGroupAsk("ask_religious_figure", false, "villager_b", out var declineSignals));
                CollectionAssert.Contains(declineSignals, "hint:m_village_religious_declined");
                var afterDecline = service.GetSummary("villager_a");
                Assert.Less(afterDecline.AggregatePiety, beforeDecline.AggregatePiety);

                var reloaded = new VillageOpinionService(tempPath);
                var persisted = reloaded.SnapshotGroupAsks();
                var mayor = persisted.Find(a => string.Equals(a.askId, "ask_run_for_mayor", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(mayor);
                Assert.AreEqual("accepted", mayor.state);
                var religious = persisted.Find(a => string.Equals(a.askId, "ask_religious_figure", StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(religious);
                Assert.AreEqual("declined", religious.state);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
}
