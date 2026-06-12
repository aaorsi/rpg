using System.Text;

namespace Rpg.GameState
{
    /// <summary>
    /// Immutable slice of canonical world state safe to inject into prompts.
    /// </summary>
    public readonly struct WorldStateSnapshot
    {
        public readonly int CurrentYear;

        public WorldStateSnapshot(int currentYear)
        {
            CurrentYear = currentYear;
        }

        public string ToFactsBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FACTS (canonical; the model must treat these as true and must not contradict them):");
            sb.Append("- CURRENT_YEAR: ").Append(CurrentYear).AppendLine(" (when discussing calendars, dates, or 'what year it is', this exact integer is the truth.)");
            return sb.ToString();
        }
    }
}
