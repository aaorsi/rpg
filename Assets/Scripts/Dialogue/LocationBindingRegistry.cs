using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Dialogue
{
    public sealed class LocationBindingRegistry
    {
        readonly Dictionary<string, Transform> _byId = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _sceneAnchorNameByLocationId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public LocationBindingRegistry(LocationCatalogDoc catalog)
        {
            if (catalog?.locations == null)
                return;
            foreach (var loc in catalog.locations)
            {
                if (loc == null || string.IsNullOrWhiteSpace(loc.id))
                    continue;
                var id = loc.id.Trim();
                if (!string.IsNullOrWhiteSpace(loc.sceneAnchorName))
                    _sceneAnchorNameByLocationId[id] = loc.sceneAnchorName.Trim();
                var found = TryFindTransformByObjectName(loc.sceneAnchorName);
                if (found != null)
                    _byId[id] = found;
            }
        }

        static Transform TryFindTransformByObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (string.Equals(t.gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
        }

        /// <summary>All location ids from the catalog (for narrative validation), not only bound anchors.</summary>
        public IEnumerable<string> AllCatalogLocationIds() => _sceneAnchorNameByLocationId.Keys;

        public bool TryResolve(string locationId, out Transform anchor)
        {
            anchor = null;
            if (string.IsNullOrWhiteSpace(locationId))
                return false;
            var key = locationId.Trim();
            if (_byId.TryGetValue(key, out anchor) && anchor != null)
                return true;
            if (!_sceneAnchorNameByLocationId.TryGetValue(key, out var anchorName))
                return false;
            var found = TryFindTransformByObjectName(anchorName);
            if (found == null)
                found = TryFindTransformByAnchorFallback(key, anchorName);
            if (found == null)
                return false;
            _byId[key] = found;
            anchor = found;
            return true;
        }

        static Transform TryFindTransformByAnchorFallback(string locationId, string primaryAnchorName)
        {
            if (string.IsNullOrWhiteSpace(primaryAnchorName))
                return null;
            if (string.Equals(locationId?.Trim(), "wagon_site", StringComparison.OrdinalIgnoreCase)
                || string.Equals(primaryAnchorName.Trim(), "rpgpp_lt_wagon_01", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t == null || t.gameObject == null)
                        continue;
                    var n = t.gameObject.name ?? string.Empty;
                    if (n.IndexOf("wagon", StringComparison.OrdinalIgnoreCase) >= 0)
                        return t;
                }
            }

            return null;
        }

        public IEnumerable<string> AllIds() => _byId.Keys;
    }
}
