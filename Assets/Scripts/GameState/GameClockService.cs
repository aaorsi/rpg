using UnityEngine;

namespace Rpg.GameState
{
    public sealed class GameClockService : MonoBehaviour
    {
        const double SecondsPerGameDay = 24d * 60d * 60d;
        const float MinHour = 0f;
        const float MaxHourExclusive = 24f;

        [SerializeField] float gameSecondsPerRealSecond = 300f;
        [SerializeField] int startingDay = 1;
        [SerializeField] float startingHour24 = 8f;
        [SerializeField] bool autoAdvance = true;

        double absoluteGameSeconds;
        bool initialized;

        public float GameSecondsPerRealSecond
        {
            get => gameSecondsPerRealSecond;
            set => gameSecondsPerRealSecond = Mathf.Max(0f, value);
        }

        public bool AutoAdvance
        {
            get => autoAdvance;
            set => autoAdvance = value;
        }

        public int CurrentDay
        {
            get
            {
                EnsureInitialized();
                return Mathf.Max(1, (int)(absoluteGameSeconds / SecondsPerGameDay) + 1);
            }
        }

        public float CurrentHour24
        {
            get
            {
                EnsureInitialized();
                return (float)(GetSecondsIntoCurrentDay() / 3600d);
            }
        }

        public float DayProgress01
        {
            get
            {
                EnsureInitialized();
                return (float)(GetSecondsIntoCurrentDay() / SecondsPerGameDay);
            }
        }

        public TimeOfDaySegment CurrentSegment => ResolveSegment(CurrentHour24);

        void Awake()
        {
            EnsureInitialized();
        }

        void Update()
        {
            if (!autoAdvance || gameSecondsPerRealSecond <= 0f)
                return;
            AdvanceByRealSeconds(Time.deltaTime);
        }

        public GameClockSnapshot GetSnapshot() =>
            new GameClockSnapshot(CurrentDay, DayProgress01, CurrentHour24, CurrentSegment);

        public void AdvanceByRealSeconds(float realSeconds)
        {
            if (realSeconds <= 0f || gameSecondsPerRealSecond <= 0f)
                return;
            AdvanceByGameSeconds(realSeconds * gameSecondsPerRealSecond);
        }

        public void AdvanceByGameSeconds(float gameSeconds)
        {
            if (gameSeconds <= 0f)
                return;
            EnsureInitialized();
            absoluteGameSeconds += gameSeconds;
        }

        public void SetClockState(int day, float hour24)
        {
            initialized = true;
            var safeDay = Mathf.Max(1, day);
            var normalizedHour = NormalizeHour(hour24);
            absoluteGameSeconds = (safeDay - 1) * SecondsPerGameDay + normalizedHour * 3600d;
        }

        public static TimeOfDaySegment ResolveSegment(float hour24)
        {
            var hour = NormalizeHour(hour24);
            if (hour < 5f)
                return TimeOfDaySegment.Night;
            if (hour < 8f)
                return TimeOfDaySegment.Dawn;
            if (hour < 12f)
                return TimeOfDaySegment.Morning;
            if (hour < 17f)
                return TimeOfDaySegment.Afternoon;
            if (hour < 21f)
                return TimeOfDaySegment.Evening;
            return TimeOfDaySegment.Night;
        }

        void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;
            SetClockState(startingDay, startingHour24);
        }

        double GetSecondsIntoCurrentDay()
        {
            var secondsIntoDay = absoluteGameSeconds % SecondsPerGameDay;
            if (secondsIntoDay < 0d)
                secondsIntoDay += SecondsPerGameDay;
            return secondsIntoDay;
        }

        static float NormalizeHour(float hour24)
        {
            if (hour24 < MinHour)
                return MinHour;
            if (hour24 >= MaxHourExclusive)
                return Mathf.Repeat(hour24, MaxHourExclusive);
            return hour24;
        }
    }
}
