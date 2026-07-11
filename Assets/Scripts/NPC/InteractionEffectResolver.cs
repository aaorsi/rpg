using System;
using System.Collections.Generic;
using Rpg.Dialogue;
using UnityEngine;

namespace Rpg.Npc
{
    public static class InteractionEffectResolver
    {
        public readonly struct StepResult
        {
            public StepResult(bool success, string summary)
            {
                Success = success;
                Summary = summary ?? string.Empty;
            }

            public bool Success { get; }
            public string Summary { get; }
        }

        public static string ResolveOutcomeId(
            InteractionDefinition definition,
            InteractionRuntimeInstance instance)
        {
            var fromSteps = InferOutcomeFromStepLog(definition, instance);
            if (!string.IsNullOrWhiteSpace(fromSteps))
                return fromSteps;
            return NpcInteractionMemoryService.ResolveWeightedOutcomeId(definition);
        }

        static string InferOutcomeFromStepLog(
            InteractionDefinition definition,
            InteractionRuntimeInstance instance)
        {
            if (instance?.stepLog == null || instance.stepLog.Count == 0)
                return string.Empty;

            var joined = string.Join(" ", instance.stepLog).ToLowerInvariant();
            var interactionId = (instance.interactionId ?? string.Empty).Trim().ToLowerInvariant();
            switch (interactionId)
            {
                case "steal":
                    if (joined.IndexOf("steal failed", StringComparison.Ordinal) >= 0
                        || joined.IndexOf("nothing to take", StringComparison.Ordinal) >= 0)
                        return "detected";
                    if (joined.IndexOf("stole ", StringComparison.Ordinal) >= 0)
                        return "undetected";
                    break;
                case "bribe":
                    if (joined.IndexOf("payment failed", StringComparison.Ordinal) >= 0
                        || joined.IndexOf("coin exchange skipped", StringComparison.Ordinal) >= 0
                        || joined.IndexOf("coin transfer failed", StringComparison.Ordinal) >= 0)
                        return "rejected_or_exposed";
                    if (joined.IndexOf("paid ", StringComparison.Ordinal) >= 0
                        && joined.IndexOf("errand", StringComparison.Ordinal) >= 0)
                        return "accepted";
                    break;
                case "romantic_relationship":
                    if (joined.IndexOf("gifted ", StringComparison.Ordinal) >= 0)
                        return "mutual_bond";
                    if (joined.IndexOf("gift failed", StringComparison.Ordinal) >= 0
                        || joined.IndexOf("gift skipped", StringComparison.Ordinal) >= 0)
                        return "rejected";
                    break;
                case "start_cult":
                case "cult_conversion":
                    if (joined.IndexOf("tithed ", StringComparison.Ordinal) >= 0)
                        return interactionId == "cult_conversion" ? "converted" : "cult_forms";
                    if (joined.IndexOf("tithe declined", StringComparison.Ordinal) >= 0)
                        return interactionId == "cult_conversion" ? "resisted" : "fails_to_form";
                    break;
                case "offer_services":
                    if (joined.IndexOf("payment failed", StringComparison.Ordinal) >= 0
                        || joined.IndexOf("coin exchange skipped", StringComparison.Ordinal) >= 0)
                        return "abandoned";
                    if (joined.IndexOf("transferred ", StringComparison.Ordinal) >= 0)
                        return "completed";
                    break;
            }

            if (definition?.outcomes == null)
                return string.Empty;

            for (var i = 0; i < definition.outcomes.Count; i++)
            {
                var outcome = definition.outcomes[i];
                if (outcome == null || string.IsNullOrWhiteSpace(outcome.id))
                    continue;
                var id = outcome.id.Trim().ToLowerInvariant().Replace('_', ' ');
                if (joined.IndexOf(id, StringComparison.Ordinal) >= 0)
                    return outcome.id.Trim();
            }

            return string.Empty;
        }

        public static string ResolveInteractionGoal(InteractionDefinition definition)
        {
            if (definition == null)
                return "Complete the village interaction.";
            if (!string.IsNullOrWhiteSpace(definition.goal))
                return definition.goal.Trim();
            return ResolveDefaultGoal(definition.id);
        }

