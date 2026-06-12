using UnityEngine;

namespace Rpg.Npc
{
    [RequireComponent(typeof(Collider))]
    public sealed class NpcInteractable : MonoBehaviour
    {
        public static NpcInteractable Active { get; private set; }

        NpcDialogueBinding _binding;

        void Awake()
        {
            _binding = GetComponent<NpcDialogueBinding>();
            if (_binding == null)
                _binding = GetComponentInParent<NpcDialogueBinding>();
            var c = GetComponent<Collider>();
            c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(Rpg.Core.GameConstants.PlayerTag))
                return;
            Active = this;
        }

        void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(Rpg.Core.GameConstants.PlayerTag))
                return;
            if (Active == this)
                Active = null;
        }

        public NpcDefinition GetNpcDefinition() => _binding != null ? _binding.Definition : null;
    }
}
