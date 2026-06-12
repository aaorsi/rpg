using UnityEngine;
using Rpg.Dialogue;

namespace Rpg.Npc
{
    /// <summary>Links a scene NPC to authored <see cref="NpcDefinition"/> data.</summary>
    public sealed class NpcDialogueBinding : MonoBehaviour
    {
        [SerializeField] NpcDefinition definition;
        [SerializeField] string personaId;
        [SerializeField, TextArea(2, 6)] string personaPersonality;

        NpcPersona _persona;

        public NpcDefinition Definition => definition;
        public string PersonaId => personaId;
        public string PersonaPersonality => personaPersonality;
        public NpcPersona Persona => _persona;

        public void SetDefinition(NpcDefinition def) => definition = def;

        public void SetPersona(NpcPersona persona)
        {
            _persona = persona;
            personaId = persona != null ? persona.personaId ?? string.Empty : string.Empty;
            personaPersonality = persona != null ? persona.personality ?? string.Empty : string.Empty;
        }
    }
}
