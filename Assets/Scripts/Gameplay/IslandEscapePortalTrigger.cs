using Rpg.Core;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Gameplay
{
    /// <summary>
    /// Win condition: when the object tagged <see cref="GameConstants.PlayerTag"/> enters this trigger,
    /// shows the escape victory UI via <see cref="GameOverController.TriggerPlayerVictory"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IslandEscapePortalTrigger : MonoBehaviour
    {
        void Awake()
        {
            if (!TryGetComponent<Collider>(out var c))
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(8f, 10f, 8f);
            }
            else
                c.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            var playerRoot = ResolvePlayerRoot(other);
            if (playerRoot == null)
                return;
            var ui = GameOverController.Instance;
            if (ui == null)
                return;
            ui.TriggerPlayerVictory(playerRoot);
        }

        static GameObject ResolvePlayerRoot(Collider other)
        {
            if (other == null)
                return null;
            var t = other.transform;
            while (t != null)
            {
                if (t.CompareTag(GameConstants.PlayerTag))
                    return t.gameObject;
                t = t.parent;
            }

            return null;
        }
    }
}
