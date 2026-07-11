using System;
using System.Collections.Generic;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Removes references to retired / non-game phantom entities from narrative text and canon data.
    /// </summary>
    public static class NarrativePhantomReferenceFilter
    {
        static readonly string[] BannedTerms =
        {
            "magic_diamond",
            "magic diamond",
            "portal_core",
            "portal core",
            "magic_statue",
            "magic statue",
            "hidden_castle",
            "hidden castle"
        };

        const string SafeGoalFallback = "Support village stability and help with practical local needs.";

        public static bool ContainsPhantomReference(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var normalized = text.Trim().ToLowerInvariant();
            for (var i = 0; i < BannedTerms.Length; i++)
            {
                if (normalized.Contains(BannedTerms[i]))
                    return true;
            }

            return false;
        }

        public static string SanitizeText(string text, string fallback = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback ?? string.Empty;
            return ContainsPhantomReference(text) ? (fallback ?? string.Empty) : text.Trim();
        }

        public static List<string> SanitizeTextList(IReadOnlyList<string> source, string fallback = null)
        {
            var result = new List<string>();
            if (source == null || source.Count == 0)
                return result;

            for (var i = 0; i < source.Count; i++)
            {
                var line = source[i];
                if (string.IsNullOrWhiteSpace(line) || ContainsPhantomReference(line))
                    continue;
                result.Add(line.Trim());
            }

            if (result.Count == 0 && !string.IsNullOrWhiteSpace(fallback))
                result.Add(fallback.Trim());
            return result;
        }

        public static List<string> SanitizeGoalList(IReadOnlyList<string> source) =>
            SanitizeTextList(source, SafeGoalFallback);

        public static void SanitizeCanon(
            NarrativeSessionCanon canon,
            NarrativeReferenceValidator refs,
            IReadOnlyList<string> npcIds,
            ObjectArtifactCatalogDoc catalog)
        {
            if (canon == null)
                return;

            canon.finalObjective = SanitizeText(
                canon.finalObjective,
                "Collect allies and required supplies, defeat the Ghoul in the castle, and reach the portal.");
            canon.summary = SanitizeText(canon.summary, canon.summary);
            canon.worldBackstory = SanitizeText(canon.worldBackstory, canon.worldBackstory);
            canon.openingIntroLines = SanitizeTextList(canon.openingIntroLines);
            canon.globalKnowledge = SanitizeTextList(canon.globalKnowledge);
            canon.victorySequence = SanitizeTextList(canon.victorySequence);
            canon.criticalMilestones = SanitizeTextList(canon.criticalMilestones);

            if (canon.tradeRequirements != null && canon.tradeRequirements.Count > 0)
            {
                var validItems = CollectKnownItemIds(catalog);
                canon.tradeRequirements = FilterTradeRequirements(canon.tradeRequirements, npcIds, validItems);
            }

            if (canon.npcProfiles == null)
                return;

            for (var i = 0; i < canon.npcProfiles.Count; i++)
            {
                var profile = canon.npcProfiles[i];
                if (profile == null)
                    continue;
                profile.keyInformation = SanitizeTextList(profile.keyInformation);
                profile.goals = SanitizeGoalList(profile.goals);
                profile.followerRecruitmentRequirements = SanitizeTextList(profile.followerRecruitmentRequirements);
            }

            if (refs == null)
                return;

            var issues = refs.ValidateCanon(canon);
            if (issues.Count > 0)
                DialogueTelemetry.Log("NarrativeCanonSanitized", string.Join(" | ", issues));
        }

        static List<TradeRequirementEntry> FilterTradeRequirements(
            IReadOnlyList<TradeRequirementEntry> source,
            IReadOnlyList<string> npcIds,
            HashSet<string> validItemIds)
        {
            var filtered = new List<TradeRequirementEntry>();
            if (source == null || source.Count == 0)
                return filtered;

            var validNpcIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (npcIds != null)
            {
                for (var i = 0; i < npcIds.Count; i++)
                {
                    var id = npcIds[i];
                    if (!string.IsNullOrWhiteSpace(id))
                        validNpcIds.Add(id.Trim());
                }
            }

            for (var i = 0; i < source.Count; i++)
            {
                var req = source[i];
                if (req == null)
                    continue;
                if (string.IsNullOrWhiteSpace(req.ownerNpcId)
                    || string.IsNullOrWhiteSpace(req.givesItemId)
                    || string.IsNullOrWhiteSpace(req.wantsItemId))
                    continue;

                var ownerNpcId = req.ownerNpcId.Trim();
                var givesItemId = req.givesItemId.Trim();
                var wantsItemId = req.wantsItemId.Trim();
                if (ContainsPhantomReference(givesItemId) || ContainsPhantomReference(wantsItemId))
                    continue;
                if (validNpcIds.Count > 0 && !validNpcIds.Contains(ownerNpcId))
                    continue;
                if (validItemIds.Count > 0 && (!validItemIds.Contains(givesItemId) || !validItemIds.Contains(wantsItemId)))
                    continue;

                filtered.Add(new TradeRequirementEntry
                {
                    id = req.id,
                    ownerNpcId = ownerNpcId,
                    givesItemId = givesItemId,
                    wantsItemId = wantsItemId,
                    unlocks = req.unlocks,
                    notes = req.notes
                });
            }

            return filtered;
        }

        static HashSet<string> CollectKnownItemIds(ObjectArtifactCatalogDoc catalog)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (catalog == null)
                return known;
            AddCatalogIds(catalog.objects, known);
            AddCatalogIds(catalog.artifacts, known);
            return known;
        }

        static void AddCatalogIds(IReadOnlyList<CatalogEntry> entries, HashSet<string> known)
        {
            if (entries == null || known == null)
                return;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                    continue;
                known.Add(entry.id.Trim());
            }
        }
    }
}
