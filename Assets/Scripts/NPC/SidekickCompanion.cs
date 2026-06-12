using UnityEngine;

namespace Rpg.Npc
{
    /// <summary>
    /// Marker for runtime sidekick NPCs that can be asked to follow the hero.
    /// </summary>
    public sealed class SidekickCompanion : MonoBehaviour
    {
        public static SidekickCompanion FindForNpcBindingRoot(GameObject bindingRoot)
        {
            if (bindingRoot == null)
                return null;
            var c = bindingRoot.GetComponent<SidekickCompanion>();
            if (c != null)
                return c;
            c = bindingRoot.GetComponentInParent<SidekickCompanion>();
            if (c != null)
                return c;
            return bindingRoot.GetComponentInChildren<SidekickCompanion>(true);
        }

        /// <summary>Character root that actually owns the NPC <see cref="CharacterController"/> for locomotion.</summary>
        public static GameObject ResolveLocomotionRoot(GameObject bindingRoot)
        {
            if (bindingRoot == null)
                return null;
            if (bindingRoot.GetComponent<CharacterController>() != null)
                return bindingRoot;
            var cc = bindingRoot.GetComponentInChildren<CharacterController>(true);
            return cc != null ? cc.gameObject : bindingRoot;
        }

        public static bool BindingRootHasSidekick(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return false;
            foreach (var b in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null)
                    continue;
                if (!string.Equals(b.Definition.npcId, npcId, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                return FindForNpcBindingRoot(b.gameObject) != null;
            }

            return false;
        }
    }
}
