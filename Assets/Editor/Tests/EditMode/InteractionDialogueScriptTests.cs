using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class InteractionDialogueScriptTests
    {
        [Test]
        public void ResolveBackgroundBeatLine_ProducesDeterministicLineWithoutLlm()
        {
            var instance = new InteractionRuntimeInstance
            {
                interactionId = "romantic_relationship",
                interactionDisplayName = "Romance"
            };
            var step = new InteractionActionStep
            {
                parameters = new Dictionary<string, string> { { "topic", "flirt" } }
            };

            var line = InteractionDialogueScript.ResolveBackgroundBeatLine(
                instance,
                "Mira",
                "Jon",
                "start",
                step);

            StringAssert.Contains("Mira", line);
            StringAssert.Contains("Jon", line);
            StringAssert.Contains("Romance", line);
        }

        [Test]
        public void ResolveBackgroundBeatLine_StealBeat_IsSilent()
        {
            var instance = new InteractionRuntimeInstance { interactionId = "steal" };
            var line = InteractionDialogueScript.ResolveBackgroundBeatLine(
                instance,
                "Thief",
                "Victim",
                "loop",
                new InteractionActionStep());

            Assert.IsTrue(string.IsNullOrWhiteSpace(line));
        }
    }
}
