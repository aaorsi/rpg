using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Core
{
    /// <summary>
    /// Loads <c>npc_csl_00_character_*</c> prefab copies from <c>Resources/{GameConstants.NpcCasualCharactersResourcesFolder}</c>.
    /// </summary>
    public static class BootstrapCasualCharacterResources
    {
        const string CharacterPrefix = "npc_csl_00_character_";

        static GameObject[] _cached;
        static HashSet<string> _cachedNames;

        public static IReadOnlyList<GameObject> GetAllCharacterPrefabs()
        {
            if (_cached == null)
                RefreshCache();
            return _cached;
        }

        public static void RefreshCache()
        {
            var loaded = Resources.LoadAll<GameObject>(GameConstants.NpcCasualCharactersResourcesFolder);
            if (loaded == null || loaded.Length == 0)
            {
                _cached = Array.Empty<GameObject>();
                _cachedNames = new HashSet<string>(StringComparer.Ordinal);
                return;
            }

            var list = new List<GameObject>(loaded.Length);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var go in loaded)
            {
                if (go != null && go.name.StartsWith(CharacterPrefix, StringComparison.Ordinal))
                {
                    list.Add(go);
                    names.Add(go.name);
                }
            }

            _cached = list.Count > 0 ? list.ToArray() : Array.Empty<GameObject>();
            _cachedNames = names;
        }

        public static bool IsKnownPrefab(GameObject prefab)
        {
            if (prefab == null)
                return false;
            if (_cachedNames == null)
                RefreshCache();
            return _cachedNames.Contains(prefab.name);
        }
    }
}
