using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class DialogueTurnCommitLogicTests
    {
        Dictionary<string, int> _rejectCounts;

        [SetUp]
        public void SetUp() => _rejectCounts = new Dictionary<string, int>();

        [Test]
        public void ApplyOutcomeTelemetryAndFailForward_FirstReject_IncrementsCounterWithoutEscalation()
        {
            var payload = new AssistantModelPayload { InteractionOutcome = "reject", Say = "No." };

            var escalated = DialogueTurnCommitLogic.ApplyOutcomeTelemetryAndFailForward("npc_a", payload, _rejectCounts);

            Assert.IsFalse(escalated);
            Assert.AreEqual(1, _rejectCounts["npc_a"]);
            Assert.AreEqual("reject", payload.InteractionOutcome);
            Assert.AreEqual("No.", payload.Say);
        }

        [Test]
        public void ApplyOutcomeTelemetryAndFailForward_ThirdReject_EscalatesToPartialWithDefaultSay()
        {
            _rejectCounts["npc_a"] = 2;
            var payload = new AssistantModelPayload { InteractionOutcome = "defer" };

            var escalated = DialogueTurnCommitLogic.ApplyOutcomeTelemetryAndFailForward("npc_a", payload, _rejectCounts);

            Assert.IsTrue(escalated);
            Assert.AreEqual(3, _rejectCounts["npc_a"]);
            Assert.AreEqual("partial", payload.InteractionOutcome);
            Assert.AreEqual(DialogueTurnCommitLogic.FailForwardDefaultSay, payload.Say);
        }

        [Test]
        public void ApplyOutcomeTelemetryAndFailForward_ThirdReject_PreservesExistingSay()
        {
            _rejectCounts["npc_a"] = 2;
            var payload = new AssistantModelPayload
            {
                InteractionOutcome = "reject",
                Say = "Fine, take this clue."
            };

            var escalated = DialogueTurnCommitLogic.ApplyOutcomeTelemetryAndFailForward("npc_a", payload, _rejectCounts);

            Assert.IsTrue(escalated);
            Assert.AreEqual("partial", payload.InteractionOutcome);
            Assert.AreEqual("Fine, take this clue.", payload.Say);
        }

        [Test]
        public void ApplyOutcomeTelemetryAndFailForward_CooperateOutcome_ResetsRejectCounter()
        {
            _rejectCounts["npc_a"] = 2;
            var payload = new AssistantModelPayload { InteractionOutcome = "cooperate" };

            var escalated = DialogueTurnCommitLogic.ApplyOutcomeTelemetryAndFailForward("npc_a", payload, _rejectCounts);

            Assert.IsFalse(escalated);
            Assert.AreEqual(0, _rejectCounts["npc_a"]);
        }

        [Test]
        public void ApplyOutcomeTelemetryAndFailForward_NullPayload_ReturnsFalse()
        {
            var escalated = DialogueTurnCommitLogic.ApplyOutcomeTelemetryAndFailForward("npc_a", null, _rejectCounts);

            Assert.IsFalse(escalated);
            Assert.IsEmpty(_rejectCounts);
        }

        [TestCase(null, 0f)]
        [TestCase("", 0f)]
        [TestCase("cooperate", 0.15f)]
        [TestCase("reject", -0.2f)]
        [TestCase("counter_offer", -0.1f)]
        [TestCase("partial", 0f)]
        public void InteractionOutcomeWillingnessDelta_KnownOutcomes_ReturnExpectedDelta(string outcome, float expected)
        {
            Assert.AreEqual(expected, DialogueTurnCommitLogic.InteractionOutcomeWillingnessDelta(outcome), 0.001f);
        }

        [Test]
        public void SuccessfulCommitResult_FromSidecarPayload_MapsDisplayFields()
        {
            var dto = new PythonDialogueTurnResponseDto
            {
                say = "Here is what I know.",
                ackYear = true,
                interactionOutcome = "cooperate",
                rawAssistant = "{\"say\":\"Here is what I know.\"}"
            };
            var payload = ResponseValidator.BuildPayloadFromDialogueDto(dto);
            var result = DialogueResult.FromModel(payload.Say, dto.rawAssistant, payload.AckYear, payload);

            Assert.IsNull(result.Error);
            Assert.AreEqual("Here is what I know.", result.DisplayText);
            Assert.AreEqual(dto.rawAssistant, result.RawModelText);
            Assert.IsTrue(result.AckYear);
            Assert.AreEqual("cooperate", result.Payload.InteractionOutcome);
        }
    }
}
