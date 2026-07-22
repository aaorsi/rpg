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
        public void InteractionRunner_Removed_DebugStartReturnsError()
        {
            CreateVillagerRoot("villager_ref_a");
            CreateVillagerRoot("villager_ref_b");
            var simulationGo = new GameObject("village_simulation_epic37_removed_fsm");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new NoopTransport());
            simulation.TickSimulation(0f);

            Assert.IsFalse(
                simulation.TryStartInteractionForDebug("steal", "villager_ref_a", "villager_ref_b", out var startError));
            Assert.AreEqual("interaction_fsm_removed", startError);
        }

        [Test]
        public void GroupInteractionStart_Removed_ReturnsError()
        {
            CreateVillagerRoot("villager_grp_a");
            CreateVillagerRoot("villager_grp_b");
            CreateVillagerRoot("villager_grp_c");
            var simulationGo = new GameObject("village_simulation_epic37_group");
            _created.Add(simulationGo);
            var simulation = simulationGo.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new NoopTransport());
            simulation.TickSimulation(0f);

            Assert.IsFalse(
                simulation.TryStartGroupInteractionForDebug(
                    "elect_mayor",
                    "villager_grp_a",
                    new List<string> { "villager_grp_b", "villager_grp_c" },
                    out var error));
            Assert.AreEqual("interaction_fsm_removed", error);
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
