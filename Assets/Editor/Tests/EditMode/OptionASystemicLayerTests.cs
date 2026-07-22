using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Rpg.Dialogue;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class OptionASystemicLayerTests
    {
        string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"rpg-option-a-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(_tempDir, "Dialogue"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort.
            }
        }

        [Test]
        public void SystemicEventResolver_ChatProximity_ProducesRumorWithForcedRoll()
        {
            WriteSystemicEvents(new
            {
                schemaVersion = 1,
                chatProximityRollChance = 1.0,
                pairCooldownSeconds = 5f,
                events = new[]
                {
                    new
                    {
                        id = "test_gossip",
                        trigger = "chat_proximity",
                        weight = 1f,
                        rumorTemplate = "{actor} chatted with {target}."
                    }
                }
            });

            var opinion = new VillageOpinionService();
            var resolver = new VillageSystemicEventResolver(_tempDir);
            var rng = new Random(42);
            var consequence = resolver.TryResolveChatProximityEvent(
                "npc_a",
                "npc_b",
                "Alice",
                "Bob",
                opinion,
                nowTime: 100f,
                random: rng);

            Assert.NotNull(consequence);
            Assert.AreEqual("test_gossip", consequence.eventId);
            StringAssert.Contains("Alice", consequence.rumorText);
            StringAssert.Contains("Bob", consequence.rumorText);
        }

        [Test]
        public void RumorFeed_EnqueueAndSnapshot_KeepsRecentOrder()
        {
            var path = Path.Combine(_tempDir, "rumors.json");
            var feed = new VillageRumorFeed(path);
            feed.Enqueue(new VillageConsequenceEvent { rumorText = "First rumor." });
            feed.Enqueue(new VillageConsequenceEvent { rumorText = "Second rumor." });

            var recent = feed.SnapshotRecent(2);
            Assert.AreEqual(2, recent.Count);
            Assert.AreEqual("First rumor.", recent[0].rumorText);
            Assert.AreEqual("Second rumor.", recent[1].rumorText);
            StringAssert.Contains("Second rumor.", feed.BuildFactsBlock());
        }

        [Test]
        public void NarrativeGraph_ResolvesHighestPriorityActiveSection()
        {
            WriteNarrativeGraph(new
            {
                schemaVersion = 1,
                sections = new[]
                {
                    new
                    {
                        id = "sec_low",
                        milestoneId = "m_followers",
                        objective = "Low priority.",
                        priority = 1,
                        activeWhileStatus = new[] { "hinted" }
                    },
                    new
                    {
                        id = "sec_high",
                        milestoneId = "m_trade_chain",
                        objective = "High priority trade.",
                        priority = 10,
                        activeWhileStatus = new[] { "hinted", "unlocked" }
                    }
                }
            });

            var graph = new NarrativeGraphService(_tempDir);
            var milestones = new List<MilestoneStateEntry>
            {
                new MilestoneStateEntry { milestoneId = "m_trade_chain", status = MilestoneStatus.hinted },
                new MilestoneStateEntry { milestoneId = "m_followers", status = MilestoneStatus.hinted }
            };

            var section = graph.ResolveActiveSection(milestones);
            Assert.NotNull(section);
            Assert.AreEqual("sec_high", section.id);
            var block = graph.BuildSectionContextBlock(section, stallCount: 2);
            StringAssert.Contains("ACTIVE_NARRATIVE_SECTION: sec_high", block);
            StringAssert.Contains("STALL_COUNT: 2", block);
        }

        [Test]
        public void TurnAuthorizer_ThreeStalledTurns_ShouldEscalate()
        {
            var authorizer = new TurnAuthorizer();
            var section = new NarrativeGraphSection
            {
                id = "sec_test",
                redirectHints = new List<string> { "Hint A.", "Hint B.", "Hint C." }
            };
            var emptyPayload = new AssistantModelPayload { Say = "..." };

            authorizer.NoteTurnResult(section, emptyPayload);
            authorizer.NoteTurnResult(section, emptyPayload);
            authorizer.NoteTurnResult(section, emptyPayload);

            Assert.AreEqual(3, authorizer.GetStallCount(section));
            Assert.IsTrue(authorizer.ShouldEscalate(section));
        }

        [Test]
        public void TurnAuthorizer_ProgressResetsStallCount()
        {
            var authorizer = new TurnAuthorizer();
            var section = new NarrativeGraphSection { id = "sec_reset" };
            var stalled = new AssistantModelPayload { Say = "no progress" };
            var progressed = new AssistantModelPayload { Say = "done" };
            progressed.MilestoneSignals.Add("hint:m_trade_chain");

            authorizer.NoteTurnResult(section, stalled);
            authorizer.NoteTurnResult(section, stalled);
            Assert.AreEqual(2, authorizer.GetStallCount(section));

            authorizer.NoteTurnResult(section, progressed);
            Assert.AreEqual(0, authorizer.GetStallCount(section));
            Assert.IsFalse(authorizer.ShouldEscalate(section));
        }

        void WriteSystemicEvents(object doc)
        {
            var path = Path.Combine(_tempDir, "Dialogue", "village_systemic_events.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(doc, Formatting.Indented));
        }

        void WriteNarrativeGraph(object doc)
        {
            var path = Path.Combine(_tempDir, "Dialogue", "narrative_graph.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(doc, Formatting.Indented));
        }
    }
}
