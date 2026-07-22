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
    public sealed class OptionASystemicModeTests
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
        public void DefaultSimulationMode_IsSystemicOnly()
        {
            var go = new GameObject("sim_default_mode");
            _created.Add(go);
            var simulation = go.AddComponent<VillageAgentSimulation>();
            Assert.AreEqual(VillageSimulationMode.SystemicOnly, simulation.SimulationMode);
            Assert.IsTrue(simulation.IsSystemicOnlyMode);
            Assert.IsFalse(simulation.IsInteractionRunnerActive);
        }

        [Test]
        public void SystemicOnly_BlocksDebugInteractionStart()
        {
            CreateVillagerRoot("v_sys_a");
            CreateVillagerRoot("v_sys_b");
            var go = new GameObject("sim_systemic_only");
            _created.Add(go);
            var simulation = go.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());

            Assert.IsFalse(simulation.TryStartInteractionForDebug("steal", "v_sys_a", "v_sys_b", out var error));
            Assert.AreEqual("interaction_fsm_removed", error);
            Assert.AreEqual(0, simulation.ActiveInteractions.Count);
        }

        [Test]
        public void InteractionRunner_IsPermanentlyDisabled()
        {
            var go = new GameObject("sim_runner_disabled");
            _created.Add(go);
            var simulation = go.AddComponent<VillageAgentSimulation>();
            simulation.ConfigureForTests(new FailingTransport());
            Assert.IsFalse(simulation.IsInteractionRunnerActive);
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
    }
}
