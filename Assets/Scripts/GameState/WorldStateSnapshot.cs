using System.Text;
using System.Globalization;

namespace Rpg.GameState
{
    /// <summary>
    /// Immutable slice of canonical world state safe to inject into prompts.
    /// </summary>
    public readonly struct WorldStateSnapshot
    {
        public readonly int CurrentYear;
        public readonly bool HasClockData;
        public readonly GameClockSnapshot Clock;

        public WorldStateSnapshot(int currentYear)
            : this(currentYear, default, false)
        {
        }

        public WorldStateSnapshot(int currentYear, GameClockSnapshot clock)
            : this(currentYear, clock, true)
        {
        }

        WorldStateSnapshot(int currentYear, GameClockSnapshot clock, bool hasClockData)
        {
            CurrentYear = currentYear;
            Clock = clock;
            HasClockData = hasClockData;
        }

        public string ToFactsBlock()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FACTS (canonical; the model must treat these as true and must not contradict them):");
            sb.Append("- CURRENT_YEAR: ").Append(CurrentYear).AppendLine(" (when discussing calendars, dates, or 'what year it is', this exact integer is the truth.)");
            if (HasClockData)
            {
                sb.Append("- CURRENT_DAY: ").Append(Clock.CurrentDay).AppendLine(" (in-world day count for simulation continuity.)");
                sb.Append("- CURRENT_HOUR_24: ").Append(Clock.Hour24.ToString("0.00", CultureInfo.InvariantCulture)).AppendLine(" (24h in-world clock time.)");
                sb.Append("- CURRENT_TIME_SEGMENT: ").Append(Clock.Segment.ToString().ToUpperInvariant()).AppendLine(" (coarse time-of-day label for NPC behavior and prompts.)");
            }
            return sb.ToString();
        }
    }
}
