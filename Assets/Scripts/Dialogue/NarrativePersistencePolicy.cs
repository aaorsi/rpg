using UnityEngine;

namespace Rpg.Dialogue
{
    [CreateAssetMenu(fileName = "NarrativePersistencePolicy", menuName = "RPG/Narrative Persistence Policy")]
    public sealed class NarrativePersistencePolicy : ScriptableObject
    {
        public bool clearCanonOnPlay = true;
        public bool clearInventoryOnPlay = true;
        public bool clearQuestStateOnPlay = true;
        public bool clearNpcMemoryOnPlay = true;
        public bool clearNpcTranscriptsOnPlay = true;
        public bool clearNpcSummariesOnPlay;
    }
}
