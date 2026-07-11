using System;
using System.Collections.Generic;

namespace Rpg.Npc
{
    /// <summary>
    /// Authoritative world-reference snapshot from Unity for deliberation and interaction validation (#42).
    /// </summary>
    [Serializable]
    public sealed class VillageWorldReferenceSnapshot
    {
        public int schemaVersion = 1;
        public float capturedAtTime;
        public List<string> npcIds = new List<string>();
        public List<string> locationIds = new List<string>();
        public List<string> workIds = new List<string>();
        public List<VillageNpcReferenceEntry> npcs = new List<VillageNpcReferenceEntry>();
    }

    [Serializable]
    public sealed class VillageNpcReferenceEntry
    {
        public string npcId = string.Empty;
        public string displayName = string.Empty;
        public float positionX;
        public float positionY;
        public float positionZ;
    }
}
