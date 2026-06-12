using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rpg.Core
{
    /// <summary>Loads StylizedCharacterPack character prefabs for runtime bootstrap NPC spawning.</summary>
    public static class BootstrapStylizedCharacterResources
    {
        static readonly List<GameObject> Cached = new();
        static readonly HashSet<int> CachedIds = new();

        public static IReadOnlyList<GameObject> GetAllCharacterPrefabs() => Cached;

        public static bool IsKnownPrefab(GameObject prefab)
            => prefab != null && CachedIds.Contains(prefab.GetInstanceID());

        public static void RefreshCache()
        {
            Cached.Clear();
            CachedIds.Clear();

            // Runtime-compatible path if user chooses to copy assets under Resources.
            var fromResources = Resources.LoadAll<GameObject>("StylizedCharacterPack/Characters");
            foreach (var go in fromResources)
                TryAdd(go);

            var lineupAsset = Resources.Load<StandaloneAvatarLineup>("Bootstrap/StandaloneAvatarLineup");
            if (lineupAsset != null && lineupAsset.lineupPrefabs != null)
                AppendPrefabs(lineupAsset.lineupPrefabs);

#if UNITY_EDITOR
            // Editor fallback: load directly from package path so no copy step is required.
            // Do not load Demo/Prefabs/Controller here: those are alternate controller rigs of the same
            // heroes as Prefabs/Characters and would duplicate every character in selection/spawn pools.
            AddEditorPrefabsAtPath("Assets/StylizedCharacterPack/Prefabs/Characters");
#endif
            if (Cached.Count == 0)
            {
                Debug.LogError(
                    "[BootstrapStylizedCharacterResources] No stylized character prefabs loaded. "
                    + "Player builds need Assets/Resources/StylizedCharacterPack/Characters (or Resources/Bootstrap/StandaloneAvatarLineup), "
                    + "or assign RuntimeLevelBootstrap.standaloneAvatarSelectionPrefabs.");
            }
        }

        /// <summary>
        /// Adds extra prefabs after <see cref="RefreshCache"/> (e.g. serialized bootstrap list for standalone builds).
        /// </summary>
        public static void AppendPrefabs(IEnumerable<GameObject> extras)
        {
            if (extras == null)
                return;
            foreach (var p in extras)
                TryAdd(p);
        }

#if UNITY_EDITOR
        static void AddEditorPrefabsAtPath(string path)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { path });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                TryAdd(prefab);
            }
        }
#endif

        static void TryAdd(GameObject prefab)
        {
            if (prefab == null)
                return;
            if (prefab.GetComponentInChildren<Animator>(true) == null)
                return;
            if (prefab.GetComponentInChildren<Renderer>(true) == null)
                return;
            foreach (var existing in Cached)
            {
                if (existing == null)
                    continue;
                if (string.Equals(existing.name, prefab.name, System.StringComparison.OrdinalIgnoreCase))
                    return;
            }
            var id = prefab.GetInstanceID();
            if (!CachedIds.Add(id))
                return;
            Cached.Add(prefab);
        }
    }
}
