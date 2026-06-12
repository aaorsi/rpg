using UnityEngine;

namespace Rpg.Gameplay
{
    /// <summary>
    /// Disables every <see cref="Collider"/> under this castle root (avoids BoxCollider vs negative scale issues).
    /// </summary>
    public sealed class CastleColliderDisabler : MonoBehaviour
    {
        void Awake()
        {
            foreach (var col in GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }
    }
}
