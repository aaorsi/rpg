namespace Rpg.Dialogue
{
    /// <summary>Parses player accept/decline lines for pending inventory transfers.</summary>
    public static class TransferDecisionParser
    {
        public static bool TryParsePlayerLine(string line, out bool accepted)
        {
            accepted = false;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var l = line.Trim().ToLowerInvariant();
            if (l == "/accept" || l == "accept" || l == "yes" || l == "y")
            {
                accepted = true;
                return true;
            }

            if (l == "/decline" || l == "decline" || l == "no" || l == "n")
            {
                accepted = false;
                return true;
            }

            return false;
        }
    }
}
