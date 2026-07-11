using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Dialogue;
using Rpg.Npc;

namespace Rpg.Editor.Tests.EditMode
{
    public sealed class NpcInteractionMemoryServiceTests
    {
        [Test]
        public void RecordInteractionStarted_writes_romantic_memory_for_actor()
        {
            var memory = new NpcMemoryRepository(System.IO.Path.Combine(
                UnityEngine.Application.temporaryCachePath,
                "NpcInteractionMemoryTests",
                "mem"));
            var service = new NpcInteractionMemoryService(memory);
            var instance = new InteractionRuntimeInstance
            {
                interactionId = "romantic_relationship",
                interactionDisplayName = "Romantic Relationship",
                actorNpcId = "npc_a",
                targetNpcId = "npc_b"
            };

            service.RecordInteractionStarted(instance, null, id => id == "npc_a" ? "Alice" : "Bob");

            var block = memory.BuildPromptBlock("npc_a");
            Assert.IsTrue(block.Contains("romantic"));
            Assert.IsTrue(block.Contains("[interaction]"));
        }

        [Test]
        public void RecordInteractionCompleted_writes_mayor_memory_for_audience()
        {
            var memory = new NpcMemoryRepository(System.IO.Path.Combine(
                UnityEngine.Application.temporaryCachePath,
                "NpcInteractionMemoryTests",
                "mem2"));
            var service = new NpcInteractionMemoryService(memory);
            var instance = new InteractionRuntimeInstance
            {
                interactionId = "elect_mayor",
                interactionDisplayName = "Elect the Hero as Mayor",
                actorNpcId = "npc_leader",
                targetNpcId = "npc_audience"
            };
            var definition = new InteractionDefinition
            {
                outcomes = new List<InteractionOutcome>
                {
                    new InteractionOutcome { id = "mayor_elected", probability = 1f }
                }
            };

            service.RecordInteractionCompleted(instance, definition, "mayor_elected", id => "Villager");

            var block = memory.BuildPromptBlock("npc_audience");
            Assert.IsTrue(block.Contains("mayor"));
        }

        [Test]
        public void RecordInteractionStarted_writes_traveler_memory_when_hero_is_actor()
        {
            var memory = new NpcMemoryRepository(System.IO.Path.Combine(
                UnityEngine.Application.temporaryCachePath,
                "NpcInteractionMemoryTests",
                "hero_actor"));
            var service = new NpcInteractionMemoryService(memory);
            var instance = new InteractionRuntimeInstance
            {
                interactionId = "offer_services",
                interactionDisplayName = "Offer Services",
                actorNpcId = InventoryService.HeroActorId,
                targetNpcId = "npc_b"
            };

            service.RecordInteractionStarted(instance, null, id => id == "npc_b" ? "Bob" : "Traveler");

            var block = memory.BuildPromptBlock("npc_b");
            Assert.IsTrue(block.Contains("traveler"));
            Assert.IsTrue(block.Contains("[interaction]"));
        }

        [Test]
        public void RecordInteractionStarted_does_not_write_memory_for_hero_id()
        {
            var memory = new NpcMemoryRepository(System.IO.Path.Combine(
                UnityEngine.Application.temporaryCachePath,
                "NpcInteractionMemoryTests",
                "hero_skip"));
            var service = new NpcInteractionMemoryService(memory);
            var instance = new InteractionRuntimeInstance
            {
                interactionId = "offer_services",
                interactionDisplayName = "Offer Services",
                actorNpcId = "npc_a",
                targetNpcId = InventoryService.HeroActorId
            };

            service.RecordInteractionStarted(instance, null, id => "Villager");

            var block = memory.BuildPromptBlock(InventoryService.HeroActorId);
            Assert.IsFalse(block.Contains("[interaction]"));
        }
    }
}
