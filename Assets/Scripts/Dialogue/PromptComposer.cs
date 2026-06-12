using System.Collections.Generic;
using System.IO;
using System.Text;
using System;
using Rpg.GameState;
using Rpg.Npc;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Rpg.Dialogue
{
    public sealed class PromptComposer
    {
        readonly string _streamingAssetsRoot;
        readonly NpcMemoryRepository _memory;

        public PromptComposer(string streamingAssetsRoot = null, NpcMemoryRepository memoryRepository = null)
        {
            _streamingAssetsRoot = string.IsNullOrEmpty(streamingAssetsRoot)
                ? Application.streamingAssetsPath
                : streamingAssetsRoot;
            _memory = memoryRepository;
        }

        public List<OllamaMessageDto> BuildMessages(
            NpcDefinition npc,
            WorldStateSnapshot world,
            DialogueSession session,
            string latestPlayerLine,
            string sessionBreakInset = null,
            NarrativeSessionCanon narrativeCanon = null,
            NpcConversationSummary npcSummary = null,
            string inventoryBlock = null)
        {
            var system = BuildSystemPrompt(npc, world, sessionBreakInset, narrativeCanon, npcSummary, inventoryBlock);
            var messages = new List<OllamaMessageDto>
            {
                new OllamaMessageDto("system", system)
            };

            foreach (var m in session.GetRecentTurnMessages())
                messages.Add(new OllamaMessageDto(m.role, m.content));

            messages.Add(new OllamaMessageDto("user", latestPlayerLine));
            return messages;
        }

        string BuildSystemPrompt(
            NpcDefinition npc,
            WorldStateSnapshot world,
            string sessionBreakInset = null,
            NarrativeSessionCanon narrativeCanon = null,
            NpcConversationSummary npcSummary = null,
            string inventoryBlock = null)
        {
            var template = LoadTemplate("npc_system_template.txt");
            var facts = world.ToFactsBlock();
            var memoryBlock = _memory != null
                ? _memory.BuildPromptBlock(npc.npcId)
                : "(Long-term memory is not wired for this build.)";
            var globalNarrativeBlock = BuildGlobalNarrativeBlock(narrativeCanon);
            var npcNarrativeBlock = BuildNpcNarrativeBlock(narrativeCanon, npc.npcId);
            var summaryBlock = BuildSummaryBlock(npcSummary);
            var invBlock = string.IsNullOrWhiteSpace(inventoryBlock) ? "(No inventory data provided.)" : inventoryBlock.Trim();
            var sessionBreakBlock = string.IsNullOrWhiteSpace(sessionBreakInset)
                ? string.Empty
                : sessionBreakInset.Trim() + "\n\n";
            var persona = new StringBuilder();
            var speakingNpcIsSidekick = SidekickCompanion.BindingRootHasSidekick(npc.npcId);
            var ghoulStory = GhoulMenaceController.IsGhoulStoryNpcId(npc.npcId);
            persona.AppendLine($"NPC_NAME: {npc.displayName}");
            persona.AppendLine($"NPC_ID: {npc.npcId}");
            persona.AppendLine($"IS_SIDEKICK: {(speakingNpcIsSidekick ? "true" : "false")}");
            persona.AppendLine($"IS_GHOUL_STORY_NPC: {(ghoulStory ? "true" : "false")}");
            persona.AppendLine("ROLE:");
            persona.AppendLine(npc.roleSummary);
            persona.AppendLine("TONE:");
            persona.AppendLine(npc.toneAndVocabulary);
            persona.AppendLine("RULES:");
            persona.AppendLine(npc.safetyRules);

            var surroundings = NpcSurroundingsScanner.BuildPromptBlock(npc.npcId);
            return template
                .Replace("{FACTS_BLOCK}", facts)
                .Replace("{MEMORY_BLOCK}", memoryBlock)
                .Replace("{SESSION_BREAK_BLOCK}", sessionBreakBlock)
                .Replace("{KNOWN_MOVE_TARGETS}", BuildKnownMoveTargetsBlock(npc.npcId))
                .Replace("{NPC_PERSONA_BLOCK}", persona.ToString()) +
                "\n\nGLOBAL_NARRATIVE_CONTEXT:\n" + globalNarrativeBlock +
                "\n\nNPC_GENERATED_PROFILE:\n" + npcNarrativeBlock +
                "\n\nNPC_CONVERSATION_SUMMARY:\n" + summaryBlock +
                "\n\nINVENTORY_CONTEXT:\n" + invBlock +
                "\n\nIMMEDIATE_SURROUNDINGS:\n" + surroundings +
                "\n\nADDITIONAL_RUNTIME_RULES:\n" +
                "- Treat NPC_GENERATED_PROFILE as canonical for this NPC's behavior, social traits, and goals.\n" +
                "- Treat INVENTORY_CONTEXT as canonical for what hero and NPC currently possess.\n" +
                "- IMMEDIATE_SURROUNDINGS is a fresh scan within ~50m of your character in the world; use it when the player asks what is around, who is nearby, or what structures they can see. Do not invent specific nearby objects that are not listed.\n" +
                "- If asked what you carry/own/trade, answer from NPC_INVENTORY only (do not invent missing items).\n" +
                "- If proposing give/receive/trade/find actions, reference only item IDs from INVENTORY_CONTEXT.\n" +
                "- NPCs may offer to guide the hero to catalog locations (like 'warehouse') or to another NPC's location.\n" +
                "- Only trigger proposedNpcActions move_to_location after the hero explicitly agrees.\n" +
                "- Sidekick NPCs can agree to follow the hero; only trigger proposedNpcActions follow_hero after the hero explicitly agrees.\n" +
                "- Non-sidekick NPCs should not emit follow_hero.\n" +
                "- For guiding to another NPC, use targetId as that NPC's npcId from GUIDE_TARGET_IDS (not a paraphrase).\n" +
                "- If intent is to visit/meet another NPC, actionType MUST be refer_to_npc with targetId=npcId from GUIDE_TARGET_IDS.\n" +
                "- Never emit location aliases (e.g. wagon_site, hill_site, market_site) when the destination is an NPC.\n" +
                "- If IS_SIDEKICK=true, this OVERRIDES normal guiding: never emit move_to_location/move_to_npc/refer_to_npc. Only decide follow_hero (after explicit player agreement) or refuse.\n" +
                "- If IS_GHOUL_STORY_NPC=true, this is pure atmospheric villain banter: never emit proposedNpcActions, trades, guides, milestones, or state deltas; keep interactionOutcome as menace_flavor; write in ALL CAPS.\n" +
                "- Never accuse the hero of stealing YOUR chicken unless INVENTORY_CONTEXT explicitly supports that confrontation for this NPC_ID; if IS_GHOUL_STORY_NPC=true, never mention chicken theft.\n" +
                "- When speaking, prefer one clear next-step tied to a visible milestone or NPC goal.\n" +
                "- At some point in natural conversation, all NPCs should mention that magic is contained in books and that some locations feel more magical than others.\n" +
                "- If NPC_TYPE is sidekick, also mention that sidekicks know magic and groups of 3 or more magicians together can cast the most powerful spell.\n";
        }

        static bool IsSidekickNpc(string npcId) => SidekickCompanion.BindingRootHasSidekick(npcId);

        static string BuildKnownMoveTargetsBlock(string speakingNpcId)
        {
            var sb = new StringBuilder();
            try
            {
                var cat = new NarrativeContentLibrary().LoadLocationCatalog();
                if (cat?.locations != null && cat.locations.Count > 0)
                {
                    sb.AppendLine("Locations (targetId = location id):");
                    foreach (var loc in cat.locations)
                    {
                        if (loc == null || string.IsNullOrWhiteSpace(loc.id))
                            continue;
                        var label = string.IsNullOrWhiteSpace(loc.label) ? loc.id : loc.label.Trim();
                        sb.AppendLine($"- {loc.id.Trim()}  ({label})");
                    }
                }
                else
                    sb.AppendLine("Locations: (catalog unavailable in prompt build)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Locations: (catalog load error: {ex.Message})");
            }

            sb.AppendLine("NPCs (targetId = npcId on the line; guiding uses that NPC's current world position):");
            var anyOther = false;
            foreach (var b in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null)
                    continue;
                var id = b.Definition.npcId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (!string.IsNullOrWhiteSpace(speakingNpcId)
                    && string.Equals(id, speakingNpcId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                anyOther = true;
                var name = string.IsNullOrWhiteSpace(b.Definition.displayName) ? id : b.Definition.displayName.Trim();
                var sidekickTag = SidekickCompanion.FindForNpcBindingRoot(b.gameObject) != null ? "  sidekick=true" : string.Empty;
                sb.AppendLine($"- npcId={id}  displayName={name}{sidekickTag}");
            }
            if (!anyOther)
                sb.AppendLine("(no other NPCs with NpcDialogueBinding found in the loaded scene)");
            return sb.ToString().TrimEnd();
        }

        static string BuildGlobalNarrativeBlock(NarrativeSessionCanon canon)
        {
            if (canon == null)
                return "(No generated session narrative available yet.)";
            var sb = new StringBuilder();
            sb.AppendLine($"SESSION_ID: {canon.sessionId}");
            sb.AppendLine($"PREMISE_ID: {canon.premiseId}");
            sb.AppendLine($"WORLD_ID: {canon.worldId}");
            sb.AppendLine($"SUMMARY: {canon.summary}");
            if (!string.IsNullOrWhiteSpace(canon.worldBackstory))
                sb.AppendLine($"WORLD_BACKSTORY: {canon.worldBackstory}");
            if (canon.openingIntroLines != null && canon.openingIntroLines.Count > 0)
            {
                sb.AppendLine("OPENING_INTRO:");
                foreach (var line in canon.openingIntroLines)
                    sb.AppendLine("- " + line);
            }
            if (canon.globalKnowledge != null && canon.globalKnowledge.Count > 0)
            {
                sb.AppendLine("GLOBAL_KNOWLEDGE:");
                foreach (var fact in canon.globalKnowledge)
                    sb.AppendLine("- " + fact);
            }
            sb.AppendLine($"FINAL_OBJECTIVE: {canon.finalObjective}");
            if (canon.criticalMilestones != null && canon.criticalMilestones.Count > 0)
            {
                sb.AppendLine("CRITICAL_MILESTONES:");
                foreach (var m in canon.criticalMilestones)
                    sb.AppendLine("- " + m);
            }
            if (canon.victorySequence != null && canon.victorySequence.Count > 0)
            {
                sb.AppendLine("VICTORY_SEQUENCE:");
                foreach (var step in canon.victorySequence)
                    sb.AppendLine("- " + step);
            }
            if (canon.tradeRequirements != null && canon.tradeRequirements.Count > 0)
            {
                sb.AppendLine("KEY_TRADES:");
                foreach (var t in canon.tradeRequirements)
                {
                    if (t == null)
                        continue;
                    var id = string.IsNullOrWhiteSpace(t.id) ? "trade_rule" : t.id.Trim();
                    sb.AppendLine($"- {id}: ownerNpcId={t.ownerNpcId}, gives={t.givesItemId}, wants={t.wantsItemId}, unlocks={t.unlocks}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        static string BuildNpcNarrativeBlock(NarrativeSessionCanon canon, string npcId)
        {
            if (canon?.npcProfiles == null || canon.npcProfiles.Count == 0)
                return "(No generated NPC profile for this NPC yet.)";
            var p = canon.npcProfiles.Find(x => x != null && string.Equals(x.npcId, npcId, StringComparison.OrdinalIgnoreCase));
            if (p == null)
                return "(No generated NPC profile match by npcId.)";
            var sb = new StringBuilder();
            sb.AppendLine($"NAME: {p.name}");
            sb.AppendLine($"NPC_TYPE: {p.npcType}");
            sb.AppendLine($"RACE: {p.race}");
            sb.AppendLine($"SEX: {p.sex}");
            sb.AppendLine($"AGE: {p.age}");
            sb.AppendLine($"OCCUPATION: {p.occupation}");
            sb.AppendLine($"PERSONALITY: {p.personality}");
            if (p.socialTraits != null && p.socialTraits.Count > 0)
            {
                sb.AppendLine("SOCIAL_TRAITS:");
                foreach (var kv in p.socialTraits)
                    sb.AppendLine($"- {kv.Key}: {kv.Value}");
            }

            if (p.keyInformation != null && p.keyInformation.Count > 0)
            {
                sb.AppendLine("NPC_KEY_INFORMATION:");
                foreach (var v in p.keyInformation)
                    sb.AppendLine("- " + v);
            }

            if (p.goals != null && p.goals.Count > 0)
            {
                sb.AppendLine("NPC_GOALS:");
                foreach (var v in p.goals)
                    sb.AppendLine("- " + v);
            }

            if (p.followerRecruitmentRequirements != null && p.followerRecruitmentRequirements.Count > 0)
            {
                sb.AppendLine("FOLLOWER_RECRUITMENT_REQUIREMENTS:");
                foreach (var v in p.followerRecruitmentRequirements)
                    sb.AppendLine("- " + v);
            }

            return sb.ToString().TrimEnd();
        }

        static string BuildSummaryBlock(NpcConversationSummary summary)
        {
            if (summary == null || string.IsNullOrWhiteSpace(summary.summary))
                return "(No prior conversation summary.)";
            var sb = new StringBuilder();
            sb.AppendLine(summary.summary.Trim());
            if (summary.learnedFacts != null && summary.learnedFacts.Count > 0)
            {
                sb.AppendLine("LEARNED_FACTS:");
                foreach (var f in summary.learnedFacts)
                    sb.AppendLine("- " + f);
            }

            if (summary.openThreads != null && summary.openThreads.Count > 0)
            {
                sb.AppendLine("OPEN_THREADS:");
                foreach (var f in summary.openThreads)
                    sb.AppendLine("- " + f);
            }

            return sb.ToString().TrimEnd();
        }

        string LoadTemplate(string fileName)
        {
            var path = Path.Combine(_streamingAssetsRoot, "Dialogue", fileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[PromptComposer] Missing template at {path}. Using emergency inline template.");
                return EmergencyTemplate();
            }

            return File.ReadAllText(path);
        }

        static string EmergencyTemplate()
        {
            return
                "You are an in-world character.\n" +
                "{NPC_PERSONA_BLOCK}\n" +
                "{FACTS_BLOCK}\n" +
                "{MEMORY_BLOCK}\n" +
                "{SESSION_BREAK_BLOCK}\n" +
                "Output ONLY JSON: {\"say\":\"...\",\"ackYear\":false,\"memoriesToAdd\":[]}\n";
        }
    }
}
