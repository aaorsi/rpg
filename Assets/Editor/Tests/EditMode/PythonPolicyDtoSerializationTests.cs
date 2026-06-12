using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class PythonPolicyDtoSerializationTests
    {
        [Test]
        public void DialogueTurnRequestDto_RoundTripsPersonaAndAutonomyContext()
        {
            var dto = new PythonDialogueTurnRequestDto
            {
                requestId = "req_1",
                model = "llama3.2",
                npc = new PythonNpcContextDto
                {
                    npcId = "merchant_1",
                    personality = "cautious",
                    socialTraits = new Dictionary<string, string> { { "helpfulness", "medium" } },
                    goals = new List<string> { "protect trade routes" },
                    capabilities = new List<string> { "dialogue", "trade" },
                    activePlanContext = "Executing goto_location; remaining_steps=2.",
                    activeGoalsContext = "merchant_1 goals: protect trade routes"
                },
                turn = new PythonTurnContextDto
                {
                    latestPlayerLine = "hello"
                }
            };

            var json = JsonConvert.SerializeObject(dto);
            var parsed = JsonConvert.DeserializeObject<PythonDialogueTurnRequestDto>(json);

            Assert.NotNull(parsed);
            Assert.NotNull(parsed.npc);
            Assert.AreEqual("cautious", parsed.npc.personality);
            Assert.AreEqual("medium", parsed.npc.socialTraits["helpfulness"]);
            Assert.AreEqual("protect trade routes", parsed.npc.goals[0]);
            Assert.AreEqual("trade", parsed.npc.capabilities[1]);
            Assert.AreEqual("Executing goto_location; remaining_steps=2.", parsed.npc.activePlanContext);
            Assert.AreEqual("merchant_1 goals: protect trade routes", parsed.npc.activeGoalsContext);
        }

        [Test]
        public void DialogueTurnRequestDto_MissingAutonomyContext_StaysBackwardCompatible()
        {
            const string json = "{\"requestId\":\"req_2\",\"model\":\"llama3.2\",\"npc\":{\"npcId\":\"merchant_2\"},\"turn\":{\"latestPlayerLine\":\"hi\"}}";
            var parsed = JsonConvert.DeserializeObject<PythonDialogueTurnRequestDto>(json);

            Assert.NotNull(parsed);
            Assert.NotNull(parsed.npc);
            Assert.AreEqual("merchant_2", parsed.npc.npcId);
            Assert.IsTrue(string.IsNullOrEmpty(parsed.npc.personality));
            Assert.IsNotNull(parsed.npc.socialTraits);
            Assert.IsNotNull(parsed.npc.goals);
            Assert.IsNotNull(parsed.npc.capabilities);
            Assert.IsTrue(string.IsNullOrEmpty(parsed.npc.activePlanContext));
            Assert.IsTrue(string.IsNullOrEmpty(parsed.npc.activeGoalsContext));
        }

        [Test]
        public void DialogueTurnResponseDto_SerializesSocialOutcomes()
        {
            var dto = new PythonDialogueTurnResponseDto
            {
                say = "Done.",
                socialOutcomes = new List<PythonSocialOutcomeDto>
                {
                    new PythonSocialOutcomeDto
                    {
                        outcomeType = "offer_task",
                        taskId = "fix_wagon",
                        notes = "Repair before dusk"
                    }
                }
            };

            var json = JsonConvert.SerializeObject(dto);
            var parsed = JsonConvert.DeserializeObject<PythonDialogueTurnResponseDto>(json);

            Assert.NotNull(parsed);
            Assert.NotNull(parsed.socialOutcomes);
            Assert.AreEqual(1, parsed.socialOutcomes.Count);
            Assert.AreEqual("offer_task", parsed.socialOutcomes[0].outcomeType);
            Assert.AreEqual("fix_wagon", parsed.socialOutcomes[0].taskId);
        }

        [Test]
        public void DialogueTurnResponseDto_MissingSocialOutcomes_StaysBackwardCompatible()
        {
            const string json = "{\"say\":\"hello\"}";
            var parsed = JsonConvert.DeserializeObject<PythonDialogueTurnResponseDto>(json);

            Assert.NotNull(parsed);
            Assert.NotNull(parsed.socialOutcomes);
            Assert.AreEqual(0, parsed.socialOutcomes.Count);
        }
    }
}
