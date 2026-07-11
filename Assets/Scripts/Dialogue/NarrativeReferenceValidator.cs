using System;
using System.Collections.Generic;
using System.Linq;

namespace Rpg.Dialogue
{
    public sealed class NarrativeReferenceValidator
    {
        readonly HashSet<string> _npcIds;
        readonly HashSet<string> _itemIds;
        readonly HashSet<string> _locationIds;
        readonly HashSet<string> _milestoneIds;

        public NarrativeReferenceValidator(
            IEnumerable<string> npcIds,
            IEnumerable<string> itemIds,
            IEnumerable<string> locationIds,
            IEnumerable<string> milestoneIds)
        {
            _npcIds = new HashSet<string>((npcIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            _itemIds = new HashSet<string>((itemIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            _locationIds = new HashSet<string>((locationIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            _milestoneIds = new HashSet<string>((milestoneIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        }

        public bool IsValidItem(string id) => !string.IsNullOrWhiteSpace(id) && _itemIds.Contains(id.Trim());
        public bool IsValidNpc(string id) => !string.IsNullOrWhiteSpace(id) && _npcIds.Contains(id.Trim());
        public bool IsValidLocation(string id) => !string.IsNullOrWhiteSpace(id) && _locationIds.Contains(id.Trim());
        public bool IsValidMilestone(string id) => !string.IsNullOrWhiteSpace(id) && _milestoneIds.Contains(id.Trim());

        public List<string> ValidateCanon(NarrativeSessionCanon canon)
        {
            var issues = new List<string>();
            if (canon == null)
            {
                issues.Add("Canon is null.");
                return issues;
            }

            foreach (var npc in canon.npcProfiles ?? new List<GeneratedNpcProfile>())
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.npcId))
                    issues.Add("Generated NPC profile missing npcId.");
                else if (!_npcIds.Contains(npc.npcId.Trim()))
                    issues.Add($"Unknown npcId in canon: {npc.npcId}");
            }

            foreach (var m in canon.criticalMilestones ?? new List<string>())
            {
                if (!_milestoneIds.Contains(m))
                    issues.Add($"Unknown milestone id: {m}");
            }

            foreach (var req in canon.tradeRequirements ?? new List<TradeRequirementEntry>())
            {
                if (req == null)
                {
                    issues.Add("Trade requirement entry is null.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(req.ownerNpcId) && !_npcIds.Contains(req.ownerNpcId.Trim()))
                    issues.Add($"Unknown ownerNpcId in trade requirement '{req.id}': {req.ownerNpcId}");
                if (!string.IsNullOrWhiteSpace(req.givesItemId) && !_itemIds.Contains(req.givesItemId.Trim()))
                    issues.Add($"Unknown givesItemId in trade requirement '{req.id}': {req.givesItemId}");
                if (!string.IsNullOrWhiteSpace(req.wantsItemId) && !_itemIds.Contains(req.wantsItemId.Trim()))
                    issues.Add($"Unknown wantsItemId in trade requirement '{req.id}': {req.wantsItemId}");
            }

            return issues;
        }

        public List<string> ValidateAction(NpcProposedAction action)
        {
            var issues = new List<string>();
            if (action == null)
            {
                issues.Add("Action is null.");
                return issues;
            }

            var t = action.ActionType?.Trim().ToLowerInvariant() ?? string.Empty;
            var target = action.TargetId?.Trim();
            if (t == "give_object" || t == "receive_object" || t == "trade" || t == "find_object" || t == "activate_object")
            {
                if (!IsValidItem(target))
                    issues.Add($"Invalid itemId target: {target}");
            }
            else if (t == "move_to_location")
            {
                // Guides may target either a catalog location id (warehouse, etc.)
                // or another NPC id (e.g., "take me to enzo").
                if (!IsValidLocation(target) && !IsValidNpc(target))
                    issues.Add($"Invalid move_to_location target (locationId or npcId expected): {target}");
            }
            else if (t == "inspect_location")
            {
                if (!IsValidLocation(target))
                    issues.Add($"Invalid locationId target: {target}");
            }
            else if (t == "refer_to_npc")
            {
                if (!IsValidNpc(target))
                    issues.Add($"Invalid npcId target: {target}");
            }

            return issues;
        }
    }
}
