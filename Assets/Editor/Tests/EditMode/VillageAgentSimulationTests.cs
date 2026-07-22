using System;
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
        public void TickSimulation_InvalidPlanReferences_RejectsAndFallsBack()
        {
            CreateVillagerRoot("villager_011");
            var simulationGo = new GameObject("village_simulation_invalid_refs");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new InvalidRefTransport());

            simulation.TickSimulation(0f);
            simulation.TickSimulation(0.1f);

            Assert.IsTrue(simulation.States.ContainsKey("villager_011"));
            var state = simulation.States["villager_011"];
            Assert.AreEqual("fallback", state.LastDeliberationSource);
            StringAssert.Contains("invalid_plan_refs", state.LastError);
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
            Assert.IsNotNull(simulation.InteractionDefinitions);
            Assert.IsNotNull(simulation.InteractionDefinitions.interactions);
            Assert.Greater(simulation.InteractionDefinitions.interactions.Count, 0);
            Assert.IsNotNull(transport.LastRequest.agreements);
            Assert.GreaterOrEqual(transport.LastRequest.agreements.Count, 2);
            StringAssert.Contains("Village standing tracks", transport.LastRequest.agreements[0]);
            StringAssert.Contains("Local opinion", transport.LastRequest.agreements[1]);
            Assert.AreEqual("villager_003", transport.LastRequest.npcId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(transport.LastRequest.goal));
            Assert.IsNotNull(transport.LastRequest.targets);
            Assert.IsNotNull(transport.LastRequest.targets.npcIds);
            CollectionAssert.Contains(transport.LastRequest.targets.npcIds, "villager_003");
            Assert.IsNotNull(transport.LastRequest.targets.locationIds);
            StringAssert.Contains("Known NPCs", transport.LastRequest.world.worldFacts);
            StringAssert.Contains("Known valid locationIds", transport.LastRequest.world.worldFacts);
        }

        [Test]
        public void DebugHeroImpact_AdjustsOpinionSummaryForSelectedVillager()
        {
            CreateVillagerRoot("villager_004");
            var simulationGo = new GameObject("village_simulation_debug_impact");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());
            simulation.TickSimulation(0f);

            var before = simulation.OpinionService.GetSummary("villager_004");
            Assert.IsTrue(simulation.TryApplyHeroImpactForDebug("villager_004", 15f, 6f, 0f, 0f, 2f, out var error), error);
            var after = simulation.OpinionService.GetSummary("villager_004");

            Assert.Greater(after.OpinionTowardHero, before.OpinionTowardHero);
            Assert.Greater(after.AggregateLeadership, before.AggregateLeadership);
        }

        [Test]
        public void DebugGossipQueue_AllowsManualQueueAndProcessing()
        {
            CreateVillagerRoot("villager_005");
            CreateVillagerRoot("villager_006");
            var simulationGo = new GameObject("village_simulation_debug_gossip");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());
            simulation.TickSimulation(0f);

            simulation.OpinionService.ApplyHeroImpact("villager_005", 70f, 0f, 0f, 0f, 0f);
            simulation.OpinionService.ApplyHeroImpact("villager_006", -40f, 0f, 0f, 0f, 0f);
            Assert.IsTrue(simulation.TryQueueGossipForDebug("villager_005", "villager_006", out var error), error);
            Assert.AreEqual(1, simulation.OpinionService.PendingGossipCount);

            var processed = simulation.ProcessGossipForDebug(1);
            Assert.AreEqual(1, processed);
            Assert.AreEqual(0, simulation.OpinionService.PendingGossipCount);
        }

        [Test]
        public void DebugForceChatPlan_InjectsChatStepIntoNpcController()
        {
            var speaker = CreateVillagerRoot("villager_007");
            CreateVillagerRoot("villager_008");
            var simulationGo = new GameObject("village_simulation_debug_chat");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());
            simulation.TickSimulation(0f);

            Assert.IsTrue(simulation.TryForceChatPlanForDebug("villager_007", "villager_008", 2.5f, out var error), error);
            var state = simulation.States["villager_007"];
            var controller = speaker.GetComponent<NpcAgentController>();

            Assert.AreEqual(1, state.ActivePlan.Count);
            Assert.AreEqual(NpcPrimitiveTypes.ChatWithNpc, state.ActivePlan[0].PrimitiveType);
            Assert.AreEqual("villager_008", state.ActivePlan[0].TargetNpcId);
            Assert.IsTrue(controller.IsExecutingPlan);
        }

        [Test]
        public void DebugGroupAskResponse_TransitionsOfferedAskAndReturnsSignals()
        {
            CreateVillagerRoot("villager_009");
            CreateVillagerRoot("villager_010");
            var simulationGo = new GameObject("village_simulation_debug_ask");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());
            simulation.TickSimulation(0f);

            simulation.OpinionService.ApplyHeroImpact("villager_009", 0f, 90f, 0f, 0f, 50f);
            var asks = simulation.OpinionService.SnapshotGroupAsks();
            Assert.IsTrue(asks.Exists(a => string.Equals(a.askId, "ask_run_for_mayor", System.StringComparison.OrdinalIgnoreCase)));

            Assert.IsTrue(
                simulation.TryRespondToGroupAskForDebug("ask_run_for_mayor", true, "villager_009", out var signals, out var error),
                error);
            CollectionAssert.Contains(signals, "unlock:m_village_mayor_arc");

            var updated = simulation.OpinionService.SnapshotGroupAsks();
            var mayor = updated.Find(a => string.Equals(a.askId, "ask_run_for_mayor", System.StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(mayor);
            Assert.AreEqual("accepted", mayor.state);
        }

        [Test]
        public void WorldReferenceSnapshot_IncludesRegisteredNpcs()
        {
            CreateVillagerRoot("villager_snapshot");
            var simulationGo = new GameObject("village_simulation_snapshot");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());
            simulation.TickSimulation(0f);

            var snapshot = simulation.WorldReferenceSnapshot;
            Assert.IsNotNull(snapshot);
            Assert.IsTrue(snapshot.npcIds.Contains("villager_snapshot"));
        }

        [Test]
        public void BuildDebugNpcEntryList_IncludesHeroEntry()
        {
            CreateVillagerRoot("villager_hero_list");
            var simulationGo = new GameObject("village_simulation_debug_hero_list");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());
            simulation.TickSimulation(0f);

            var entries = new List<VillageAgentSimulation.DebugNpcEntry>();
            simulation.BuildDebugNpcEntryList(entries);

            Assert.IsTrue(entries.Exists(e => e.IsHero && e.NpcId == InventoryService.HeroActorId));
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

        sealed class InvalidRefTransport : VillageAgentSimulation.IVillageDeliberationTransport
        {
            public Task<VillageAgentSimulation.VillagerDeliberationEnvelope> DeliberateAsync(
                PythonNpcDeliberationRequestDto request,
                CancellationToken token)
            {
                var envelope = new VillageAgentSimulation.VillagerDeliberationEnvelope
                {
                    Success = true,
                    UsedFallback = false,
                    Goals = new List<string> { "Visit unknown target" },
                    PlanSteps = new List<NpcPrimitiveStep>
                    {
                        new NpcPrimitiveStep
                        {
                            PrimitiveType = NpcPrimitiveTypes.GotoNpc,
                            TargetNpcId = "npc_does_not_exist",
                            DurationSeconds = 1f
                        }
                    }
                };
                return Task.FromResult(envelope);
            }
        }
    }
}
