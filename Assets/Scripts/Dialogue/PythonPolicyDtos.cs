using System;
using System.Collections.Generic;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class PythonNpcContextDto
    {
        public string npcId;
        public string displayName;
        public string npcType;
        public string roleSummary;
        public string toneAndVocabulary;
        public string safetyRules;
        public string personality;
        public Dictionary<string, string> socialTraits = new Dictionary<string, string>();
        public List<string> goals = new List<string>();
        public List<string> capabilities = new List<string>();
    }

    [Serializable]
    public sealed class PythonTurnContextDto
    {
        public string worldFacts;
        public string memoryBlock;
        public string summaryBlock;
        public string inventoryBlock;
        public string surroundingsBlock;
        public string narrativeBlock;
        public List<OllamaMessageDto> recentTurns = new List<OllamaMessageDto>();
        public string latestPlayerLine;
    }

    [Serializable]
    public sealed class PythonDialogueTurnRequestDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string model;
        public PythonNpcContextDto npc;
        public PythonTurnContextDto turn;
        public string apiToken;
        public string providerBaseUrl;
    }

    [Serializable]
    public sealed class PythonDialogueTurnResponseDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string say;
        public bool ackYear;
        public string interactionOutcome;
        public List<NpcProposedAction> proposedActions = new List<NpcProposedAction>();
        public List<string> milestoneSignals = new List<string>();
        public Dictionary<string, string> stateDeltas = new Dictionary<string, string>();
        public List<Dictionary<string, string>> memoriesToAdd = new List<Dictionary<string, string>>();
        public string rawAssistant;
    }

    [Serializable]
    public sealed class PythonSummaryRequestDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string model;
        public string npcId;
        public List<OllamaMessageDto> turns = new List<OllamaMessageDto>();
        public string apiToken;
        public string providerBaseUrl;
    }

    [Serializable]
    public sealed class PythonSummaryResponseDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string summary;
        public List<string> learnedFacts = new List<string>();
        public List<string> openThreads = new List<string>();
        public string relationshipShift;
        public string rawAssistant;
    }

    [Serializable]
    public sealed class PythonNarrativeRequestDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string model;
        public int seed;
        public string fallbackCanonJson;
        public string apiToken;
        public string providerBaseUrl;
    }

    [Serializable]
    public sealed class PythonNarrativeResponseDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string canonJson;
        public string rawAssistant;
    }

    [Serializable]
    public sealed class PythonPolicyErrorDto
    {
        public string code;
        public string message;
    }

    [Serializable]
    public sealed class PythonPolicyEnvelopeDto
    {
        public bool ok;
        public PythonPolicyErrorDto error;
        public PythonDialogueTurnResponseDto dialogue;
        public PythonSummaryResponseDto summary;
        public PythonNarrativeResponseDto narrative;
    }
}
