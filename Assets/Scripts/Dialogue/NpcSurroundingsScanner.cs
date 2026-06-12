using System;
using System.Collections.Generic;
using Rpg.Core;
using Rpg.Npc;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Builds a compact text list of notable objects within a radius of an NPC for dialogue system prompts.
    /// Recomputed whenever the prompt is built (each player line while in dialogue).
    /// </summary>
    public static class NpcSurroundingsScanner
    {
        public const float DefaultRadiusMeters = 50f;
        const int MaxOverlapColliders = 256;
        const int MaxEntriesPerCategory = 24;

        public static string BuildPromptBlock(string npcId, float radiusMeters = DefaultRadiusMeters)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return "(Surroundings: no npc id.)";
            if (!TryResolveNpcTransform(npcId.Trim(), out var selfBinding, out var origin))
                return "(Surroundings: this NPC has no NpcDialogueBinding in the scene; cannot sample.)";

            var selfGo = selfBinding.gameObject;
            var npcLines = new List<Entry>();
            var itemLines = new List<Entry>();
            var buildingLines = new List<Entry>();
            var heroLine = default(Entry?);

            foreach (var b in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null)
                    continue;
                var otherId = b.Definition.npcId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(otherId) || string.Equals(otherId, npcId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var d = Vector3.Distance(origin, b.transform.position);
                if (d > radiusMeters)
                    continue;
                var label = string.IsNullOrWhiteSpace(b.Definition.displayName) ? otherId : b.Definition.displayName.Trim();
                npcLines.Add(new Entry(d, $"NPC: {label} (npcId={otherId})"));
            }

            npcLines.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var player = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (player != null)
            {
                var d = Vector3.Distance(origin, player.transform.position);
                if (d <= radiusMeters)
                    heroLine = new Entry(d, "Hero (player character) is nearby.");
            }

            var seenRoots = new HashSet<int>();
            var buffer = new Collider[MaxOverlapColliders];
            var hitCount = Physics.OverlapSphereNonAlloc(origin, radiusMeters, buffer, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hitCount; i++)
            {
                var col = buffer[i];
                if (col == null)
                    continue;
                var root = col.transform.root.gameObject;
                if (root == null)
                    continue;
                if (root == selfGo)
                    continue;
                var id = root.GetInstanceID();
                if (!seenRoots.Add(id))
                    continue;
                if (IsUnderTransform(root.transform, selfBinding.transform))
                    continue;

                var center = col.bounds.center;
                var d = Vector3.Distance(origin, center);
                if (d > radiusMeters + 3f)
                    continue;

                var rootName = root.name ?? string.Empty;
                if (root.CompareTag("Player"))
                    continue;

                if (root.GetComponentInChildren<NpcDialogueBinding>(true) != null)
                    continue;

                if (IsLikelyWorldItem(rootName) || IsUnderInventoryItemsRoot(root.transform))
                {
                    itemLines.Add(new Entry(d, $"Item / object: {FormatWorldItemLabel(root, rootName)}"));
                    continue;
                }

                if (IsLikelyBuildingOrLargeStructure(rootName))
                    buildingLines.Add(new Entry(d, $"Building / structure: {rootName.Trim()}"));
            }

            itemLines.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            buildingLines.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Radius: ~{radiusMeters:F0} m from this NPC's position (refreshed each dialogue turn).");
            if (heroLine.HasValue)
                sb.AppendLine("- " + heroLine.Value.Line);

            AppendCapped(sb, "Other NPCs", npcLines);
            AppendCapped(sb, "Items and notable objects", itemLines);
            AppendCapped(sb, "Buildings and large structures", buildingLines);

            if (npcLines.Count == 0 && itemLines.Count == 0 && buildingLines.Count == 0 && !heroLine.HasValue)
                sb.AppendLine("(Nothing notable detected in range — sparse colliders or empty area.)");

            return sb.ToString().TrimEnd();
        }

        readonly struct Entry
        {
            public readonly float Distance;
            public readonly string Line;

            public Entry(float distance, string line)
            {
                Distance = distance;
                Line = line + $" (~{distance:F1} m)";
            }
        }

        static void AppendCapped(System.Text.StringBuilder sb, string title, List<Entry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;
            sb.AppendLine(title + ":");
            var n = Mathf.Min(entries.Count, MaxEntriesPerCategory);
            for (var i = 0; i < n; i++)
                sb.AppendLine("- " + entries[i].Line);
            if (entries.Count > MaxEntriesPerCategory)
                sb.AppendLine($"- …and {entries.Count - MaxEntriesPerCategory} more (omitted).");
        }

        static bool TryResolveNpcTransform(string npcId, out NpcDialogueBinding binding, out Vector3 worldPosition)
        {
            binding = null;
            worldPosition = default;
            foreach (var b in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null)
                    continue;
                var id = b.Definition.npcId?.Trim() ?? string.Empty;
                if (!string.Equals(id, npcId, StringComparison.OrdinalIgnoreCase))
                    continue;
                binding = b;
                worldPosition = b.transform.position;
                return true;
            }
            return false;
        }

        static bool IsUnderTransform(Transform child, Transform ancestor)
        {
            if (child == null || ancestor == null)
                return false;
            for (var t = child; t != null; t = t.parent)
            {
                if (t == ancestor)
                    return true;
            }
            return false;
        }

        static bool IsLikelyWorldItem(string rootName)
        {
            if (string.IsNullOrWhiteSpace(rootName))
                return false;
            var n = rootName.Trim();
            if (n.StartsWith("Item_", StringComparison.OrdinalIgnoreCase))
                return true;
            return n.IndexOf("pickup", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsUnderInventoryItemsRoot(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
            {
                if (string.Equals(p.gameObject.name, "_NpcInventoryItems", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static string FormatWorldItemLabel(GameObject root, string rootName)
        {
            if (rootName.StartsWith("Item_", StringComparison.OrdinalIgnoreCase))
            {
                var parts = rootName.Split('_');
                if (parts.Length >= 3)
                    return $"{rootName.Trim()} (item id hint: {parts[2]})";
            }
            return rootName.Trim();
        }

        static bool IsLikelyBuildingOrLargeStructure(string rootName)
        {
            if (string.IsNullOrWhiteSpace(rootName))
                return false;
            var n = rootName.ToLowerInvariant();
            if (n.Contains("terrain") || n == "floor" || n.Contains("bootstrap") || n.Contains("camera")
                || n.Contains("light") || n.Contains("canvas") || n.Contains("managers"))
                return false;
            if (n.Contains("building") || n.Contains("warehouse") || n.Contains("house") || n.Contains("tower")
                || n.Contains("castle") || n.Contains("temple") || n.Contains("church") || n.Contains("inn")
                || n.Contains("stable") || n.Contains("barn"))
                return true;
            if (n.StartsWith("rpgpp_lt_building", StringComparison.OrdinalIgnoreCase))
                return true;
            if (n.Contains("wagon") || n.Contains("well") || n.Contains("windmill"))
                return true;
            return false;
        }
    }
}
