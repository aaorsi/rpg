using UnityEngine;

namespace Rpg.GameState
{
    public sealed class WorldStateService : MonoBehaviour
    {
        [SerializeField] int currentYear = 2847;
        [SerializeField] bool playerAcknowledgedYear;
        [SerializeField] GameClockService gameClock;

        public int CurrentYear => currentYear;
        public bool PlayerAcknowledgedYear => playerAcknowledgedYear;
        public GameClockService GameClock => ResolveClock();

        void Awake()
        {
            ResolveClock();
        }

        public WorldStateSnapshot GetSnapshot()
        {
            var clock = ResolveClock();
            if (clock == null)
                return new WorldStateSnapshot(currentYear);
            return new WorldStateSnapshot(currentYear, clock.GetSnapshot());
        }

        public void MarkPlayerAcknowledgedYear()
        {
            playerAcknowledgedYear = true;
        }

#if UNITY_EDITOR
        public void SetCurrentYearForTests(int year) => currentYear = year;
        public void SetGameClockForTests(GameClockService clock) => gameClock = clock;
#endif

        GameClockService ResolveClock()
        {
            if (gameClock == null)
                gameClock = GetComponent<GameClockService>();
            return gameClock;
        }
    }
}
