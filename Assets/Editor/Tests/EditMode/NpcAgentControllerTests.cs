using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Npc.Tests.EditMode
{
    [TestFixture]
    public class NpcAgentControllerTests
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
        public void ExecutePlan_AllPrimitiveTypes_TransitionsToCompleted()
        {
            var controller = CreateController("agent", Vector3.zero);
            var targetNpc = CreateNpcBinding("target_npc", "target_npc", Vector3.zero);

            controller.SetPlan(new[]
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.GotoLocation,
                    WorldLocation = Vector3.zero,
                    StopDistanceMeters = 0.25f
                },
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.GotoNpc,
                    TargetNpcId = targetNpc.Definition.npcId,
                    StopDistanceMeters = 1f
                },
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.WaitAt,
                    WorldLocation = Vector3.zero,
                    DurationSeconds = 0.05f
                },
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.PerformWork,
                    WorldLocation = Vector3.zero,
                    DurationSeconds = 0.05f,
                    WorkId = "smithing"
                },
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.ChatWithNpc,
                    TargetNpcId = targetNpc.Definition.npcId,
                    DurationSeconds = 0.05f
                },
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.IdleHome,
                    DurationSeconds = 0.05f
                }
            });

            SimulateTicks(controller, tickCount: 24, deltaTime: 0.02f);

            Assert.IsFalse(controller.IsExecutingPlan);
            Assert.AreEqual(6, controller.SuccessfulStepCount);
            Assert.AreEqual(0, controller.FailedStepCount);
            Assert.AreEqual(NpcAgentStepCompletionState.Succeeded, controller.LastStepCompletionState);
            Assert.AreEqual(0, controller.RemainingStepCount);
        }

        [Test]
        public void ExecutePlan_MissingNpcTarget_FailsStepAndAdvances()
        {
            var controller = CreateController("agent", Vector3.zero);
            controller.SetPlan(new[]
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.GotoNpc,
                    TargetNpcId = "does_not_exist"
                },
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.WaitAt,
                    WorldLocation = Vector3.zero,
                    DurationSeconds = 0.02f
                }
            });

            SimulateTicks(controller, tickCount: 8, deltaTime: 0.02f);

            Assert.IsFalse(controller.IsExecutingPlan);
            Assert.AreEqual(1, controller.SuccessfulStepCount);
            Assert.AreEqual(1, controller.FailedStepCount);
            Assert.AreEqual(NpcAgentStepCompletionState.Succeeded, controller.LastStepCompletionState);
        }

        [Test]
        public void IdleWithoutPlan_RestoresVillagerAmbientRoutine()
        {
            var go = new GameObject("agent_with_ambient");
            _created.Add(go);
            go.transform.position = Vector3.zero;

            go.AddComponent<CharacterController>();
            var ambient = go.AddComponent<VillagerAmbientRoutine>();
            var controller = go.AddComponent<NpcAgentController>();
            controller.InitializeForTests();

            Assert.IsTrue(ambient.enabled);

            controller.SetPlan(new[]
            {
                new NpcPrimitiveStep
                {
                    PrimitiveType = NpcPrimitiveTypes.WaitAt,
                    WorldLocation = Vector3.zero,
                    DurationSeconds = 0.02f
                }
            });

            controller.TickAgent(0.02f, 0.02f);
            Assert.IsFalse(ambient.enabled);

            SimulateTicks(controller, tickCount: 6, deltaTime: 0.02f, startTime: 0.04f);
            Assert.IsFalse(controller.IsExecutingPlan);
            Assert.IsTrue(ambient.enabled);
        }

        static void SimulateTicks(NpcAgentController controller, int tickCount, float deltaTime, float startTime = 0f)
        {
            var now = startTime;
            for (var i = 0; i < tickCount; i++)
            {
                controller.TickAgent(deltaTime, now);
                now += deltaTime;
            }
        }

        NpcAgentController CreateController(string name, Vector3 position)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var controller = go.AddComponent<NpcAgentController>();
            controller.InitializeForTests();
            return controller;
        }

        NpcDialogueBinding CreateNpcBinding(string gameObjectName, string npcId, Vector3 position)
        {
            var go = new GameObject(gameObjectName);
            _created.Add(go);
            go.transform.position = position;
            var binding = go.AddComponent<NpcDialogueBinding>();
            var def = ScriptableObject.CreateInstance<NpcDefinition>();
            def.npcId = npcId;
            def.displayName = npcId;
            _created.Add(def);
            binding.SetDefinition(def);
            return binding;
        }
    }
}
