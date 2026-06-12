namespace Rpg.GameState
{
    public readonly struct GameClockSnapshot
    {
        public readonly int CurrentDay;
        public readonly float DayProgress01;
        public readonly float Hour24;
        public readonly TimeOfDaySegment Segment;

        public GameClockSnapshot(int currentDay, float dayProgress01, float hour24, TimeOfDaySegment segment)
        {
            CurrentDay = currentDay;
            DayProgress01 = dayProgress01;
            Hour24 = hour24;
            Segment = segment;
        }
    }
}
