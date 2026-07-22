using UnityEngine;

namespace Rpg.Npc
{
    [CreateAssetMenu(fileName = "NpcDefinition", menuName = "RPG/NPC Definition")]
    public sealed class NpcDefinition : ScriptableObject
    {
        public string npcId = "npc_guide";
        public string displayName = "Guide";

        [TextArea(3, 10)] public string roleSummary =
            "You are a calm local guide who helps newcomers orient themselves.";

        [TextArea(3, 10)] public string toneAndVocabulary =
            "Warm, concise, slightly formal. Short paragraphs. Ask friendly questions back when appropriate.";

        [TextArea(4, 14)] public string safetyRules =
            "Never invent world facts outside the FACTS block.\n" +
            "Never output anything except the JSON object schema described in the system template.";

        [Tooltip("If set, overrides OllamaSettings.model for this NPC.")]
        public string ollamaModelOverride = "";

        [TextArea(2, 6)] public string openingLine =
            "Hello there — you’re awake. I’m your guide. Who are you, and how are you feeling?";

        [TextArea(2, 5)] public string[] fallbackLines =
        {
            "I need a moment to think… try again?",
            "Sorry — my thoughts scattered. What did you ask?"
        };

        [Tooltip("Option A: role-sensitive prompt scaffolding.")]
        public NpcDialogueRole dialogueRole = NpcDialogueRole.Default;
    }
}
