using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Dialogue.Tests.EditMode
{
    [TestFixture]
    public class TransferDecisionParserTests
    {
        [TestCase("/accept", true)]
        [TestCase("accept", true)]
        [TestCase("YES", true)]
        [TestCase(" y ", true)]
        [TestCase("/decline", false)]
        [TestCase("decline", false)]
        [TestCase("NO", false)]
        [TestCase(" n ", false)]
        public void TryParsePlayerLine_KnownDecisionTokens_ReturnExpectedBranch(string line, bool expectedAccepted)
        {
            var parsed = TransferDecisionParser.TryParsePlayerLine(line, out var accepted);

            Assert.IsTrue(parsed);
            Assert.AreEqual(expectedAccepted, accepted);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("maybe later")]
        [TestCase("/give sword")]
        public void TryParsePlayerLine_UnknownLine_ReturnsFalse(string line)
        {
            var parsed = TransferDecisionParser.TryParsePlayerLine(line, out var accepted);

            Assert.IsFalse(parsed);
            Assert.IsFalse(accepted);
        }
    }
}
