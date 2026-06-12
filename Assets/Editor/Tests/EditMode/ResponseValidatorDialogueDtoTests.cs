using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class ResponseValidatorDialogueDtoTests
    {
        [Test]
        public void BuildPayloadFromDialogueDto_NullDto_ReturnsEmptyPayload()
        {
            var payload = ResponseValidator.BuildPayloadFromDialogueDto(null);

            Assert.AreEqual(string.Empty, payload.Say);
            Assert.IsFalse(payload.AckYear);
            Assert.IsEmpty(payload.ProposedActions);
            Assert.IsEmpty(payload.SocialOutcomes);
            Assert.IsEmpty(payload.MemoryAdds);
        }

        [Test]
        public void BuildPayloadFromDialogueDto_FullDto_MapsAllFields()
        {
            var dto = new PythonDialogueTurnResponseDto
            {
                say = "Meet me at the warehouse.",
                ackYear = true,
                interactionOutcome = "Cooperate",
                proposedActions = new List<NpcProposedAction>
                {
                    new NpcProposedAction { ActionType = "guide_to_location", TargetId = "warehouse", Quantity = 1f }
                },
                socialOutcomes = new List<PythonSocialOutcomeDto>
                {
                    new PythonSocialOutcomeDto { outcomeType = "payment", amount = 4f, currency = "gold" },
                    new PythonSocialOutcomeDto { outcomeType = "unknown_type", notes = "should be ignored" }
                },
                milestoneSignals = new List<string> { "hint:find_key" },
                stateDeltas = new Dictionary<string, string> { { "trust", "high" } },
                memoriesToAdd = new List<Dictionary<string, string>>
                {
                    new Dictionary<string, string>
                    {
                        { "kind", "fact" },
                        { "summary", "Hero asked about the ghoul." },
                        { "subjectCharacterId", "player" }
                    }
                }
            };

            var payload = ResponseValidator.BuildPayloadFromDialogueDto(dto);

            Assert.AreEqual("Meet me at the warehouse.", payload.Say);
            Assert.IsTrue(payload.AckYear);
            Assert.AreEqual("cooperate", payload.InteractionOutcome);
            Assert.AreEqual(1, payload.ProposedActions.Count);
            Assert.AreEqual("move_to_location", payload.ProposedActions[0].ActionType);
            Assert.AreEqual("warehouse", payload.ProposedActions[0].TargetId);
            Assert.AreEqual(1, payload.SocialOutcomes.Count);
            Assert.AreEqual("payment", payload.SocialOutcomes[0].OutcomeType);
            Assert.AreEqual(4f, payload.SocialOutcomes[0].Amount);
            Assert.AreEqual("gold", payload.SocialOutcomes[0].Currency);
            Assert.Contains("hint:find_key", payload.MilestoneSignals);
            Assert.AreEqual("high", payload.StateDeltas["trust"]);
            Assert.AreEqual(1, payload.MemoryAdds.Count);
            Assert.AreEqual("fact", payload.MemoryAdds[0].Kind);
            Assert.AreEqual("Hero asked about the ghoul.", payload.MemoryAdds[0].Summary);
            Assert.AreEqual("player", payload.MemoryAdds[0].SubjectCharacterId);
        }
    }
}
