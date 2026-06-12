using System.IO;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Spawns a world <see cref="ItemPickup"/> when the hero drops inventory; uses Resources when possible.
    /// </summary>
    public static class WorldItemDropSpawner
    {
        public static GameObject SpawnDroppedItem(string itemId, Vector3 worldPosition, NarrativeContentLibrary library = null)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;
            var lib = library ?? new NarrativeContentLibrary();
            var catalog = lib.LoadObjectArtifactCatalog();
            var hint = FindPrefabHint(catalog, itemId.Trim());
            var prefab = TryLoadGameObjectFromHint(hint) ?? TryLoadByItemId(itemId.Trim());

            GameObject root;
            if (prefab != null)
            {
                root = Object.Instantiate(prefab, worldPosition, Quaternion.identity);
                root.name = $"Dropped_{itemId}";
            }
            else
            {
                root = GameObject.CreatePrimitive(PrimitiveType.Cube);
                root.name = $"Dropped_{itemId}_Proxy";
                root.transform.position = worldPosition;
                root.transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
                var col = root.GetComponent<Collider>();
                if (col != null)
                    col.isTrigger = false;
            }

            var pickup = root.GetComponent<ItemPickup>() ?? root.AddComponent<ItemPickup>();
            pickup.Configure(itemId.Trim());
            if (root.GetComponentInChildren<Collider>(true) == null)
            {
                var sc = root.AddComponent<SphereCollider>();
                sc.radius = 0.35f;
            }

            return root;
        }

        static string FindPrefabHint(ObjectArtifactCatalogDoc catalog, string itemId)
        {
            if (catalog?.objects != null)
            {
                foreach (var e in catalog.objects)
                {
                    if (e != null && string.Equals(e.id, itemId, System.StringComparison.OrdinalIgnoreCase))
                        return e.prefabHint;
                }
            }

            if (catalog?.artifacts != null)
            {
                foreach (var e in catalog.artifacts)
                {
                    if (e != null && string.Equals(e.id, itemId, System.StringComparison.OrdinalIgnoreCase))
                        return e.prefabHint;
                }
            }

            return null;
        }

        static GameObject TryLoadGameObjectFromHint(string hintedPath)
        {
            if (string.IsNullOrWhiteSpace(hintedPath))
                return null;
            var normalized = hintedPath.Replace('\\', '/').Trim();
            if (normalized.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("Assets/".Length);
            if (normalized.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - ".prefab".Length);
            var go = Resources.Load<GameObject>(normalized);
            if (go != null)
                return go;
            var file = Path.GetFileNameWithoutExtension(hintedPath.Replace('\\', '/'));
            if (!string.IsNullOrWhiteSpace(file))
            {
                go = Resources.Load<GameObject>(file);
                if (go != null)
                    return go;
                go = Resources.Load<GameObject>("Medieval props/Prefabs/" + file);
                if (go != null)
                    return go;
            }

            return null;
        }

        static GameObject TryLoadByItemId(string itemId)
        {
            var go = Resources.Load<GameObject>(itemId);
            if (go != null)
                return go;
            go = Resources.Load<GameObject>("Medieval props/Prefabs/" + itemId);
            return go;
        }
    }
}
