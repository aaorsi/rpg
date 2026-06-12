using UnityEngine;

namespace Rpg.Core
{
    /// <summary>
    /// Disables third-party Animals FREE mover scripts while preserving required components.
    /// Disabling avoids dependency errors (e.g. MovePlayerInput requires CreatureMover).
    /// </summary>
    public static class ThirdPartyAnimalRig
    {
        const string IthappyNamespace = "ithappy.Animals_FREE";

        public static void StripForNavMeshAuthoring(GameObject root)
        {
            if (root == null)
                return;

            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                if (mb.GetType().Namespace == IthappyNamespace)
                    mb.enabled = false;
            }
        }
    }
}
