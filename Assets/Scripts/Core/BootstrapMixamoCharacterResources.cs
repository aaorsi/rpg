using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Core
{
    /// <summary>
    /// Loads humanoid prefabs from <c>Resources/{GameConstants.MixamoCharactersResourcesFolder}</c> for bootstrap random selection.
    /// Put Mixamo-derived prefabs here (recommended name prefix <c>mixamo_</c> for clarity).
    /// </summary>
    public static class BootstrapMixamoCharacterResources
    {
        static GameObject[] _cached;
        static HashSet<string> _cachedPrefabNames;

        public static IReadOnlyList<GameObject> GetAllCharacterPrefabs()
        {
            if (_cached == null)
                RefreshCache();
            return _cached;
        }

        public static void RefreshCache()
        {
            var loaded = Resources.LoadAll<GameObject>(GameConstants.MixamoCharactersResourcesFolder);
            if (loaded == null || loaded.Length == 0)
            {
                _cached = Array.Empty<GameObject>();
                _cachedPrefabNames = new HashSet<string>(StringComparer.Ordinal);
                return;
            }

            var list = new List<GameObject>(loaded.Length);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var go in loaded)
            {
                if (go == null)
                    continue;
                list.Add(go);
                names.Add(go.name);
            }

            _cached = list.Count > 0 ? list.ToArray() : Array.Empty<GameObject>();
            _cachedPrefabNames = names;
        }

        /// <summary>True if this asset was listed under the Mixamo Resources folder last refresh.</summary>
        public static bool IsKnownMixamoPrefab(GameObject prefab)
        {
            if (prefab == null)
                return false;
            if (_cachedPrefabNames == null)
                RefreshCache();
            return _cachedPrefabNames.Contains(prefab.name);
        }
    }
}
