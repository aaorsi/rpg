using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class InteractionEffectResolverTests
    {
        string _inventoryPath;

        [SetUp]
        public void SetUp()
        {
            _inventoryPath = Path.Combine(Path.GetTempPath(), $"interaction-effects-{System.Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_inventoryPath))
                File.Delete(_inventoryPath);
        }

        InventoryService NewInventory()
        {
            var library = new NarrativeContentLibrary(Application.streamingAssetsPath);
            return new InventoryService(library, _inventoryPath);
        }

        [Test]
        public void ApplyExchangeItem_StealRandomItem_TransfersFromTargetToInitiator()
        {
            var inventory = NewInventory();
            inventory.EnsureActor("npc_thief");
            inventory.EnsureActor("npc_victim");
            var known = inventory.GetAllKnownItemIds();
            Assert.Greater(known.Count, 0, "catalog must expose at least one item for steal test");
            inventory.AddItem("npc_victim", known[0], 1);

            var instance = new InteractionRuntimeInstance
            {
                interactionId = "steal",
                actorNpcId = "npc_thief",
                targetNpcId = "npc_victim"
            };
            var step = new InteractionActionStep
            {
                actionType = InteractionActionTypes.ExchangeItem,
                parameters = new Dictionary<string, string> { { "mode", "steal_random_item" } }
            };

            var result = InteractionEffectResolver.ApplyExchangeItem(
                inventory,
                instance,
                step,
                "npc_victim",
                "npc_thief");

            Assert.IsTrue(result.Success);
            StringAssert.Contains("stole", result.Summary);
            Assert.IsTrue(inventory.HasAtLeast("npc_thief", known[0], 1));
            Assert.IsFalse(inventory.HasAtLeast("npc_victim", known[0], 1));
        }

        [Test]
        public void ApplyExchangeCoins_BribeOffer_TransfersCoinsAndAssignsErrand()
        {
            var inventory = NewInventory();
            inventory.EnsureActor("npc_client");
            inventory.EnsureActor("npc_worker");
            inventory.AddItem("npc_client", InventoryService.CoinItemId, 20);

            var instance = new InteractionRuntimeInstance
            {
                instanceId = "test-instance",
                interactionId = "bribe",
                actorNpcId = "npc_client",
                targetNpcId = "npc_worker"
            };
            var step = new InteractionActionStep
            {
                actionType = InteractionActionTypes.ExchangeCoins,
                parameters = new Dictionary<string, string>
                {
                    { "mode", "bribe_offer" },
                    { "amount", "8" }
                }
            };
            var locations = new List<string> { "warehouse", "temple" };

            var result = InteractionEffectResolver.ApplyExchangeCoins(
                inventory,
                instance,
                new InteractionDefinition { id = "bribe" },
                step,
                "npc_client",
                "npc_worker",
                locations,
                null);

            Assert.IsTrue(result.Success);
            StringAssert.Contains("paid 8 coins", result.Summary);
            Assert.IsTrue(inventory.HasAtLeast("npc_worker", InventoryService.CoinItemId, 8));
            Assert.IsFalse(string.IsNullOrWhiteSpace(instance.assignedErrand));
            StringAssert.Contains("warehouse", instance.assignedErrand);
        }

        [Test]
        public void ResolveOutcomeId_StealFailure_BiasesDetected()
        {
            var definition = new InteractionDefinition
            {
                id = "steal",
                outcomes = new List<InteractionOutcome>
                {
                    new InteractionOutcome { id = "undetected", probability = 0.65f },
                    new InteractionOutcome { id = "detected", probability = 0.35f }
                }
            };
            var instance = new InteractionRuntimeInstance { interactionId = "steal" };
            instance.stepLog.Add("steal failed — target had nothing to take");

            var outcome = InteractionEffectResolver.ResolveOutcomeId(definition, instance);

            Assert.AreEqual("detected", outcome);
        }

        [Test]
        public void ResolveOutcomeId_BribeSuccess_BiasesAccepted()
        {
            var definition = new InteractionDefinition
            {
                id = "bribe",
                outcomes = new List<InteractionOutcome>
                {
                    new InteractionOutcome { id = "accepted", probability = 0.6f },
                    new InteractionOutcome { id = "rejected_or_exposed", probability = 0.4f }
                }
            };
            var instance = new InteractionRuntimeInstance { interactionId = "bribe" };
            instance.stepLog.Add("paid 8 coins — errand: fetch supplies");

            var outcome = InteractionEffectResolver.ResolveOutcomeId(definition, instance);

            Assert.AreEqual("accepted", outcome);
        }

        [Test]
        public void BuildInteractionDebugLines_IncludesGoalAndOutcome()
        {
            var instance = new InteractionRuntimeInstance
            {
                interactionId = "bribe",
                interactionDisplayName = "Bribe",
                interactionGoal = "Pay for an errand.",
                actorNpcId = "npc_a",
                targetNpcId = "npc_b",
                status = InteractionRuntimeStatus.Completed,
                outcomeSummary = "paid 8 coins — errand: fetch supplies"
            };
            instance.stepLog.Add("paid 8 coins — errand: fetch supplies");

            var lines = VillageAutonomyDebugFormatter.BuildInteractionDebugLines(
                instance,
                id => id,
                _ => 8);

            Assert.IsTrue(lines.Count >= 5);
            StringAssert.Contains("initiator:", lines[0]);
            StringAssert.Contains("goal:", lines[3]);
            StringAssert.Contains("outcome:", lines[lines.Count - 1]);
        }
    }
}
