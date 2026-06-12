using System;
using System.Collections.Generic;
using System.Text;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Maps scene object names (e.g. <c>BottleV1 (Clone)</c>) to catalog item ids for click pickup when no <see cref="ItemPickup"/> is present.
    /// Only indexes the main <c>objects</c> list — not relic artifacts.
    /// </summary>
    public sealed class ScenePickableCatalogResolver
    {
        readonly Dictionary<string, string> _keyToItemId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ScenePickableCatalogResolver(NarrativeContentLibrary library = null)
        {
            var lib = library ?? new NarrativeContentLibrary();
            var doc = lib.LoadObjectArtifactCatalog();
            if (doc?.objects != null)
            {
                foreach (var e in doc.objects)
                    IndexEntry(e);
            }
        }

        void IndexEntry(CatalogEntry e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.id))
                return;
            var id = e.id.Trim();
            AddKey(AlphanumericKey(id), id);
            if (!string.IsNullOrWhiteSpace(e.label))
                AddKey(AlphanumericKey(e.label), id);
            if (!string.IsNullOrWhiteSpace(e.prefabHint))
            {
                var slash = e.prefabHint.Replace('\\', '/');
                var file = System.IO.Path.GetFileNameWithoutExtension(slash);
                if (!string.IsNullOrWhiteSpace(file))
                    AddKey(AlphanumericKey(file), id);
            }
        }

        void AddKey(string key, string itemId)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(itemId))
                return;
            if (!_keyToItemId.ContainsKey(key))
                _keyToItemId.Add(key, itemId);
        }

        /// <summary>
        /// Walk from the hit transform upward; first name token that matches a catalog object wins.
        /// </summary>
        public bool TryResolvePickableRoot(Transform hitTransform, out string itemId, out Transform pickupRoot)
        {
            itemId = null;
            pickupRoot = null;
            if (hitTransform == null)
                return false;
            for (var t = hitTransform; t != null; t = t.parent)
            {
                var name = t.gameObject.name;
                var key = AlphanumericKey(name);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (_keyToItemId.TryGetValue(key, out var resolved))
                {
                    itemId = resolved;
                    pickupRoot = t;
                    return true;
                }
            }

            return false;
        }

        public static string AlphanumericKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            var s = raw.Trim();
            var paren = s.IndexOf('(');
            if (paren >= 0)
                s = s.Substring(0, paren).TrimEnd();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }
    }
}
