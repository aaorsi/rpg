using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class NpcMemoryRepositoryTests
    {
        string _tempDir;
        NpcMemoryRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"rpg-memory-tests-{Guid.NewGuid():N}");
            _repo = new NpcMemoryRepository(_tempDir);
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
        public void TryAppendCandidates_InvalidNpcId_UsesSanitizedFileNameAndPersists()
        {
            var npcId = "villager:101/alden";
            _repo.TryAppendCandidates(npcId, new List<NpcMemoryCandidate>
            {
                new NpcMemoryCandidate("fact", "Likes bread.", "player")
            });

            var expectedPath = Path.Combine(_tempDir, $"{SanitizeFileName(npcId)}.json");
            Assert.IsTrue(File.Exists(expectedPath));

            var promptBlock = _repo.BuildPromptBlock(npcId);
            StringAssert.Contains("Likes bread.", promptBlock);
        }

        [Test]
        public void BuildPromptBlock_CorruptedJson_ReturnsDefaultBlock()
        {
            var npcId = "villager_202";
            var path = Path.Combine(_tempDir, $"{SanitizeFileName(npcId)}.json");
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(path, "{not-valid-json");

            var block = _repo.BuildPromptBlock(npcId);

            Assert.AreEqual("(No remembered facts yet.)", block);
        }

        static string SanitizeFileName(string value)
        {
            var safe = string.IsNullOrWhiteSpace(value) ? "npc_unknown" : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            return safe;
        }
    }
}
