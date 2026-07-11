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
    public sealed class Epic37CompletionTests
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
        public void WorldReferenceSnapshot_IncludesGoalsAndRelationships()
        {
            CreateVillagerRoot("villager_goal");
            var simulationGo = new GameObject("village_simulation_epic37_snapshot");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new NoopTransport());
            simulation.TickSimulation(0f);
            simulation.RequestWorldReferenceRefresh("test");

            var snapshot = simulation.WorldReferenceSnapshot;
            Assert.IsNotNull(snapshot);
            Assert.Greater(snapshot.goalIds.Count, 0);
            Assert.Greater(snapshot.relationships.Count, 0);
            Assert.AreEqual(2, snapshot.schemaVersion);
        }

        [Test]
        public void InteractionRunner_InvalidLocationStep_FailsAndRecordsRejectEvent()
        {
            CreateVillagerRoot("villager_ref_a");
            CreateVillagerRoot("villager_ref_b");
            var simulationGo = new GameObject("village_simulation_epic37_invalid_ref");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new NoopTransport());
            simulation.TickSimulation(0f);

            var badDefinition = new InteractionDefinition
            {
                id = "debug_bad_move",
                status = "active",
                phases = new InteractionPhases
                {
                    start = new List<InteractionActionStep>
                    {
                        new InteractionActionStep
                        {
                            actionType = InteractionActionTypes.MoveToLocation,
                            actorRole = "initiator",
                            parameters = new Dictionary<string, string> { { "locationRef", "nowhere_land" } }
                        }
                    }
                }
            };
            Assert.IsTrue(simulation.TryRegisterDebugInteractionDefinition(badDefinition, out var registerError), registerError);
            Assert.IsTrue(
                simulation.TryStartInteractionForDebug("debug_bad_move", "villager_ref_a", "villager_ref_b", out var startError),
                startError);
            simulation.TickSimulation(0f);
            simulation.TickSimulation(6f);

            var failed = false;
            var active = simulation.ActiveInteractions;
            for (var i = 0; i < active.Count; i++)
            {
                var interaction = active[i];
                if (interaction != null
                    && string.Equals(interaction.interactionId, "debug_bad_move", System.StringComparison.OrdinalIgnoreCase)
                    && interaction.status == InteractionRuntimeStatus.Failed)
                {
                    failed = true;
                    break;
                }
            }

            Assert.IsTrue(failed);
            Assert.Greater(simulation.InteractionRejectEvents.Count, 0);
        }

        [Test]
        public void GroupInteractionStart_BuildsThreePlusParticipantJoinContext()
        {
            CreateVillagerRoot("villager_grp_a");
            CreateVillagerRoot("villager_grp_b");
            CreateVillagerRoot("villager_grp_c");
            var simulationGo = new GameObject("village_simulation_epic37_group");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new NoopTransport());
            simulation.TickSimulation(0f);

            Assert.IsTrue(
                simulation.TryStartGroupInteractionForDebug(
                    "elect_mayor",
                    "villager_grp_a",
                    new List<string> { "villager_grp_b", "villager_grp_c" },
                    out var error),
                error);

            Assert.IsTrue(simulation.TryAcquireHeroJoinContext(Vector3.zero, 100f, out var context), "join context");
            Assert.IsNotNull(context);
            Assert.GreaterOrEqual(context.ParticipantNpcIds.Count, 3);
        }

        [Test]
        public void SeedInteractionFile_LoadsWithoutValidatorIssues()
        {
            var seedPath = Path.Combine(Application.streamingAssetsPath, "Dialogue", "interactions.seed.json");
            Assert.IsTrue(File.Exists(seedPath), "seed file missing");
            var registry = new InteractionDefinitionRegistry(seedPath, Path.Combine(Path.GetTempPath(), "rpg-test-runtime-" + System.Guid.NewGuid().ToString("N")));
            var doc = registry.LoadEffective(out var issues);
            Assert.IsNotNull(doc);
            Assert.AreEqual(0, issues.Count, string.Join(" | ", issues));
            Assert.Greater(doc.interactions.Count, 4);
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
