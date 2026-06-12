using System;
using System.Collections.Generic;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class NarrativeSessionCanon
    {
        public int schemaVersion = 1;
        public string sessionId;
        public int seed;
        public string premiseId;
        public string worldId;
        public string summary;
        public List<string> openingIntroLines = new List<string>();
        public string worldBackstory;
        public List<string> globalKnowledge = new List<string>();
        public string finalObjective;
        public List<string> victorySequence = new List<string>();
        public List<string> criticalMilestones = new List<string>();
        public Dictionary<string, int> routesByMilestone = new Dictionary<string, int>();
        public List<TradeRequirementEntry> tradeRequirements = new List<TradeRequirementEntry>();
        public List<GeneratedNpcProfile> npcProfiles = new List<GeneratedNpcProfile>();
    }

    [Serializable]
    public sealed class GeneratedNpcProfile
    {
        public string npcId;
        public string name;
        public string npcType;
        public string race;
        public string sex;
        public string age;
        public string occupation;
        public string personality;
        public Dictionary<string, string> socialTraits = new Dictionary<string, string>();
        public List<string> keyInformation = new List<string>();
        public List<string> goals = new List<string>();
        public List<string> capabilities = new List<string>();
        public List<string> followerRecruitmentRequirements = new List<string>();
    }

    [Serializable]
    public sealed class NarrativeGenerationRequest
    {
        public int seed;
        public string premiseId;
        public List<string> npcIds = new List<string>();
    }

    [Serializable]
    public sealed class GlobalKnowledgeDoc
    {
        public int schemaVersion = 1;
        public string worldId;
        public string title;
        public string openingBackstory;
        public List<string> sharedFacts = new List<string>();
    }

    [Serializable]
    public sealed class IntroPremiseLibraryDoc
    {
        public int schemaVersion = 1;
        public List<IntroPremiseEntry> premises = new List<IntroPremiseEntry>();
    }

    [Serializable]
    public sealed class IntroPremiseEntry
    {
        public string id;
        public string title;
        public List<string> introLines = new List<string>();
    }

    [Serializable]
    public sealed class CoreProgressionDoc
    {
        public int schemaVersion = 1;
        public string finalObjective;
        public List<string> victorySequence = new List<string>();
        public List<CoreMilestoneEntry> milestones = new List<CoreMilestoneEntry>();
    }

    [Serializable]
    public sealed class CoreMilestoneEntry
    {
        public string id;
        public string title;
        public string description;
        public int requiredRouteCount = 2;
    }

    [Serializable]
    public sealed class NpcKnowledgeLibraryDoc
    {
        public int schemaVersion = 1;
        public List<NpcKnowledgeEntry> npcKnowledge = new List<NpcKnowledgeEntry>();
    }

    [Serializable]
    public sealed class NpcKnowledgeEntry
    {
        public string npcId;
        public string npcType;
        public List<string> uniqueKnowledge = new List<string>();
        public List<string> preferredGoals = new List<string>();
        public List<string> followerRecruitmentRequirements = new List<string>();
    }

    [Serializable]
    public sealed class ObjectArtifactCatalogDoc
    {
        public int schemaVersion = 1;
        public List<CatalogEntry> objects = new List<CatalogEntry>();
        public List<CatalogEntry> artifacts = new List<CatalogEntry>();
    }

    [Serializable]
    public sealed class CatalogEntry
    {
        public string id;
        public string label;
        public string category;
        public List<string> interactionTags = new List<string>();
        public int tradeValue = 1;
        public bool questCritical;
        public bool stackable = true;
        public List<string> usableActions = new List<string>();
        public string prefabHint;
    }

    [Serializable]
    public sealed class LocationCatalogDoc
    {
        public int schemaVersion = 1;
        public List<LocationCatalogEntry> locations = new List<LocationCatalogEntry>();
    }

    [Serializable]
    public sealed class LocationCatalogEntry
    {
        public string id;
        public string label;
        public string sceneAnchorName;
        public List<string> interactionTags = new List<string>();
    }

    [Serializable]
    public sealed class NpcArchetypeLibraryDoc
    {
        public int schemaVersion = 1;
        public List<NpcArchetypeEntry> archetypes = new List<NpcArchetypeEntry>();
    }

    [Serializable]
    public sealed class NpcArchetypeEntry
    {
        public string id;
        public string occupation;
        public string personality;
        public Dictionary<string, string> socialTraits = new Dictionary<string, string>();
    }

    [Serializable]
    public sealed class TradeRequirementsDoc
    {
        public int schemaVersion = 1;
        public List<TradeRequirementEntry> requirements = new List<TradeRequirementEntry>();
    }

    [Serializable]
    public sealed class TradeRequirementEntry
    {
        public string id;
        public string ownerNpcId;
        public string givesItemId;
        public string wantsItemId;
        public string unlocks;
        public string notes;
    }
}
