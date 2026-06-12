using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class FailForwardServiceTests
    {
        FailForwardService _service;

        [SetUp]
        public void SetUp() => _service = new FailForwardService();

        [Test]
        public void BuildEscalationSignals_ThreeStalledLockedMilestones_EmitsHint()
        {
            var milestones = new List<MilestoneStateEntry>
            {
                new MilestoneStateEntry { milestoneId = "find_key", status = MilestoneStatus.locked }
            };

            _service.NoteTurnWithoutProgress(milestones);
            _service.NoteTurnWithoutProgress(milestones);
            _service.NoteTurnWithoutProgress(milestones);

            var signals = _service.BuildEscalationSignals(milestones);

            Assert.Contains("hint:find_key", signals);
        }

        [Test]
        public void BuildEscalationSignals_FiveStalledLockedMilestones_EmitsUnlock()
        {
            var milestones = new List<MilestoneStateEntry>
            {
                new MilestoneStateEntry { milestoneId = "find_key", status = MilestoneStatus.locked }
            };

            for (var i = 0; i < 5; i++)
                _service.NoteTurnWithoutProgress(milestones);

            var signals = _service.BuildEscalationSignals(milestones);

            Assert.Contains("hint:find_key", signals);
            Assert.Contains("unlock:find_key", signals);
        }

        [Test]
        public void BuildEscalationSignals_CompletedMilestone_IsIgnored()
        {
            var milestones = new List<MilestoneStateEntry>
            {
                new MilestoneStateEntry { milestoneId = "done", status = MilestoneStatus.completed }
            };

            for (var i = 0; i < 5; i++)
                _service.NoteTurnWithoutProgress(milestones);

            var signals = _service.BuildEscalationSignals(milestones);

            Assert.IsEmpty(signals);
        }
    }
}
