using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rpg.Dialogue;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class NpcPersonaRepositoryTests
    {
        string _tempDir;
        NpcPersonaRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"rpg-persona-tests-{Guid.NewGuid():N}");
            _repo = new NpcPersonaRepository(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best-effort cleanup for test temp files.
            }
        }

        [Test]
        public void SaveAndLoad_PreservesPersonaFields()
        {
            var persona = new NpcPersona
            {
                npcId = "villager_101_alden",
                personality = "friendly",
                socialTraits = new Dictionary<string, string> { { "helpfulness", "high" }, { "skepticism", "low" } },
                goals = new List<string> { "Share local rumors." },
                capabilities = new List<string> { "dialogue", "guide_to_location" }
            };

            _repo.Save(persona);
            var loaded = _repo.Load("villager_101_alden");

            Assert.IsNotNull(loaded);
            Assert.AreEqual("villager_101_alden", loaded.npcId);
            Assert.AreEqual("friendly", loaded.personality);
            Assert.AreEqual("persona_villager_101_alden", loaded.personaId);
            Assert.AreEqual("high", loaded.socialTraits["helpfulness"]);
            CollectionAssert.Contains(loaded.goals, "Share local rumors.");
            CollectionAssert.Contains(loaded.capabilities, "guide_to_location");
        }

        [Test]
        public void LoadOrCreate_NoExistingPersona_GeneratesFallbackAndStableId()
        {
            var definition = ScriptableObject.CreateInstance<NpcDefinition>();
            definition.npcId = "villager_1000_alden";
            definition.roleSummary = "A friendly villager focused on practical village life.";

            var generated = _repo.LoadOrCreate(definition, "normal");
            var loadedAgain = _repo.Load("villager_1000_alden");

            Assert.IsNotNull(generated);
            Assert.AreEqual("persona_villager_1000_alden", generated.personaId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(generated.personality));
            Assert.IsNotNull(generated.socialTraits);
            Assert.IsNotNull(generated.goals);
            Assert.IsNotNull(generated.capabilities);
            CollectionAssert.Contains(generated.capabilities, "dialogue");

            Assert.IsNotNull(loadedAgain);
            Assert.AreEqual(generated.personaId, loadedAgain.personaId);
            Assert.AreEqual(generated.npcId, loadedAgain.npcId);
        }
    }
}
