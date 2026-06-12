using System;
using System.IO;
using NUnit.Framework;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class InventoryServiceCurrencyTests
    {
        string _inventoryPath;

        [SetUp]
        public void SetUp()
        {
            _inventoryPath = Path.Combine(Path.GetTempPath(), $"inventory-service-currency-{Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_inventoryPath))
                File.Delete(_inventoryPath);
        }

        [Test]
        public void CoinAliases_Canonicalize_ToCoinItemId()
        {
            var service = NewService();
            service.EnsureActor("npc_merchant");

            var added = service.TryAddItem("npc_merchant", "gold_coins", 12);

            Assert.IsTrue(added);
            Assert.IsTrue(service.HasAtLeast("npc_merchant", InventoryService.CoinItemId, 12));
            Assert.IsTrue(service.HasAtLeast("npc_merchant", "coins", 12));
            Assert.AreEqual("coins", service.GetItemDisplayName("gold"));
        }

        [Test]
        public void EnsureHeroStartingPurse_AddsMissingCoins_WithoutLoweringExistingBalance()
        {
            var service = NewService();

            service.EnsureHeroStartingPurse(10);
            Assert.IsTrue(service.HasAtLeast(InventoryService.HeroActorId, InventoryService.CoinItemId, 10));

            service.EnsureHeroStartingPurse(4);
            Assert.IsTrue(service.HasAtLeast(InventoryService.HeroActorId, InventoryService.CoinItemId, 10));
        }

        [Test]
        public void TryPayWage_Succeeds_WhenPayerHasEnoughCoins()
        {
            var service = NewService();
            service.AddItem("guild_master", InventoryService.CoinItemId, 20);
            service.EnsureActor("hero_worker");

            var ok = service.TryPayWage("guild_master", "hero_worker", 7);

            Assert.IsTrue(ok);
            Assert.IsTrue(service.HasAtLeast("guild_master", InventoryService.CoinItemId, 13));
            Assert.IsTrue(service.HasAtLeast("hero_worker", InventoryService.CoinItemId, 7));
        }

        [Test]
        public void TryPayWage_Fails_WhenPayerLacksFunds()
        {
            var service = NewService();
            service.AddItem("guild_master", InventoryService.CoinItemId, 3);
            service.EnsureActor("hero_worker");

            var ok = service.TryPayWage("guild_master", "hero_worker", 5);

            Assert.IsFalse(ok);
            Assert.IsTrue(service.HasAtLeast("guild_master", InventoryService.CoinItemId, 3));
            Assert.IsFalse(service.HasAtLeast("hero_worker", InventoryService.CoinItemId, 1));
        }

        [Test]
        public void TryGrantReward_Succeeds_WhenGranterHasEnoughCoins()
        {
            var service = NewService();
            service.AddItem("quest_board", "coins", 15);
            service.EnsureActor("hero_worker");

            var ok = service.TryGrantReward("quest_board", "hero_worker", 9);

            Assert.IsTrue(ok);
            Assert.IsTrue(service.HasAtLeast("quest_board", InventoryService.CoinItemId, 6));
            Assert.IsTrue(service.HasAtLeast("hero_worker", InventoryService.CoinItemId, 9));
        }

        [Test]
        public void TryGrantReward_Fails_WhenGranterLacksFunds()
        {
            var service = NewService();
            service.AddItem("quest_board", InventoryService.CoinItemId, 2);
            service.EnsureActor("hero_worker");

            var ok = service.TryGrantReward("quest_board", "hero_worker", 5);

            Assert.IsFalse(ok);
            Assert.IsTrue(service.HasAtLeast("quest_board", InventoryService.CoinItemId, 2));
            Assert.IsFalse(service.HasAtLeast("hero_worker", InventoryService.CoinItemId, 1));
        }

        InventoryService NewService()
        {
            var library = new NarrativeContentLibrary(Application.streamingAssetsPath);
            return new InventoryService(library, _inventoryPath);
        }
    }
}
