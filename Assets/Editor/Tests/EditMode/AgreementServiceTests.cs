using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class AgreementServiceTests
    {
        string _agreementPath;
        string _inventoryPath;

        [SetUp]
        public void SetUp()
        {
            _agreementPath = Path.Combine(Path.GetTempPath(), $"agreement-service-{Guid.NewGuid():N}.json");
            _inventoryPath = Path.Combine(Path.GetTempPath(), $"agreement-inventory-{Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_agreementPath))
                File.Delete(_agreementPath);
            if (File.Exists(_inventoryPath))
                File.Delete(_inventoryPath);
        }

        [Test]
        public void Lifecycle_HireAgreement_CompletesAndPersists_WithPayout()
        {
            var agreements = NewAgreementService();
            var inventory = NewInventoryService();
            inventory.AddItem("npc_blacksmith", InventoryService.CoinItemId, 20);

            var created = agreements.CreateOffer(
                AgreementType.Hire,
                "npc_blacksmith",
                payerActorId: "npc_blacksmith",
                payeeActorId: InventoryService.HeroActorId,
                payoutCoins: 8,
                summary: "Escort contract");

            Assert.IsTrue(agreements.TryAccept(created.agreementId));
            Assert.IsTrue(agreements.TryStart(created.agreementId));
            Assert.IsTrue(agreements.TryComplete(created.agreementId, inventory, out var completionError), completionError);

            var reloaded = new AgreementService(_agreementPath);
            var persisted = reloaded.Snapshot().Single();
            Assert.AreEqual("completed", persisted.state);
            Assert.IsTrue(inventory.HasAtLeast("npc_blacksmith", InventoryService.CoinItemId, 12));
            Assert.IsTrue(inventory.HasAtLeast(InventoryService.HeroActorId, InventoryService.CoinItemId, 8));
        }

        [Test]
        public void Lifecycle_RejectsInvalidTransitions()
        {
            var agreements = NewAgreementService();
            var inventory = NewInventoryService();
            var created = agreements.CreateOffer(
                AgreementType.Advice,
                "npc_sage",
                payerActorId: "npc_sage",
                payeeActorId: InventoryService.HeroActorId,
                payoutCoins: 2,
                summary: "Advice for ruins");

            Assert.IsFalse(agreements.TryStart(created.agreementId));
            Assert.IsFalse(agreements.TryComplete(created.agreementId, inventory, out var completionError));
            Assert.AreEqual("transition_not_allowed", completionError);

            var record = agreements.Snapshot().Single();
            Assert.AreEqual("offered", record.state);
        }

        [Test]
        public void Lifecycle_CompletionFails_WhenPayoutTransferFails()
        {
            var agreements = NewAgreementService();
            var inventory = NewInventoryService();
            var created = agreements.CreateOffer(
                AgreementType.Persuasion,
                "npc_guard",
                payerActorId: "npc_guard",
                payeeActorId: InventoryService.HeroActorId,
                payoutCoins: 5,
                summary: "Persuasion contract");
            agreements.TryAccept(created.agreementId);
            agreements.TryStart(created.agreementId);

            var ok = agreements.TryComplete(created.agreementId, inventory, out var completionError);

            Assert.IsFalse(ok);
            Assert.AreEqual("payout_transfer_failed", completionError);
            Assert.AreEqual("in_progress", agreements.Snapshot().Single().state);
        }

        [Test]
        public void Adapter_AppliesSignalDrivenCompletion_AndTransfersPayout()
        {
            var agreements = NewAgreementService();
            var inventory = NewInventoryService();
            inventory.AddItem("npc_mentor", InventoryService.CoinItemId, 10);
            var payload = new AssistantModelPayload();
            payload.MilestoneSignals.Add("agreement:hire:completed");
            payload.StateDeltas["hire_contract_coins"] = "4";

            AgreementOutcomeAdapter.ApplyFromDialoguePayload(agreements, inventory, "npc_mentor", payload);

            var agreement = agreements.Snapshot().Single();
            Assert.AreEqual("hire", agreement.contractType);
            Assert.AreEqual("completed", agreement.state);
            Assert.IsTrue(inventory.HasAtLeast(InventoryService.HeroActorId, InventoryService.CoinItemId, 4));
        }

        [Test]
        public void Adapter_UsesCooperateAndRejectOutcomes_ToAdvanceOrFailOffer()
        {
            var agreements = NewAgreementService();
            agreements.CreateOffer(
                AgreementType.Advice,
                "npc_scholar",
                payerActorId: "npc_scholar",
                payeeActorId: InventoryService.HeroActorId,
                payoutCoins: 0,
                summary: "Advice contract");
            var payload = new AssistantModelPayload { InteractionOutcome = "cooperate" };

            AgreementOutcomeAdapter.ApplyFromDialoguePayload(agreements, null, "npc_scholar", payload);
            Assert.AreEqual("accepted", agreements.Snapshot().Single().state);

            payload.InteractionOutcome = "reject";
            AgreementOutcomeAdapter.ApplyFromDialoguePayload(agreements, null, "npc_scholar", payload);
            Assert.AreEqual("failed", agreements.Snapshot().Single().state);
        }

        AgreementService NewAgreementService() => new AgreementService(_agreementPath);

        InventoryService NewInventoryService()
        {
            var library = new NarrativeContentLibrary(Application.streamingAssetsPath);
            return new InventoryService(library, _inventoryPath);
        }
    }
}
