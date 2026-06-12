using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>Links a scene NPC to authored <see cref="NpcDefinition"/> data.</summary>
    public sealed class NpcDialogueBinding : MonoBehaviour
    {
        [SerializeField] NpcDefinition definition;

        public NpcDefinition Definition => definition;

        public void SetDefinition(NpcDefinition def) => definition = def;
    }
}
