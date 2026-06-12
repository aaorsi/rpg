using UnityEngine;

namespace Rpg.GameState
{
    public sealed class WorldStateService : MonoBehaviour
    {
        [SerializeField] int currentYear = 2847;
        [SerializeField] bool playerAcknowledgedYear;

        public int CurrentYear => currentYear;
        public bool PlayerAcknowledgedYear => playerAcknowledgedYear;

        public WorldStateSnapshot GetSnapshot() => new WorldStateSnapshot(currentYear);

        public void MarkPlayerAcknowledgedYear()
        {
            playerAcknowledgedYear = true;
        }

#if UNITY_EDITOR
        public void SetCurrentYearForTests(int year) => currentYear = year;
#endif
    }
}
