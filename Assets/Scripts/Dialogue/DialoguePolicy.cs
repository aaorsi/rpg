using UnityEngine;

namespace Rpg.Dialogue
{
    [CreateAssetMenu(fileName = "DialoguePolicy", menuName = "RPG/Dialogue Policy")]
    public sealed class DialoguePolicy : ScriptableObject
    {
        [Min(1)] public int maxRecentTurnPairs = 6;
        [Min(1)] public int maxPlayerCharacters = 800;
    }
}
