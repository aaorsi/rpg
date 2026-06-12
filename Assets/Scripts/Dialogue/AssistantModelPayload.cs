using System.Collections.Generic;

namespace Rpg.Dialogue
{
    /// <summary>Parsed assistant JSON: spoken line, year flag, and optional long-term memory candidates.</summary>
    public sealed class AssistantModelPayload
    {
        public string Say;
        public bool AckYear;
        public readonly List<NpcMemoryCandidate> MemoryAdds = new List<NpcMemoryCandidate>();
        public string InteractionOutcome;
        public readonly List<NpcProposedAction> ProposedActions = new List<NpcProposedAction>();
        public readonly List<NpcSocialOutcome> SocialOutcomes = new List<NpcSocialOutcome>();
        public readonly Dictionary<string, string> StateDeltas = new Dictionary<string, string>();
        public readonly List<string> MilestoneSignals = new List<string>();
    }

    /// <summary>One item the model chose to remember (written to disk after a successful parse).</summary>
    public readonly struct NpcMemoryCandidate
    {
        public readonly string Kind;
        public readonly string Summary;
        public readonly string SubjectCharacterId;

        public NpcMemoryCandidate(string kind, string summary, string subjectCharacterId)
        {
            Kind = kind;
            Summary = summary;
            SubjectCharacterId = subjectCharacterId;
        }
    }

    public sealed class NpcProposedAction
    {
        public string ActionType;
        public string TargetId;
        public float Quantity;
        public string Notes;
    }

    public sealed class NpcSocialOutcome
    {
        public string OutcomeType;
        public string TaskId;
        public string TargetNpcId;
        public float Amount;
        public string Currency;
        public string Persuasion;
        public string AdviceTopic;
        public string Notes;
    }
}
