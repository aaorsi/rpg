using System;

namespace Rpg.Npc
{
    [Serializable]
    public sealed class VillageConsequenceEvent
    {
        public string eventId;
        public string eventType;
        public double timestampUtc;
        public string actorNpcId;
        public string targetNpcId;
        public string actorDisplayName;
        public string targetDisplayName;
        public string rumorText;
        public float opinionDeltaTowardHero;
    }
}
