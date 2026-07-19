using Rpg.Dialogue;
using Rpg.Npc;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Player
{
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] KeyCode interactKey = KeyCode.E;
        [SerializeField] float interactionDistance = 5f;

        [Tooltip("Optional. If true, interaction also requires NPC trigger overlap in addition to distance.")]
        [SerializeField] bool requireInTriggerRange;

        public void SetInteractionDistance(float value)
        {
            interactionDistance = Mathf.Max(0.1f, value);
        }

        void Update()
        {
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;
            if (Input.GetKeyDown(KeyCode.Escape) && DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
            {
                DialogueManager.Instance.EndDialogue();
                return;
            }

            if (DialogueManager.Instance == null)
                return;

            if (DialogueManager.Instance.IsDialogueOpen)
                return;

            if (!Input.GetKeyDown(interactKey))
                return;

            var simulation = Object.FindFirstObjectByType<VillageAgentSimulation>(FindObjectsInactive.Exclude);
            if (simulation != null
                && !simulation.IsSystemicOnlyMode
                && DialogueManager.Instance.TryStartNearbyInteractionDialogue(simulation, transform.position, interactionDistance))
                return;

            var candidate = ResolveNearestNpcCandidate();
            if (candidate.definition == null || candidate.npcTransform == null)
                return;

            if (!DialogueManager.Instance.TryStartDialogue(candidate.definition))
                return;
            var cam = Camera.main != null ? Camera.main.GetComponent<SliceFollowCamera>() : null;
            cam?.StartDialogueFocus(candidate.npcTransform);
        }

        (NpcDefinition definition, Transform npcTransform) ResolveNearestNpcCandidate()
        {
            NpcDefinition bestDef = null;
            Transform bestTransform = null;
            var bestDistSq = interactionDistance * interactionDistance;

            foreach (var binding in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude))
            {
                if (binding == null || binding.Definition == null)
                    continue;
                var delta = binding.transform.position - transform.position;
                delta.y = 0f;
                var sq = delta.sqrMagnitude;
                if (sq > bestDistSq)
                    continue;
                if (requireInTriggerRange && (NpcInteractable.Active == null || NpcInteractable.Active.GetNpcDefinition() != binding.Definition))
                    continue;
                bestDistSq = sq;
                bestDef = binding.Definition;
                bestTransform = binding.transform;
            }

            return (bestDef, bestTransform);
        }
    }
}
