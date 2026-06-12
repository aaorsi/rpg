using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Core
{
    /// <summary>
    /// Loads Animals FREE prefab copies from <c>Resources/{GameConstants.AnimalsFreeResourcesFolder}</c> for runtime bootstrap.
    /// </summary>
    public static class BootstrapAnimalResources
    {
        static GameObject[] _cached;

        /// <summary>All animal prefabs in the Resources folder (may be empty before assets are imported).</summary>
        public static IReadOnlyList<GameObject> GetAllAnimalPrefabs()
        {
            if (_cached == null)
                RefreshCache();
            return _cached;
        }

        public static void RefreshCache()
        {
            var loaded = Resources.LoadAll<GameObject>(GameConstants.AnimalsFreeResourcesFolder);
            if (loaded == null || loaded.Length == 0)
            {
                _cached = Array.Empty<GameObject>();
                return;
            }

            var list = new List<GameObject>(loaded.Length);
            foreach (var go in loaded)
            {
                if (go != null && go.name.EndsWith("_001", StringComparison.Ordinal))
                    list.Add(go);
            }

            _cached = list.Count > 0 ? list.ToArray() : Array.Empty<GameObject>();
        }
    }
}
