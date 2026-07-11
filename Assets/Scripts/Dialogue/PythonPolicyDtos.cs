using System;
using System.Collections.Generic;
using Rpg.Npc;

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
        public string activePlanContext;
        public string activeGoalsContext;
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
        public List<PythonSocialOutcomeDto> socialOutcomes = new List<PythonSocialOutcomeDto>();
        public List<string> milestoneSignals = new List<string>();
        public Dictionary<string, string> stateDeltas = new Dictionary<string, string>();
        public List<Dictionary<string, string>> memoriesToAdd = new List<Dictionary<string, string>>();
        public string rawAssistant;
    }

    [Serializable]
    public sealed class PythonSocialOutcomeDto
    {
        public string outcomeType;
        public string taskId;
        public string targetNpcId;
        public float amount;
        public string currency;
        public string persuasion;
        public string adviceTopic;
        public string notes;
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
    public sealed class PythonVector3Dto
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public sealed class PythonNpcPlanStepDto
    {
        public string primitiveType;
        public string stepType;
        public PythonVector3Dto worldLocation;
        public float worldX;
        public float worldY;
        public float worldZ;
        public string targetNpcId;
        public float durationSeconds;
        public float stopDistanceMeters;
        public string workId;
    }

    [Serializable]
    public sealed class PythonNpcDeliberationNpcDto
    {
        public string npcId;
        public string displayName;
        public string npcType;
        public string personality;
        public Dictionary<string, string> socialTraits = new Dictionary<string, string>();
        public List<string> capabilities = new List<string>();
    }

    [Serializable]
    public sealed class PythonNpcDeliberationWorldDto
    {
        public string worldFacts;
        public int currentYear;
        public int currentDay;
        public float currentHour24;
        public string surroundingsBlock;
    }

    [Serializable]
    public sealed class PythonDeliberationTargetsDto
    {
        public List<string> locationIds = new List<string>();
        public List<string> npcIds = new List<string>();
        public List<string> workIds = new List<string>();
        public List<string> goalIds = new List<string>();
    }

    [Serializable]
    public sealed class PythonNpcDeliberationRequestDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string model;
        public string npcId;
        public string goal;
        public int maxSteps = 4;
        public PythonDeliberationTargetsDto targets = new PythonDeliberationTargetsDto();
        public string reason;
        public PythonNpcDeliberationNpcDto npc;
        public PythonNpcDeliberationWorldDto world;
        public List<string> currentGoals = new List<string>();
        public List<PythonNpcPlanStepDto> currentPlan = new List<PythonNpcPlanStepDto>();
        public List<string> agreements = new List<string>();
        public string apiToken;
        public string providerBaseUrl;
    }

    [Serializable]
    public sealed class PythonNpcDeliberationResponseDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string npcId;
        public List<string> goals = new List<string>();
        public List<PythonNpcPlanStepDto> planSteps = new List<PythonNpcPlanStepDto>();
        public List<InteractionDefinition> proposedInteractions = new List<InteractionDefinition>();
        public string rawAssistant;
    }

    [Serializable]
    public sealed class PythonTtsSynthesizeRequestDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string text;
        public string voiceId;
        public string language = "english";
        public bool quantize = true;
        public string speakerRole = "npc";
    }

    [Serializable]
    public sealed class PythonTtsSynthesizeResponseDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public int sampleRate;
        public string audioFormat;
        public string audioBase64;
        public int synthesisMs;
        public float rtf;
        public int timeToFirstChunkMs;
        public string speakerRole;
    }

    [Serializable]
    public sealed class PythonInteractionLineRequestDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string model;
        public string npcId;
        public string displayName;
        public string prompt;
        public string apiToken;
        public string providerBaseUrl;
    }

    [Serializable]
    public sealed class PythonInteractionLineResponseDto
    {
        public int schemaVersion = 1;
        public string requestId;
        public string say;
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
        public PythonNpcDeliberationResponseDto deliberation;
        public PythonTtsSynthesizeResponseDto tts;
        public PythonInteractionLineResponseDto interaction;
    }
}
