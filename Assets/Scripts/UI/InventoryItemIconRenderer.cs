using System;
using System.Collections.Generic;
using Rpg.Dialogue;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rpg.UI
{
    /// <summary>
    /// Builds and caches sprite thumbnails for inventory items by rendering their prefabs.
    /// </summary>
    public sealed class InventoryItemIconRenderer
    {
        readonly Dictionary<string, Sprite> _iconCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _prefabHintById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _labelById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public InventoryItemIconRenderer()
        {
            var library = new NarrativeContentLibrary();
            var catalog = library.LoadObjectArtifactCatalog();
            IndexCatalogEntries(catalog?.objects);
            IndexCatalogEntries(catalog?.artifacts);
        }

        public Sprite GetOrCreate(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;
            var key = itemId.Trim();
            if (_iconCache.TryGetValue(key, out var cached))
                return cached;
            var prefab = ResolveItemPrefab(key);
            var sprite = prefab != null ? RenderPrefabIcon(prefab) : null;
            if (sprite == null)
                sprite = GetOrCreateFallbackSprite();
            _iconCache[key] = sprite;
            return sprite;
        }

        void IndexCatalogEntries(List<CatalogEntry> entries)
        {
            if (entries == null)
                return;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.id))
                    continue;
                var id = e.id.Trim();
                _prefabHintById[id] = e.prefabHint ?? string.Empty;
                _labelById[id] = string.IsNullOrWhiteSpace(e.label) ? id : e.label.Trim();
            }
        }

        GameObject ResolveItemPrefab(string itemId)
        {
            _prefabHintById.TryGetValue(itemId, out var hint);
            var hintedAssetPath = string.IsNullOrWhiteSpace(hint) ? null : hint.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(hintedAssetPath))
            {
                var resourcesPath = EditorOrRuntimeResourcesPathFromHint(hintedAssetPath);
                if (!string.IsNullOrWhiteSpace(resourcesPath))
                {
                    var resGo = Resources.Load<GameObject>(resourcesPath);
                    if (resGo != null)
                        return resGo;
                }

#if UNITY_EDITOR
                var editorPath = RemapLegacyAssetPathForEditor(hintedAssetPath);
                var direct = AssetDatabase.LoadAssetAtPath<GameObject>(editorPath);
                if (direct != null)
                    return direct;
#endif
            }

            var candidates = new List<string>();
            if (_labelById.TryGetValue(itemId, out var label) && !string.IsNullOrWhiteSpace(label))
                candidates.Add(label);
            candidates.Add(itemId);
            if (!string.IsNullOrWhiteSpace(hintedAssetPath))
            {
                var file = System.IO.Path.GetFileNameWithoutExtension(hintedAssetPath);
                if (!string.IsNullOrWhiteSpace(file))
                    candidates.Add(file);
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (string.IsNullOrWhiteSpace(c))
                    continue;
                var t = c.Trim();
                var r1 = Resources.Load<GameObject>(t);
                if (r1 != null) return r1;
                var r2 = Resources.Load<GameObject>("Medieval props/Prefabs/" + t);
                if (r2 != null) return r2;
                var r3 = Resources.Load<GameObject>("Prefabs/" + t);
                if (r3 != null) return r3;
#if UNITY_EDITOR
                var guids = AssetDatabase.FindAssets(t + " t:prefab");
                if (guids != null && guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (loaded != null)
                        return loaded;
                }
#endif
            }

            return null;
        }

        /// <summary>
        /// Converts catalog paths like <c>Assets/Medieval props/Prefabs/X.prefab</c> to a <see cref="Resources"/> key
        /// (prefabs must live under a <c>Resources</c> folder to load in player builds).
        /// </summary>
        static string EditorOrRuntimeResourcesPathFromHint(string hintedAssetPath)
        {
            var p = hintedAssetPath.Trim().Replace('\\', '/');
            if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring("Assets/".Length);
            if (p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                p = p.Substring(0, p.Length - ".prefab".Length);
            var idx = p.IndexOf("Resources/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return p.Substring(idx + "Resources/".Length);
            return p;
        }

#if UNITY_EDITOR
        static string RemapLegacyAssetPathForEditor(string hintedAssetPath)
        {
            var p = hintedAssetPath.Trim().Replace('\\', '/');
            if (p.StartsWith("Assets/Medieval props/", StringComparison.OrdinalIgnoreCase))
                return "Assets/Resources/Medieval props/" + p.Substring("Assets/Medieval props/".Length);
            if (p.StartsWith("Assets/Books/", StringComparison.OrdinalIgnoreCase))
                return "Assets/Resources/Books/" + p.Substring("Assets/Books/".Length);
            return p;
        }
#endif

        Sprite _fallbackItemSprite;

        Sprite GetOrCreateFallbackSprite()
        {
            if (_fallbackItemSprite != null)
                return _fallbackItemSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var c0 = new Color(0.22f, 0.26f, 0.32f, 1f);
            var c1 = new Color(0.32f, 0.38f, 0.46f, 1f);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                tex.SetPixel(x, y, (x + y) % 8 < 4 ? c0 : c1);
            tex.Apply(false, false);
            _fallbackItemSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _fallbackItemSprite;
        }

        static Sprite RenderPrefabIcon(GameObject prefab)
        {
            const int size = 128;
            var root = new GameObject("InventoryIconRenderRoot");
            root.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var camGo = new GameObject("Cam");
                camGo.transform.SetParent(root.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.orthographic = false;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.fieldOfView = 30f;

                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(root.transform, false);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.15f;
                light.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

                var inst = UnityEngine.Object.Instantiate(prefab, root.transform, false);
                inst.SetActive(true);
                var renderers = inst.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0)
                    return null;
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        bounds.Encapsulate(renderers[i].bounds);
                }

                var center = bounds.center;
                var radius = Mathf.Max(0.05f, bounds.extents.magnitude);
                var dir = new Vector3(1f, 0.5f, -1f).normalized;
                cam.transform.position = center + dir * (radius * 2.6f);
                cam.transform.LookAt(center);

                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
                rt.Create();
                var prevActive = RenderTexture.active;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;

                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply(false, false);

                cam.targetTexture = null;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.Destroy(rt);

                return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }
            catch
            {
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
            }
        }
    }
}
