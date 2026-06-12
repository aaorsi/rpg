using NUnit.Framework;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class VillageOpinionServiceTests
    {
        [Test]
        public void ApplyHeroImpact_UpdatesNpcOpinionAndAggregateTracks()
        {
            var service = new VillageOpinionService();
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
            var service = new VillageOpinionService();
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
            var service = new VillageOpinionService();
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
            Assert.Greater(cAfterSecond, 0f);
        }
    }
}