        public static string ResolveInteractionGoalFromId(string interactionId) => ResolveDefaultGoal(interactionId);

        public static StepResult ApplyExchangeItem(
            InventoryService inventory,
            InteractionRuntimeInstance instance,
            InteractionActionStep step,
            string actorNpcId,
            string targetNpcId)
        {
            if (inventory == null || instance == null || step == null)
                return new StepResult(false, "exchange_item skipped (no inventory)");

            var mode = ResolveParameter(step, "mode");
            switch (mode)
            {
                case "steal_random_item":
                    return ApplyStealRandomItem(inventory, actorNpcId, targetNpcId);
                case "gift_optional":
                    if (UnityEngine.Random.value > 0.45f)
                        return new StepResult(false, "gift skipped — optional");
                    return ApplyOptionalGift(inventory, actorNpcId, targetNpcId);
                default:
                    return new StepResult(false, $"exchange_item unsupported mode '{mode}'");
            }
        }

        public static StepResult ApplyExchangeCoins(
            InventoryService inventory,
            InteractionRuntimeInstance instance,
            InteractionDefinition definition,
            InteractionActionStep step,
            string actorNpcId,
            string targetNpcId,
            IReadOnlyList<string> locationIds,
            AgreementService agreements)
        {
            if (inventory == null || instance == null || step == null)
                return new StepResult(false, "exchange_coins skipped (no inventory)");

            var mode = ResolveParameter(step, "mode");
            if (string.Equals(mode, "optional_tithe_small", StringComparison.OrdinalIgnoreCase)
                && UnityEngine.Random.value > 0.5f)
            {
                return new StepResult(false, "tithe declined — optional");
            }

            var amountMode = ResolveParameter(step, "amountMode");
            var payer = actorNpcId;
            var payee = targetNpcId;
            var amount = ResolveCoinAmount(inventory, instance, step, payer, mode, amountMode);
            if (amount <= 0)
                return new StepResult(false, "coin exchange skipped (zero amount)");

            if (!inventory.HasAtLeast(payer, InventoryService.CoinItemId, amount))
            {
                var payerName = payer;
                return new StepResult(false, $"payment failed — {payerName} has only {inventory.GetCoinBalance(payer)} coins (needs {amount})");
            }

            if (!inventory.TryPayWage(payer, payee, amount))
                return new StepResult(false, $"coin transfer failed ({amount} coins)");

            if (string.Equals(instance.interactionId, "bribe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "bribe_offer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "covert_offer", StringComparison.OrdinalIgnoreCase))
            {
                var errand = BuildBribeErrand(inventory, instance, locationIds, payee, payer, amount);
                instance.assignedErrand = errand;
                if (agreements != null)
                {
                    agreements.CreateOffer(
                        AgreementType.Hire,
                        payee,
                        payer,
                        payee,
                        amount,
                        errand,
                        "interaction_bribe");
                }

                return new StepResult(true, $"paid {amount} coins — errand: {errand}");
            }

            if (string.Equals(mode, "optional_tithe_small", StringComparison.OrdinalIgnoreCase))
                return new StepResult(true, $"tithed {amount} coins");

            return new StepResult(true, $"transferred {amount} coins");
        }

        public static string ResolveOutcomeSummary(
            InteractionDefinition definition,
            InteractionRuntimeInstance instance,
            string outcomeId)
        {
            if (instance == null)
                return string.Empty;
            if (instance.stepLog != null && instance.stepLog.Count > 0)
            {
                var joined = string.Join(" → ", instance.stepLog);
                if (!string.IsNullOrWhiteSpace(instance.outcomeSummary))
                    return instance.outcomeSummary + " | steps: " + joined;
                return joined;
            }

            if (definition?.outcomes == null || string.IsNullOrWhiteSpace(outcomeId))
                return instance.outcomeSummary ?? string.Empty;

            InteractionOutcome matched = null;
            for (var i = 0; i < definition.outcomes.Count; i++)
            {
                var outcome = definition.outcomes[i];
                if (outcome == null || string.IsNullOrWhiteSpace(outcome.id))
                    continue;
                if (string.Equals(outcome.id, outcomeId, StringComparison.OrdinalIgnoreCase))
                {
                    matched = outcome;
                    break;
                }
            }

            if (matched?.effects == null || matched.effects.Count == 0)
                return HumanizeOutcomeId(outcomeId);

            var parts = new List<string>();
            for (var i = 0; i < matched.effects.Count; i++)
                parts.Add(HumanizeEffect(matched.effects[i], instance));
            return string.Join("; ", parts);
        }

