using NUnit.Framework;
using Rpg.GameState;
using UnityEngine;

namespace Rpg.GameState.Tests.EditMode
{
    [TestFixture]
    public class GameClockServiceTests
    {
        GameObject go;
        GameClockService clock;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("ClockTestObject");
            clock = go.AddComponent<GameClockService>();
            clock.AutoAdvance = false;
            clock.GameSecondsPerRealSecond = 600f;
            clock.SetClockState(2, 6f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AdvanceByRealSeconds_ProgressesAndRollsDayDeterministically()
        {
            clock.AdvanceByRealSeconds(60f);
            var afterFirst = clock.GetSnapshot();
            Assert.AreEqual(2, afterFirst.CurrentDay);
            Assert.AreEqual(16f, afterFirst.Hour24, 0.0001f);
            Assert.AreEqual(TimeOfDaySegment.Afternoon, afterFirst.Segment);

            clock.AdvanceByRealSeconds(60f);
            var afterSecond = clock.GetSnapshot();
            Assert.AreEqual(3, afterSecond.CurrentDay);
            Assert.AreEqual(2f, afterSecond.Hour24, 0.0001f);
            Assert.AreEqual(TimeOfDaySegment.Night, afterSecond.Segment);
        }

        [Test]
        public void AdvanceByGameSeconds_DoesNotDependOnFrameTiming()
        {
            clock.SetClockState(1, 0f);
            clock.AdvanceByGameSeconds(5400f);
            var snapshot = clock.GetSnapshot();

            Assert.AreEqual(1, snapshot.CurrentDay);
            Assert.AreEqual(1.5f, snapshot.Hour24, 0.0001f);
            Assert.AreEqual(TimeOfDaySegment.Night, snapshot.Segment);
        }

        [TestCase(4.99f, TimeOfDaySegment.Night)]
        [TestCase(5f, TimeOfDaySegment.Dawn)]
        [TestCase(7.99f, TimeOfDaySegment.Dawn)]
        [TestCase(8f, TimeOfDaySegment.Morning)]
        [TestCase(11.99f, TimeOfDaySegment.Morning)]
        [TestCase(12f, TimeOfDaySegment.Afternoon)]
        [TestCase(16.99f, TimeOfDaySegment.Afternoon)]
        [TestCase(17f, TimeOfDaySegment.Evening)]
        [TestCase(20.99f, TimeOfDaySegment.Evening)]
        [TestCase(21f, TimeOfDaySegment.Night)]
        public void ResolveSegment_BoundaryHours_ReturnExpectedSegment(float hour, TimeOfDaySegment expected)
        {
            Assert.AreEqual(expected, GameClockService.ResolveSegment(hour));
        }

        [Test]
        public void WorldStateSnapshot_WithClock_EmitsPromptFacts()
        {
            var world = go.AddComponent<WorldStateService>();
            world.SetCurrentYearForTests(3001);
            world.SetGameClockForTests(clock);

            clock.SetClockState(4, 17.5f);
            var snapshot = world.GetSnapshot();
            var facts = snapshot.ToFactsBlock();

            Assert.IsTrue(snapshot.HasClockData);
            Assert.AreEqual(4, snapshot.Clock.CurrentDay);
            StringAssert.Contains("CURRENT_YEAR: 3001", facts);
            StringAssert.Contains("CURRENT_DAY: 4", facts);
            StringAssert.Contains("CURRENT_HOUR_24: 17.50", facts);
            StringAssert.Contains("CURRENT_TIME_SEGMENT: EVENING", facts);
        }
    }
}
