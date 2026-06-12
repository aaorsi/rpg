using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Core
{
    /// <summary>
    /// Resolves shared locomotion clips from <c>Resources/{GameConstants.MixamoAnimationsResourcesFolder}</c>.
    /// Deterministic ordering keeps picks stable across sessions.
    /// </summary>
    public static class MixamoAnimationCatalog
    {
        public readonly struct Selection
        {
            public readonly AnimationClip IdleA;
            public readonly AnimationClip IdleB;
            public readonly AnimationClip Walk;

            public Selection(AnimationClip idleA, AnimationClip idleB, AnimationClip walk)
            {
                IdleA = idleA;
                IdleB = idleB;
                Walk = walk;
            }

            public bool IsValid => IdleA != null || IdleB != null || Walk != null;
        }

        static Selection? _cached;
        static List<AnimationClip> _allClipsSorted;

        public static Selection GetSelection()
        {
            if (_cached.HasValue)
                return _cached.Value;
            RefreshCache();
            return _cached ?? default;
        }

        public static void RefreshCache()
        {
            var list = new List<AnimationClip>(32);
            var modelAssets = Resources.LoadAll<GameObject>(GameConstants.MixamoAnimationsResourcesFolder);
            if (modelAssets != null && modelAssets.Length > 0)
            {
                foreach (var model in modelAssets)
                {
                    if (model == null)
                        continue;
                    var path = $"{GameConstants.MixamoAnimationsResourcesFolder}/{model.name}";
                    var clipsFromModel = Resources.LoadAll<AnimationClip>(path);
                    foreach (var c in clipsFromModel)
                    {
                        if (c == null || c.name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        if (!list.Contains(c))
                            list.Add(c);
                    }
                }
            }

            if (list.Count == 0)
            {
                var fallback = Resources.LoadAll<AnimationClip>(GameConstants.MixamoAnimationsResourcesFolder);
                foreach (var c in fallback)
                {
                    if (c == null || c.name.IndexOf("mask", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (!list.Contains(c))
                        list.Add(c);
                }
            }

            if (list.Count == 0)
            {
                _allClipsSorted = null;
                _cached = default;
                return;
            }

            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            _allClipsSorted = list;

            PickBestIdleWalkClips(list, out var idleA, out var idleB, out var walk);

            _cached = new Selection(idleA, idleB, walk);
        }

        /// <summary>Picks stable NPC-friendly idle/walk clips (prefers Idle1 + Walking, never idle==walk).</summary>
        static void PickBestIdleWalkClips(List<AnimationClip> list, out AnimationClip idleA, out AnimationClip idleB, out AnimationClip walk)
        {
            idleA = null;
            idleB = null;
            walk = null;

            idleA = FindClipByExactName(list, "Idle1")
                ?? FindClipByExactName(list, "Idle2");

            if (idleA == null)
            {
                foreach (var c in list)
                {
                    if (c == null)
                        continue;
                    var n = c.name.ToLowerInvariant();
                    if (n.Contains("idle") || n.Contains("stand") || n.Contains("breath"))
                    {
                        idleA = c;
                        break;
                    }
                }
            }

            if (idleA == null && list.Count > 0)
                idleA = list[0];

            walk = FindClipByExactName(list, "Walking")
                ?? FindClipByExactName(list, "Running");

            if (walk == null)
            {
                foreach (var c in list)
                {
                    if (c == null)
                        continue;
                    var n = c.name.ToLowerInvariant();
                    if (IsPreferredWalkClipName(n))
                    {
                        walk = c;
                        break;
                    }
                }
            }

            if (walk == null)
            {
                foreach (var c in list)
                {
                    if (c == null)
                        continue;
                    var n = c.name.ToLowerInvariant();
                    if (n.Contains("walk") || n.Contains("jog") || n.Contains("run") || n.Contains("move"))
                    {
                        walk = c;
                        break;
                    }
                }
            }

            if (walk == null)
                walk = idleA;

            if (walk != null && idleA != null && ReferenceEquals(walk, idleA))
            {
                foreach (var c in list)
                {
                    if (c == null || ReferenceEquals(c, idleA))
                        continue;
                    var n = c.name.ToLowerInvariant();
                    if (n.Contains("walk") || n.Contains("jog") || n.Contains("run"))
                    {
                        walk = c;
                        break;
                    }
                }
            }

            if (walk == null)
                walk = idleA;

            idleB = FindClipByExactName(list, "Idle2");
            if (idleB == null || ReferenceEquals(idleB, idleA))
            {
                idleB = idleA;
                foreach (var c in list)
                {
                    if (c == null || ReferenceEquals(c, idleA))
                        continue;
                    var n = c.name.ToLowerInvariant();
                    if (n.Contains("idle") || n.Contains("stand") || n.Contains("breath"))
                    {
                        idleB = c;
                        break;
                    }
                }
            }

            if (idleB == null)
                idleB = idleA;
        }

        static AnimationClip FindClipByExactName(List<AnimationClip> list, string exactName)
        {
            if (list == null || string.IsNullOrWhiteSpace(exactName))
                return null;
            foreach (var c in list)
            {
                if (c != null && string.Equals(c.name, exactName, StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            return null;
        }

        /// <summary>
        /// Resolves a clip by name under the Mixamo animations Resources folder.
        /// Prefers exact case-insensitive match, then first clip whose name contains the fragment.
        /// </summary>
        public static bool TryFindAnimationClipByName(string clipNameFragment, out AnimationClip clip)
        {
            clip = null;
            if (string.IsNullOrWhiteSpace(clipNameFragment))
                return false;

            RefreshCache();
            if (_allClipsSorted == null || _allClipsSorted.Count == 0)
                return false;

            var needle = clipNameFragment.Trim();
            AnimationClip containsMatch = null;
            foreach (var c in _allClipsSorted)
            {
                if (c == null || string.IsNullOrEmpty(c.name))
                    continue;
                if (string.Equals(c.name, needle, StringComparison.OrdinalIgnoreCase))
                {
                    clip = c;
                    return true;
                }
                if (containsMatch == null && c.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    containsMatch = c;
            }

            if (containsMatch != null)
            {
                clip = containsMatch;
                return true;
            }

            return false;
        }

        static bool IsPreferredWalkClipName(string lowerName)
        {
            if (string.IsNullOrWhiteSpace(lowerName))
                return false;
            var hasLocomotionKeyword =
                lowerName.Contains("walk")
                || lowerName.Contains("jog")
                || lowerName.Contains("run")
                || lowerName.Contains("move");
            if (!hasLocomotionKeyword)
                return false;

            // Exclude stylized/special-motion clips that can look wrong for regular NPC locomotion.
            if (lowerName.Contains("drunk")
                || lowerName.Contains("hit")
                || lowerName.Contains("defeat")
                || lowerName.Contains("dying")
                || lowerName.Contains("flying")
                || lowerName.Contains("angry")
                || lowerName.Contains("kick")
                || lowerName.Contains("jab")
                || lowerName.Contains("button")
                || lowerName.Contains("open"))
                return false;

            return true;
        }
    }
}
