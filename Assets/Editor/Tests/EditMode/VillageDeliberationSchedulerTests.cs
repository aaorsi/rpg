using NUnit.Framework;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class VillageDeliberationSchedulerTests
    {
        [Test]
        public void TryAcquire_UsesCadenceAndRoundRobinOrder()
        {
            var scheduler = new VillageDeliberationScheduler(2f);
            scheduler.SetParticipants(new[] { "villager_a", "villager_b", "villager_c" });

            Assert.IsTrue(scheduler.TryAcquire(0f, out var first, out var firstReason));
            Assert.AreEqual("villager_a", first);
            Assert.AreEqual("round_robin", firstReason);

            Assert.IsFalse(scheduler.TryAcquire(1.5f, out _, out _));

            Assert.IsTrue(scheduler.TryAcquire(2.01f, out var second, out _));
            Assert.AreEqual("villager_b", second);

            Assert.IsTrue(scheduler.TryAcquire(4.05f, out var third, out _));
            Assert.AreEqual("villager_c", third);

            Assert.IsTrue(scheduler.TryAcquire(6.1f, out var fourth, out _));
            Assert.AreEqual("villager_a", fourth);
        }

        [Test]
        public void RequestImmediate_QueuesPriorityWithoutBreakingCadence()
        {
            var scheduler = new VillageDeliberationScheduler(1f);
            scheduler.SetParticipants(new[] { "villager_a", "villager_b" });

            Assert.IsTrue(scheduler.TryAcquire(0f, out var first, out _));
            Assert.AreEqual("villager_a", first);

            scheduler.RequestImmediate("villager_b", "player_seen");
            Assert.IsFalse(scheduler.TryAcquire(0.25f, out _, out _));

            Assert.IsTrue(scheduler.TryAcquire(1.02f, out var second, out var reason));
            Assert.AreEqual("villager_b", second);
            Assert.AreEqual("player_seen", reason);

            Assert.IsTrue(scheduler.TryAcquire(2.1f, out var third, out _));
            Assert.AreEqual("villager_b", third);
        }

        [Test]
        public void SetParticipants_PreservesNextRoundRobinNpcAcrossRefresh()
        {
            var scheduler = new VillageDeliberationScheduler(1f);
            scheduler.SetParticipants(new[] { "villager_a", "villager_b", "villager_c" });

            Assert.IsTrue(scheduler.TryAcquire(0f, out var first, out _));
            Assert.AreEqual("villager_a", first);

            Assert.IsTrue(scheduler.TryAcquire(1.1f, out var second, out _));
            Assert.AreEqual("villager_b", second);

            scheduler.SetParticipants(new[] { "villager_a", "villager_b", "villager_c", "villager_d" });

            Assert.IsTrue(scheduler.TryAcquire(2.2f, out var third, out _));
            Assert.AreEqual("villager_c", third);
        }

        [Test]
        public void RequestImmediate_DeduplicatesNpcAndIgnoresUnknownIds()
        {
            var scheduler = new VillageDeliberationScheduler(1f);
            scheduler.SetParticipants(new[] { "villager_a", "villager_b" });

            Assert.IsTrue(scheduler.TryAcquire(0f, out var first, out _));
            Assert.AreEqual("villager_a", first);

            scheduler.RequestImmediate("villager_b", "combat");
            scheduler.RequestImmediate("villager_b", "duplicate_should_not_queue");
            scheduler.RequestImmediate("villager_unknown", "ignored");

            Assert.IsTrue(scheduler.TryAcquire(1.1f, out var priority, out var reason));
            Assert.AreEqual("villager_b", priority);
            Assert.AreEqual("combat", reason);

            Assert.IsTrue(scheduler.TryAcquire(2.2f, out var roundRobin, out var roundRobinReason));
            Assert.AreEqual("villager_b", roundRobin);
            Assert.AreEqual("round_robin", roundRobinReason);
        }
    }
}
