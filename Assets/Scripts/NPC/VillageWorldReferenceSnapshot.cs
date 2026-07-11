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
        public int schemaVersion = 2;
        public float capturedAtTime;
        public List<string> npcIds = new List<string>();
        public List<string> locationIds = new List<string>();
        public List<string> workIds = new List<string>();
        public List<string> goalIds = new List<string>();
        public List<VillageNpcReferenceEntry> npcs = new List<VillageNpcReferenceEntry>();
        public List<VillageRelationshipReferenceEntry> relationships = new List<VillageRelationshipReferenceEntry>();
    }

    [Serializable]
    public sealed class VillageRelationshipReferenceEntry
    {
        public string subjectNpcId = string.Empty;
        public string objectNpcId = string.Empty;
        public string metric = string.Empty;
        public float value;
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
