using System.Collections.Generic;
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
    public sealed class VillageAgentSimulationTests
    {
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (var i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void TickSimulation_FailedDeliberation_UsesFallbackWithoutStartingPlan()
        {
            var villagerRoot = CreateVillagerRoot("villager_001");
            var simulationGo = new GameObject("village_simulation");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());

            simulation.TickSimulation(0f);
            simulation.TickSimulation(0.1f);

            var controller = villagerRoot.GetComponent<NpcAgentController>();
            Assert.IsFalse(controller.IsExecutingPlan);
            Assert.IsTrue(simulation.States.ContainsKey("villager_001"));
            var state = simulation.States["villager_001"];
            Assert.AreEqual("fallback", state.LastDeliberationSource);
            StringAssert.Contains("sidecar_failed", state.LastError);
            var telemetry = simulation.TelemetrySnapshot;
            Assert.AreEqual(1, telemetry.DeliberationCalls);
            Assert.AreEqual(1, telemetry.FallbackCalls);
        }

        [Test]
        public void TickSimulation_SidecarPlan_FeedsNpcAgentControllerAndTracksGoals()
        {
            var villagerRoot = CreateVillagerRoot("villager_002");
            var simulationGo = new GameObject("village_simulation_success");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new SuccessTransport());

            simulation.TickSimulation(0f);
            simulation.TickSimulation(0.1f);

            var controller = villagerRoot.GetComponent<NpcAgentController>();
            Assert.IsTrue(controller.IsExecutingPlan);
            Assert.IsTrue(simulation.States.ContainsKey("villager_002"));
            var state = simulation.States["villager_002"];
            Assert.AreEqual("sidecar", state.LastDeliberationSource);
            CollectionAssert.Contains(state.ActiveGoals, "Keep watch at the square");
            Assert.GreaterOrEqual(state.ActivePlan.Count, 1);
        }

        [Test]
        public void TickSimulation_AddsVillageOpinionSummaryToDeliberationContext()
        {
            CreateVillagerRoot("villager_003");
            var simulationGo = new GameObject("village_simulation_context");
            _created.Add(simulationGo);
            var transport = new CapturingTransport();
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(transport);
            simulation.OpinionService.ApplyHeroImpact("villager_003", 12f, 9f, -3f, 2f, 5f);

            simulation.TickSimulation(0f);
            simulation.TickSimulation(0.1f);

            Assert.IsNotNull(transport.LastRequest);
            Assert.IsNotNull(transport.LastRequest.agreements);
            Assert.GreaterOrEqual(transport.LastRequest.agreements.Count, 2);
            StringAssert.Contains("Village standing tracks", transport.LastRequest.agreements[0]);
            StringAssert.Contains("Local opinion", transport.LastRequest.agreements[1]);
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

        sealed class FailingTransport : VillageAgentSimulation.IVillageDeliberationTransport
        {
            public Task<VillageAgentSimulation.VillagerDeliberationEnvelope> DeliberateAsync(
                PythonNpcDeliberationRequestDto request,
                CancellationToken token)
            {
                return Task.FromResult(VillageAgentSimulation.VillagerDeliberationEnvelope.Failure("sidecar_failed", true));
            }
        }

        sealed class SuccessTransport : VillageAgentSimulation.IVillageDeliberationTransport
        {
            public Task<VillageAgentSimulation.VillagerDeliberationEnvelope> DeliberateAsync(
                PythonNpcDeliberationRequestDto request,
                CancellationToken token)
            {
                var envelope = new VillageAgentSimulation.VillagerDeliberationEnvelope
                {
                    Success = true,
                    UsedFallback = false,
                    Goals = new List<string> { "Keep watch at the square" },
                    PlanSteps = new List<NpcPrimitiveStep>
                    {
                        new NpcPrimitiveStep
                        {
                            PrimitiveType = NpcPrimitiveTypes.WaitAt,
                            WorldLocation = Vector3.zero,
                            DurationSeconds = 0.2f,
                            StopDistanceMeters = 0.3f
                        }
                    }
                };
                return Task.FromResult(envelope);
            }
        }

        sealed class CapturingTransport : VillageAgentSimulation.IVillageDeliberationTransport
        {
            public PythonNpcDeliberationRequestDto LastRequest { get; private set; }

            public Task<VillageAgentSimulation.VillagerDeliberationEnvelope> DeliberateAsync(
                PythonNpcDeliberationRequestDto request,
                CancellationToken token)
            {
                LastRequest = request;
                return Task.FromResult(VillageAgentSimulation.VillagerDeliberationEnvelope.Failure("captured", true));
            }
        }
    }
}
