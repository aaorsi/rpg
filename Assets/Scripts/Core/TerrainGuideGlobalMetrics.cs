using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Core
{
    /// <summary>
    /// One-time terrain statistics for NPC guiding: 75th percentile of sampled world heights
    /// (used as a threshold to treat very high ground as undesirable for brute-force pathing).
    /// </summary>
    public static class TerrainGuideGlobalMetrics
    {
        const float WorldSampleStepMeters = 25f;

        public static float AvoidTerrainHeightWorldY { get; private set; }
        public static bool IsInitialized { get; private set; }

        /// <summary>Call once early in play; safe to call repeatedly.</summary>
        public static void EnsureInitialized()
        {
            if (IsInitialized)
                return;

            var heights = new List<float>(4096);
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.terrainData == null)
                    continue;
                var origin = t.transform.position;
                var size = t.terrainData.size;
                for (var x = 0f; x < size.x; x += WorldSampleStepMeters)
                {
                    for (var z = 0f; z < size.z; z += WorldSampleStepMeters)
                    {
                        var wxz = new Vector3(origin.x + x, 0f, origin.z + z);
                        heights.Add(t.SampleHeight(wxz) + origin.y);
                    }
                }
            }

            if (heights.Count == 0)
            {
                AvoidTerrainHeightWorldY = float.MaxValue;
                IsInitialized = true;
                return;
            }

            heights.Sort();
            var idx = Mathf.Clamp(Mathf.RoundToInt(0.75f * (heights.Count - 1)), 0, heights.Count - 1);
            AvoidTerrainHeightWorldY = heights[idx];
            IsInitialized = true;
        }

        public static bool TrySampleWorldTerrainHeight(Vector3 worldPos, out float heightY)
        {
            heightY = 0f;
            var best = float.MinValue;
            var found = false;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.terrainData == null)
                    continue;
                var o = t.transform.position;
                var s = t.terrainData.size;
                if (worldPos.x < o.x || worldPos.x > o.x + s.x || worldPos.z < o.z || worldPos.z > o.z + s.z)
                    continue;
                var y = t.SampleHeight(worldPos) + o.y;
                found = true;
                best = Mathf.Max(best, y);
            }

            if (!found)
                return false;
            heightY = best;
            return true;
        }

        public static bool IsTerrainAboveAvoidThreshold(Vector3 worldPos)
        {
            if (!IsInitialized || !TrySampleWorldTerrainHeight(worldPos, out var y))
                return false;
            return y > AvoidTerrainHeightWorldY;
        }
    }
}
