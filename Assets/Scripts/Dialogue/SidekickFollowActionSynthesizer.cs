using Rpg.Npc;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Substring heuristics that synthesize <see cref="NpcActionTypes.FollowHero"/> when a sidekick
    /// agrees to follow but the model omitted the action from its payload.
    /// </summary>
    public static class SidekickFollowActionSynthesizer
    {
        public static void TryAppendSyntheticFollowAction(string npcId, AssistantModelPayload payload, string lastPlayerLine)
        {
            if (payload == null || string.IsNullOrWhiteSpace(npcId))
                return;
            if (!SidekickCompanion.BindingRootHasSidekick(npcId))
                return;

            foreach (var a in payload.ProposedActions)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.ActionType))
                    continue;
                var t = a.ActionType.Trim().ToLowerInvariant();
                if (t == NpcActionTypes.FollowHero)
                    return;
            }

            if (!LooksLikeFollowAcceptance(payload.Say)
                && !(LooksLikePlayerAskedCompanionToFollow(lastPlayerLine) && LooksLikeNpcAffirmsFollow(payload.Say)))
                return;

            payload.ProposedActions.Add(new NpcProposedAction
            {
                ActionType = NpcActionTypes.FollowHero,
                TargetId = InventoryService.HeroActorId,
                Quantity = 1f,
                Notes = "auto_inferred_from_sidekick_reply"
            });
            DialogueTelemetry.Log("NpcActionSynthesized", $"npc={npcId} synthesized {NpcActionTypes.FollowHero} from sidekick follow-language heuristics.");
        }

        static bool LooksLikeFollowAcceptance(string say)
        {
            if (string.IsNullOrWhiteSpace(say))
                return false;
            var t = say.ToLowerInvariant();
            var offersCompanion =
                t.Contains("follow")
                || t.Contains("with you")
                || t.Contains("come with")
                || t.Contains("come along")
                || t.Contains("go with you")
                || t.Contains("join you")
                || t.Contains("tag along")
                || t.Contains("stick with")
                || t.Contains("alongside")
                || t.Contains("beside you")
                || t.Contains("by your side")
                || t.Contains("right behind")
                || t.Contains("behind you")
                || t.Contains("keep you company")
                || t.Contains("shadow you")
                || t.Contains("trail")
                || t.Contains("walk with")
                || t.Contains("travel with")
                || t.Contains("stay close")
                || t.Contains("stay near")
                || t.Contains("where you go")
                || t.Contains("wherever you")
                || t.Contains("i'll come")
                || t.Contains("i will come")
                || t.Contains("i'm coming")
                || t.Contains("im coming")
                || t.Contains("coming with")
                || t.Contains("at your heels")
                || t.Contains("along for");
            if (!offersCompanion)
                return false;
            if (t.Contains("won't") || t.Contains("cannot") || t.Contains("can't") || t.Contains("refuse"))
                return false;
            return true;
        }

        static bool LooksLikePlayerAskedCompanionToFollow(string playerLine)
        {
            if (string.IsNullOrWhiteSpace(playerLine))
                return false;
            var t = playerLine.ToLowerInvariant();
            return t.Contains("follow me")
                || t.Contains("come with me")
                || t.Contains("join me")
                || t.Contains("tag along")
                || t.Contains("stick with me")
                || t.Contains("accompany me")
                || t.Contains("walk with me")
                || t.Contains("travel with me");
        }

        static bool LooksLikeNpcAffirmsFollow(string say)
        {
            if (string.IsNullOrWhiteSpace(say))
                return false;
            var t = say.ToLowerInvariant();
            if (t.Contains("won't") || t.Contains("cannot") || t.Contains("can't") || t.Contains("refuse"))
                return false;
            return t.Contains("yes")
                || t.Contains("yeah")
                || t.Contains("yep")
                || t.Contains("sure")
                || t.Contains("of course")
                || t.Contains("absolutely")
                || t.Contains("definitely")
                || t.Contains("gladly")
                || t.Contains("happy to")
                || t.Contains("i'd love")
                || t.Contains("i will")
                || t.Contains("let's ")
                || t.Contains("lets ")
                || t.Contains("okay")
                || t.Contains("ok ")
                || t.StartsWith("ok.")
                || t.Contains("alright")
                || t.Contains("lead the way")
                || t.Contains("right away")
                || t.Contains("ready when you");
        }
    }
}
