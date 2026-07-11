using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public sealed class VillageAtomicActionDebugTests
    {
        readonly List<Object> _created = new List<Object>();
        string _inventoryPath;

        [SetUp]
        public void SetUp()
        {
            _inventoryPath = Path.Combine(Path.GetTempPath(), $"atomic-debug-{System.Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
            if (File.Exists(_inventoryPath))
                File.Delete(_inventoryPath);
        }

        [Test]
        public void BuildDebugLocationEntryList_IncludesCatalogLocations()
        {
            var simulation = CreateSimulation();
            var locations = new List<VillageAgentSimulation.DebugLocationEntry>();
            simulation.BuildDebugLocationEntryList(locations);

            Assert.Greater(locations.Count, 0);
            Assert.IsTrue(locations.Exists(entry => entry.LocationId == "warehouse"));
        }

        [Test]
        public void BuildDebugItemEntryList_IncludesKnownItems()
        {
            var simulation = CreateSimulation(withInventory: true);
            var items = new List<VillageAgentSimulation.DebugItemEntry>();
            simulation.BuildDebugItemEntryList(items);

            Assert.Greater(items.Count, 0);
        }

        [Test]
        public void TryMoveNpcToLocationForDebug_UnknownLocation_FailsValidation()
        {
            CreateVillagerRoot("villager_move_a");
            var simulation = CreateSimulation();
            simulation.TickSimulation(0f);

            var ok = simulation.TryMoveNpcToLocationForDebug("villager_move_a", "not_a_real_place", out var summary);

            Assert.IsFalse(ok);
            StringAssert.Contains("unknown locationRef", summary);
        }

        [Test]
        public void TryMoveNpcToLocationForDebug_ValidLocation_DispatchesPlan()
        {
            CreateVillagerRoot("villager_move_b");
            var simulation = CreateSimulation();
            simulation.TickSimulation(0f);

            var ok = simulation.TryMoveNpcToLocationForDebug("villager_move_b", "escape_nearby", out var summary);

            Assert.IsTrue(ok, summary);
            StringAssert.Contains("move_to_location", summary);
            Assert.IsTrue(simulation.States.ContainsKey("villager_move_b"));
            var state = simulation.States["villager_move_b"];
            Assert.GreaterOrEqual(state.ActivePlan.Count, 1);
            Assert.AreEqual(NpcPrimitiveTypes.GotoLocation, state.ActivePlan[0].PrimitiveType);
        }

        [Test]
        public void TryMoveNpcToNpcForDebug_ValidTarget_DispatchesPlan()
        {
            var mover = CreateVillagerRoot("villager_move_c");
            CreateVillagerRoot("villager_move_d");
            var simulation = CreateSimulation();
            simulation.TickSimulation(0f);

            var ok = simulation.TryMoveNpcToNpcForDebug("villager_move_c", "villager_move_d", out var summary);

            Assert.IsTrue(ok, summary);
            StringAssert.Contains("move_to_npc", summary);
            Assert.IsTrue(simulation.States.ContainsKey("villager_move_c"));
            var state = simulation.States["villager_move_c"];
            Assert.GreaterOrEqual(state.ActivePlan.Count, 1);
            Assert.AreEqual(NpcPrimitiveTypes.GotoNpc, state.ActivePlan[0].PrimitiveType);
            Assert.AreEqual("villager_move_d", state.ActivePlan[0].TargetNpcId);
        }

        [Test]
        public void TryExchangeItemForDebug_TransferMode_MovesItemWhenInRange()
        {
            CreateVillagerRoot("villager_giver");
            CreateVillagerRoot("villager_receiver");
            var simulation = CreateSimulation(withInventory: true);
            simulation.TickSimulation(0f);

            var inventory = NewInventory();
            simulation.ConfigureInventoryForTests(inventory);
            inventory.EnsureActor("villager_giver");
            inventory.EnsureActor("villager_receiver");
            var itemId = inventory.GetAllKnownItemIds()[0];
            inventory.AddItem("villager_giver", itemId, 1);

            var ok = simulation.TryExchangeItemForDebug(
                "villager_giver",
                "villager_receiver",
                "transfer",
                itemId,
                1,
                out var summary);

            Assert.IsTrue(ok, summary);
            StringAssert.Contains("transferred", summary);
            Assert.IsTrue(inventory.HasAtLeast("villager_receiver", itemId, 1));
            Assert.IsFalse(inventory.HasAtLeast("villager_giver", itemId, 1));
        }

        [Test]
        public void TryExchangeCoinsForDebug_TransfersCoinsWhenInRange()
        {
            CreateVillagerRoot("villager_payer");
            CreateVillagerRoot("villager_payee");
            var simulation = CreateSimulation(withInventory: true);
            simulation.TickSimulation(0f);

            var inventory = NewInventory();
            simulation.ConfigureInventoryForTests(inventory);
            inventory.AddItem("villager_payer", InventoryService.CoinItemId, 12);
            inventory.EnsureActor("villager_payee");

            var ok = simulation.TryExchangeCoinsForDebug(
                "villager_payer",
                "villager_payee",
                5,
                "transfer",
                out var summary);

            Assert.IsTrue(ok, summary);
            StringAssert.Contains("transferred 5 coins", summary);
            Assert.IsTrue(inventory.HasAtLeast("villager_payee", InventoryService.CoinItemId, 5));
            Assert.IsTrue(inventory.HasAtLeast("villager_payer", InventoryService.CoinItemId, 7));
        }

        VillageAgentSimulation CreateSimulation(bool withInventory = false)
        {
            var simulationGo = new GameObject("village_simulation_atomic_debug");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new NoopTransport());
            if (withInventory)
                simulation.ConfigureInventoryForTests(NewInventory());
            return simulation;
        }

        InventoryService NewInventory()
        {
            var library = new NarrativeContentLibrary(Application.streamingAssetsPath);
            return new InventoryService(library, _inventoryPath);
        }

        GameObject CreateVillagerRoot(string npcId)
        {
            var go = new GameObject(npcId);
            _created.Add(go);
            go.transform.position = Vector3.zero;
            go.AddComponent<CharacterController>();
            go.AddComponent<VillagerAmbientRoutine>();
            var controller = go.AddComponent<NpcAgentController>();
            controller.InitializeForTests();

            var binding = go.AddComponent<NpcDialogueBinding>();
            var def = ScriptableObject.CreateInstance<NpcDefinition>();
            def.npcId = npcId;
            def.displayName = npcId;
            _created.Add(def);
            binding.SetDefinition(def);
            binding.SetPersona(new NpcPersona
            {
                npcId = npcId,
                goals = new List<string> { "Protect village life." },
                capabilities = new List<string> { "dialogue" }
            });
            return go;
        }

        sealed class NoopTransport : VillageAgentSimulation.IVillageDeliberationTransport
        {
            public Task<VillageAgentSimulation.VillagerDeliberationEnvelope> DeliberateAsync(
                PythonNpcDeliberationRequestDto request,
                CancellationToken token) =>
                Task.FromResult(VillageAgentSimulation.VillagerDeliberationEnvelope.Failure("noop", true));
        }
    }
}
