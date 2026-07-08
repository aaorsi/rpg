using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class NpcDialogueTranscriptRepositoryTests
    {
        string _tempDir;
        NpcDialogueTranscriptRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"rpg-transcript-tests-{Guid.NewGuid():N}");
            _repo = new NpcDialogueTranscriptRepository(_tempDir);
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
        public void SaveAndLoad_InvalidNpcId_UsesSanitizedFileNameAndPreservesMessages()
        {
            var npcId = "villager:303/alden";
            var messages = new List<OllamaMessageDto>
            {
                new OllamaMessageDto("user", "Hello"),
                new OllamaMessageDto("assistant", "Hi traveler")
            };

            _repo.Save(npcId, messages, markConversationEnded: false);

            var expectedPath = Path.Combine(_tempDir, $"{SanitizeFileName(npcId)}.json");
            Assert.IsTrue(File.Exists(expectedPath));

            var snapshot = _repo.LoadSnapshot(npcId);
            Assert.AreEqual(2, snapshot.Messages.Count);
            Assert.AreEqual("assistant", snapshot.Messages[1].role);
            Assert.AreEqual("Hi traveler", snapshot.Messages[1].content);
        }

        [Test]
        public void LoadSnapshot_CorruptedJson_ReturnsEmptySnapshot()
        {
            var npcId = "villager_404";
            var path = Path.Combine(_tempDir, $"{SanitizeFileName(npcId)}.json");
            Directory.CreateDirectory(_tempDir);
            File.WriteAllText(path, "{not-valid-json");

            var snapshot = _repo.LoadSnapshot(npcId);

            Assert.IsNotNull(snapshot);
            Assert.IsNotNull(snapshot.Messages);
            Assert.AreEqual(0, snapshot.Messages.Count);
            Assert.IsNull(snapshot.LastConversationEndedUtc);
            Assert.IsNull(snapshot.DialoguePlayInstanceId);
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
