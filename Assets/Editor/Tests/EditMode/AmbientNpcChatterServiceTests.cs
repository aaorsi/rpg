using NUnit.Framework;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class AmbientNpcChatterServiceTests
    {
        [Test]
        public void TryBuildEvent_UsesPersonaAndOpinionContextForDeterministicBark()
        {
            var service = new AmbientNpcChatterService();
            service.SetParticipants(new[] { "villager_a", "villager_b" });

            var first = service.TryBuildEvent(
                0f,
                "villager_a",
                "villager_b",
                "Alda",
                "Bran",
                "Warm and helpful miller",
                "Stern guard",
                new[] { "Protect the market road" },
                45f,
                1f,
                1f,
                0,
                out var firstEvent);

            Assert.IsTrue(first);
            StringAssert.Contains("Alda to Bran:", firstEvent.Text);
            StringAssert.Contains("hero", firstEvent.Text.ToLowerInvariant());

            var serviceAgain = new AmbientNpcChatterService();
            serviceAgain.SetParticipants(new[] { "villager_a", "villager_b" });
            var second = serviceAgain.TryBuildEvent(
                0f,
                "villager_a",
                "villager_b",
                "Alda",
                "Bran",
                "Warm and helpful miller",
                "Stern guard",
                new[] { "Protect the market road" },
                45f,
                1f,
                1f,
                0,
                out var secondEvent);

            Assert.IsTrue(second);
            Assert.AreEqual(firstEvent.Text, secondEvent.Text);
        }

        [Test]
        public void TryBuildEvent_RespectsSpeakerAndPairCooldowns()
        {
            var service = new AmbientNpcChatterService();
            service.SetParticipants(new[] { "villager_a", "villager_b", "villager_c" });

            Assert.IsTrue(service.TryBuildEvent(
                1f,
                "villager_a",
                "villager_b",
                "A",
                "B",
                "neutral",
                "neutral",
                null,
                0f,
                4f,
                10f,
                0,
                out _));

            Assert.IsFalse(service.TryBuildEvent(
                2f,
                "villager_a",
                "villager_c",
                "A",
                "C",
                "neutral",
                "neutral",
                null,
                0f,
                4f,
                10f,
                0,
                out _));

            Assert.IsFalse(service.TryBuildEvent(
                3f,
                "villager_b",
                "villager_a",
                "B",
                "A",
                "neutral",
                "neutral",
                null,
                0f,
                4f,
                10f,
                0,
                out _));

            Assert.IsTrue(service.TryBuildEvent(
                11.2f,
                "villager_b",
                "villager_a",
                "B",
                "A",
                "neutral",
                "neutral",
                null,
                0f,
                4f,
                10f,
                0,
                out _));
        }
    }
}
