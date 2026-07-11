using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Dialogue
{
    public sealed class NarrativeGenerationService
    {
        readonly NarrativeContentLibrary _library;
        readonly OllamaClient _ollamaClient;
        readonly OllamaSettings _settings;
        readonly NarrativeSessionStore _store;
        readonly NarrativeReferenceValidator _refs;

        public NarrativeGenerationService(
            NarrativeContentLibrary library,
            OllamaClient ollamaClient,
            OllamaSettings settings,
            NarrativeSessionStore store,
            NarrativeReferenceValidator refs = null)
        {
            _library = library;
            _ollamaClient = ollamaClient;
            _settings = settings;
            _store = store;
            _refs = refs;
        }

        public NarrativeSessionCanon BuildFallback(int seed, IReadOnlyList<string> npcIds)
        {
            var global = _library.LoadGlobalKnowledge();
            var premiseLib = _library.LoadIntroPremises();
            var progression = _library.LoadCoreProgression();
            var npcKnowledge = _library.LoadNpcKnowledge();
            var tradeRequirements = _library.LoadTradeRequirements();
            var catalog = _library.LoadObjectArtifactCatalog();
            var archetypes = _library.LoadNpcArchetypes().archetypes;
            var rng = new System.Random(seed);

            var premise = premiseLib.premises.Count > 0
                ? premiseLib.premises[rng.Next(premiseLib.premises.Count)]
                : new IntroPremiseEntry
                {
                    id = "default_intro",
                    title = "Castaway Arrival",
                    introLines = new List<string>
                    {
                        "You awaken on a strange island with no memory of how you arrived.",
                        "People here whisper that a giant Ghoul has taken the castle and guards an unstable portal.",
                        "To survive, you must negotiate with island residents, gather allies, and reach the castle.",
                        "Only by defeating the Ghoul can you open the portal and leave this place."
                    }
                };

            var coreMilestones = progression.milestones ?? new List<CoreMilestoneEntry>();
            var introLines = (premise.introLines ?? new List<string>()).Take(4).ToList();
            if (introLines.Count == 0)
                introLines.Add("You must gather allies, defeat the Ghoul, and escape through the castle portal.");
            var canon = new NarrativeSessionCanon
            {
                sessionId = Guid.NewGuid().ToString("N"),
                seed = seed,
                premiseId = string.IsNullOrWhiteSpace(premise.id) ? "default_intro" : premise.id,
                worldId = string.IsNullOrWhiteSpace(global.worldId) ? "island_world" : global.worldId,
                summary = string.Join(" ", introLines),
                openingIntroLines = introLines,
                worldBackstory = string.IsNullOrWhiteSpace(global.openingBackstory)
                    ? "The island community was once peaceful, but monsters and toxic tides now threaten everyone."
                    : global.openingBackstory,
                globalKnowledge = (global.sharedFacts ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(12)
                    .ToList(),
                finalObjective = string.IsNullOrWhiteSpace(progression.finalObjective)
                    ? "Collect allies and required supplies, defeat the Ghoul in the castle, and reach the portal."
                    : progression.finalObjective,
                victorySequence = (progression.victorySequence ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList(),
                tradeRequirements = FilterTradeRequirements(
                    tradeRequirements.requirements ?? new List<TradeRequirementEntry>(),
                    npcIds,
                    CollectKnownItemIds(catalog))
            };

            foreach (var m in coreMilestones)
            {
                if (m == null || string.IsNullOrWhiteSpace(m.title))
                    continue;
                canon.criticalMilestones.Add(m.title.Trim());
                canon.routesByMilestone[m.title.Trim()] = Mathf.Max(2, m.requiredRouteCount);
            }

            if (npcIds != null)
            {
                var knowledgeByNpc = new Dictionary<string, NpcKnowledgeEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in npcKnowledge.npcKnowledge ?? new List<NpcKnowledgeEntry>())
                {
                    if (k == null || string.IsNullOrWhiteSpace(k.npcId))
                        continue;
                    knowledgeByNpc[k.npcId.Trim()] = k;
                }

                foreach (var npcId in npcIds)
                {
                    var a = archetypes.Count > 0 ? archetypes[rng.Next(archetypes.Count)] : new NpcArchetypeEntry { occupation = "Resident", personality = "Neutral" };
                    var keyInfo = new List<string>();
                    var goals = new List<string>();
                    var followerReq = new List<string>();
                    var runtimeType = IsSidekickNpcRuntime(npcId) ? "sidekick" : "normal";
                    if (!string.IsNullOrWhiteSpace(npcId) && knowledgeByNpc.TryGetValue(npcId.Trim(), out var k))
                    {
                        if (!string.IsNullOrWhiteSpace(k.npcType))
                            runtimeType = k.npcType.Trim().ToLowerInvariant();
                        if (k.uniqueKnowledge != null)
                            keyInfo.AddRange(k.uniqueKnowledge.Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));
                        if (k.preferredGoals != null)
                            goals.AddRange(k.preferredGoals.Where(x => !string.IsNullOrWhiteSpace(x)).Take(2));
                        if (k.followerRecruitmentRequirements != null)
                            followerReq.AddRange(k.followerRecruitmentRequirements.Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));
                    }

                    if (keyInfo.Count == 0)
                        keyInfo.Add("Knows one rumor that can help the hero advance toward the castle.");
                    if (goals.Count == 0)
                        goals.Add("Trade information or items in exchange for help with personal concerns.");
                    if (runtimeType == "sidekick")
                    {
                        if (followerReq.Count == 0)
                            followerReq.Add("May join only after persuasion or receiving a wanted item.");
                        goals.Insert(0, "Evaluate whether the hero is trustworthy enough to follow.");
                    }
                    else if (followerReq.Count > 0)
                        followerReq.Clear();

                    var capabilities = runtimeType == "sidekick"
                        ? new List<string> { "dialogue", "follow_hero", "give", "trade" }
                        : new List<string> { "dialogue", "give", "trade", "guide_to_location", "refer_to_npc" };

                    canon.npcProfiles.Add(new GeneratedNpcProfile
                    {
                        npcId = npcId,
                        name = npcId,
                        npcType = runtimeType,
                        race = "Unknown",
                        sex = "Unknown",
                        age = "Adult",
                        occupation = a.occupation,
                        personality = a.personality,
                        socialTraits = new Dictionary<string, string>(a.socialTraits),
                        keyInformation = keyInfo,
                        goals = goals,
                        capabilities = capabilities,
                        followerRecruitmentRequirements = followerReq
                    });
                }
            }

            ValidateAndRepair(canon);
            NarrativePhantomReferenceFilter.SanitizeCanon(canon, _refs, npcIds, catalog);
            if (_refs != null)
            {
                var issues = _refs.ValidateCanon(canon);
                if (issues.Count > 0)
                    DialogueTelemetry.Log("NarrativeFallbackRefIssues", string.Join(" | ", issues));
            }
            DialogueTelemetry.Log("NarrativeFallbackGenerated", $"seed={seed}, npcs={canon.npcProfiles.Count}");
            return canon;
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
                if (string.IsNullOrWhiteSpace(req.ownerNpcId) || string.IsNullOrWhiteSpace(req.givesItemId) || string.IsNullOrWhiteSpace(req.wantsItemId))
                    continue;
                var ownerNpcId = req.ownerNpcId.Trim();
                var givesItemId = req.givesItemId.Trim();
                var wantsItemId = req.wantsItemId.Trim();
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

        public async Task<NarrativeSessionCanon> GenerateOrFallbackAsync(int seed, IReadOnlyList<string> npcIds, CancellationToken token)
        {
            var fallback = BuildFallback(seed, npcIds);
            if (_ollamaClient == null || _settings == null)
            {
                _store?.Save(fallback);
                return fallback;
            }

            var prompt = BuildGenerationPrompt(fallback);
            var req = new List<OllamaMessageDto>
            {
                new OllamaMessageDto("system",
                    "You are a game narrative generator. Return ONLY JSON matching a session narrative with npcProfiles and routesByMilestone."),
                new OllamaMessageDto("user", prompt)
            };

            const int maxAttempts = 2;
            for (var i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var http = await _ollamaClient.ChatAsync(req, _settings.model, token);
                    if (!http.IsSuccess)
                    {
                        DialogueTelemetry.Log("NarrativeGenerationHttpFail", http.Error);
                        continue;
                    }

                    var normalized = ResponseValidator.NormalizeJsonPayload(http.AssistantContent);
                    var parsed = JsonConvert.DeserializeObject<NarrativeSessionCanon>(normalized);
                    if (parsed == null)
                        continue;
                    if (!ValidateAndRepair(parsed))
                        continue;
                    NarrativePhantomReferenceFilter.SanitizeCanon(
                        parsed,
                        _refs,
                        npcIds,
                        _library.LoadObjectArtifactCatalog());
                    if (_refs != null)
                    {
                        var issues = _refs.ValidateCanon(parsed);
                        if (issues.Count > 0)
                        {
                            DialogueTelemetry.Log("NarrativeCanonRefInvalid", string.Join(" | ", issues));
                            continue;
                        }
                    }
                    if (parsed.npcProfiles == null || parsed.npcProfiles.Count == 0)
                        parsed.npcProfiles = fallback.npcProfiles;
                    if (string.IsNullOrWhiteSpace(parsed.sessionId))
                        parsed.sessionId = Guid.NewGuid().ToString("N");
                    parsed.seed = seed;
                    _store?.Save(parsed);
                    DialogueTelemetry.Log("NarrativeGenerationSuccess", $"attempt={i + 1}");
                    return parsed;
                }
                catch (Exception ex)
                {
                    DialogueTelemetry.Log("NarrativeGenerationException", ex.Message);
                }
            }

            DialogueTelemetry.Log("NarrativeGenerationFallbackUsed");
            _store?.Save(fallback);
            return fallback;
        }

        static string BuildGenerationPrompt(NarrativeSessionCanon fallback)
        {
            return
                "Generate a coherent narrative session from this seed scaffold. Keep all milestone route counts >= 2, preserve the core win condition (followers -> castle -> Ghoul -> portal), and keep JSON shape intact.\n" +
                "Use only NPC ids, item ids, and location ids already present in the scaffold. Do not invent phantom quest items (no magic diamonds, portal cores, magic statues, or hidden castles).\n" +
                JsonConvert.SerializeObject(fallback, Formatting.Indented);
        }

        public static bool ValidateAndRepair(NarrativeSessionCanon canon)
        {
            if (canon == null)
                return false;
            canon.schemaVersion = 1;
            canon.openingIntroLines ??= new List<string>();
            canon.globalKnowledge ??= new List<string>();
            canon.criticalMilestones ??= new List<string>();
            canon.routesByMilestone ??= new Dictionary<string, int>();
            canon.tradeRequirements ??= new List<TradeRequirementEntry>();
            canon.victorySequence ??= new List<string>();
            canon.npcProfiles ??= new List<GeneratedNpcProfile>();
            if (string.IsNullOrWhiteSpace(canon.premiseId))
                canon.premiseId = "default_intro";
            if (string.IsNullOrWhiteSpace(canon.worldId))
                canon.worldId = "island_world";
            if (string.IsNullOrWhiteSpace(canon.finalObjective))
                canon.finalObjective = "Collect allies and required supplies, defeat the Ghoul in the castle, and reach the portal.";
            if (canon.openingIntroLines.Count == 0)
                canon.openingIntroLines.Add("You awaken on a hostile island and must seek help to survive.");
            if (canon.globalKnowledge.Count == 0)
                canon.globalKnowledge.Add("The island has monsters, toxic water, and a haunted castle with a guarded portal.");
            if (canon.victorySequence.Count == 0)
                canon.victorySequence.AddRange(new[]
                {
                    "Collect followers through dialogue.",
                    "Trade and gather required quest items.",
                    "Reach the castle with followers.",
                    "Defeat the giant Ghoul.",
                    "Enter the portal."
                });
            if (canon.criticalMilestones.Count == 0)
                canon.criticalMilestones.Add("Recruit allies and gather clues needed to approach the castle.");

            foreach (var m in canon.criticalMilestones)
            {
                if (!canon.routesByMilestone.TryGetValue(m, out var routes) || routes < 2)
                    canon.routesByMilestone[m] = 2;
            }

            foreach (var npc in canon.npcProfiles)
            {
                npc.socialTraits ??= new Dictionary<string, string>();
                EnsureSocialLevel(npc.socialTraits, "helpfulness");
                EnsureSocialLevel(npc.socialTraits, "skepticism");
                EnsureSocialLevel(npc.socialTraits, "gullibility");
                EnsureSocialLevel(npc.socialTraits, "naivete");
                EnsureSocialLevel(npc.socialTraits, "trickery");
                EnsureSocialLevel(npc.socialTraits, "patience");
                npc.keyInformation ??= new List<string>();
                npc.goals ??= new List<string>();
                npc.capabilities ??= new List<string>();
                npc.followerRecruitmentRequirements ??= new List<string>();
                if (string.IsNullOrWhiteSpace(npc.npcType))
                    npc.npcType = "normal";
                else
                    npc.npcType = npc.npcType.Trim().ToLowerInvariant();
                if (npc.capabilities.Count == 0)
                    npc.capabilities.Add("dialogue");
            }

            return true;
        }

        static void EnsureSocialLevel(Dictionary<string, string> traits, string key)
        {
            if (!traits.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
            {
                traits[key] = "medium";
                return;
            }

            var t = v.Trim().ToLowerInvariant();
            if (t != "low" && t != "medium" && t != "high")
                traits[key] = "medium";
            else
                traits[key] = t;
        }

        static bool IsSidekickNpcRuntime(string npcId) => SidekickCompanion.BindingRootHasSidekick(npcId);
    }
}