        public static void ApplyOutcomeState(
            InteractionRuntimeInstance instance,
            InteractionDefinition definition,
            string outcomeId,
            VillageOpinionService opinions)
        {
            if (instance == null || definition?.outcomes == null)
                return;

            InteractionOutcome matched = null;
            for (var i = 0; i < definition.outcomes.Count; i++)
            {
                var outcome = definition.outcomes[i];
                if (outcome == null || string.IsNullOrWhiteSpace(outcome.id))
                    continue;
                if (string.Equals(outcome.id, outcomeId, StringComparison.OrdinalIgnoreCase))
                {
                    matched = outcome;
                    break;
                }
            }

            if (matched?.effects == null)
                return;

            for (var i = 0; i < matched.effects.Count; i++)
            {
                var effect = matched.effects[i];
                if (string.IsNullOrWhiteSpace(effect))
                    continue;
                if (effect.IndexOf("follower", StringComparison.OrdinalIgnoreCase) >= 0)
                    instance.targetIsFollower = true;
            }

            instance.outcomeSummary = ResolveOutcomeSummary(definition, instance, outcomeId);
            if (instance.targetIsFollower && instance.outcomeSummary.IndexOf("follower", StringComparison.OrdinalIgnoreCase) < 0)
                instance.outcomeSummary = "target became follower; " + instance.outcomeSummary;

            if (opinions == null)
                return;
            for (var i = 0; i < matched.effects.Count; i++)
            {
                var effect = matched.effects[i];
                if (string.IsNullOrWhiteSpace(effect))
                    continue;
                if (effect.IndexOf("relationship_positive", StringComparison.OrdinalIgnoreCase) >= 0
                    || effect.IndexOf("mutual_bond", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    opinions.QueueInteraction(instance.actorNpcId, instance.targetNpcId);
                }
            }
        }

        static StepResult ApplyStealRandomItem(InventoryService inventory, string fromActorId, string toActorId)
        {
            if (inventory.TryStealRandomItem(fromActorId, toActorId, out _, out var display))
                return new StepResult(true, $"stole {display} from target");
            return new StepResult(false, "steal failed — target had nothing to take");
        }

        static StepResult ApplyOptionalGift(InventoryService inventory, string giverId, string receiverId)
        {
            var items = inventory.GetStealableItems(giverId);
            if (items == null || items.Count == 0)
                return new StepResult(false, "gift skipped — initiator has nothing to offer");
            var pick = items[0];
            if (inventory.TryTransfer(giverId, receiverId, pick.itemId, 1))
                return new StepResult(true, $"gifted {pick.displayName}");
            return new StepResult(false, "gift failed");
        }

        static int ResolveCoinAmount(
            InventoryService inventory,
            InteractionRuntimeInstance instance,
            InteractionActionStep step,
            string payerActorId,
            string mode,
            string amountMode)
        {
            if (step?.parameters != null && step.parameters.TryGetValue("amount", out var rawAmount)
                && int.TryParse(rawAmount, out var fixedAmount))
            {
                return Mathf.Max(1, fixedAmount);
            }

            var balance = inventory.GetCoinBalance(payerActorId);
            if (string.Equals(amountMode, "agreed_or_contextual", StringComparison.OrdinalIgnoreCase)
                || string.Equals(amountMode, "llm_or_contextual", StringComparison.OrdinalIgnoreCase)
                || string.Equals(amountMode, "contextual", StringComparison.OrdinalIgnoreCase))
                return Mathf.Clamp(balance / 4, 2, 12);
            if (string.Equals(mode, "optional_tithe_small", StringComparison.OrdinalIgnoreCase))
                return Mathf.Clamp(UnityEngine.Random.Range(1, 6), 1, Mathf.Max(1, balance));
            if (string.Equals(instance?.interactionId, "bribe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "bribe_offer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "covert_offer", StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Clamp(UnityEngine.Random.Range(5, 16), 1, Mathf.Max(1, balance));
            }

            return Mathf.Clamp(UnityEngine.Random.Range(3, 11), 1, Mathf.Max(1, balance));
        }

        static string BuildBribeErrand(
            InventoryService inventory,
            InteractionRuntimeInstance instance,
            IReadOnlyList<string> locationIds,
            string workerNpcId,
            string clientNpcId,
            int payout)
        {
            var location = "the marked location";
            if (locationIds != null && locationIds.Count > 0)
            {
                var seed = (instance?.instanceId ?? string.Empty).GetHashCode();
                location = locationIds[Mathf.Abs(seed) % locationIds.Count];
            }

            var itemLabel = "supplies";
            var catalogItems = inventory.GetAllKnownItemIds();
            if (catalogItems != null && catalogItems.Count > 0)
            {
                var filtered = new List<string>();
                for (var i = 0; i < catalogItems.Count; i++)
                {
                    var id = catalogItems[i];
                    if (string.IsNullOrWhiteSpace(id) || string.Equals(id, InventoryService.CoinItemId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    filtered.Add(id);
                }

                if (filtered.Count > 0)
                {
                    var seed = (workerNpcId + "|" + clientNpcId).GetHashCode();
                    itemLabel = inventory.GetItemDisplayName(filtered[Mathf.Abs(seed) % filtered.Count]);
                }
            }

            var worker = workerNpcId;
            var client = clientNpcId;
            return $"For {payout} coins: go to {location}, pick up {itemLabel}, bring it to {client}";
        }

        static string ResolveDefaultGoal(string interactionId)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
                return "Complete the village interaction.";
            switch (interactionId.Trim().ToLowerInvariant())
            {
                case "steal":
                    return "Steal an item from the target without being caught.";
                case "romantic_relationship":
                    return "Build a romantic bond with the target.";
                case "start_cult":
                    return "Recruit the target into your group.";
                case "bribe":
                    return "Pay coins so the target runs an errand for you.";
                case "offer_services":
                    return "Offer a service and get paid when it is done.";
                case "elect_mayor":
                    return "Persuade the target to support the hero as mayor.";
                case "cult_conversion":
                    return "Convert the target to your beliefs.";
                default:
                    return "Complete the interaction with a clear outcome.";
            }
        }

        static string HumanizeOutcomeId(string outcomeId) =>
            string.IsNullOrWhiteSpace(outcomeId) ? "completed" : outcomeId.Replace('_', ' ');

        static string HumanizeEffect(string effect, InteractionRuntimeInstance instance)
        {
            if (string.IsNullOrWhiteSpace(effect))
                return "resolved";
            switch (effect.Trim().ToLowerInvariant())
            {
                case "follower_tag_applied":
                    return "target became follower";
                case "initiator_gain_item":
                    return "initiator gained an item";
                case "target_unaware":
                    return "target remained unaware";
                case "target_aware":
                    return "target noticed the theft";
                case "relationship_positive":
                    return "relationship improved";
                case "relationship_negative_short_term":
                    return "relationship soured";
                case "leader_influence_up":
                    return "leader influence increased";
                case "provider_reputation_up":
                    return "provider reputation improved";
                case "hero_mayor_support_up":
                    return "mayor support grew";
                default:
                    if (instance != null && !string.IsNullOrWhiteSpace(instance.assignedErrand)
                        && effect.IndexOf("goal", StringComparison.OrdinalIgnoreCase) >= 0)
                        return instance.assignedErrand;
                    return effect.Replace('_', ' ');
            }
        }

        static string ResolveParameter(InteractionActionStep step, string key)
        {
            if (step?.parameters == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;
            return step.parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim().ToLowerInvariant()
                : string.Empty;
        }
    }
}
