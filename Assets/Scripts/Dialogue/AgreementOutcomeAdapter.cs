using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Bridges dialogue/social payload outputs into agreement lifecycle changes.
    /// Keeps the mapping tolerant so richer social contract output can land independently.
    /// </summary>
    public static class AgreementOutcomeAdapter
    {
        public static void ApplyFromDialoguePayload(
            AgreementService agreementService,
            InventoryService inventoryService,
            string npcId,
            AssistantModelPayload payload,
            string heroActorId = InventoryService.HeroActorId)
        {
            if (agreementService == null || payload == null || string.IsNullOrWhiteSpace(npcId))
                return;

            foreach (var contractType in AllContractTypes())
            {
                ApplySignalDrivenTransitions(agreementService, inventoryService, npcId, payload, contractType, heroActorId);
                ApplyOutcomeFallbackTransitions(agreementService, npcId, payload.InteractionOutcome, contractType);
            }
        }

        static void ApplySignalDrivenTransitions(
            AgreementService agreementService,
            InventoryService inventoryService,
            string npcId,
            AssistantModelPayload payload,
            AgreementType type,
            string heroActorId)
        {
            var signalState = ResolveStateSignal(payload, type);
            if (string.IsNullOrWhiteSpace(signalState))
                return;

            var payoutCoins = ResolvePayoutCoins(payload, type);
            var record = agreementService.FindMostRecentByNpcAndType(npcId, type);
            if (record == null)
            {
                record = agreementService.CreateOffer(
                    type,
                    npcId,
                    payerActorId: npcId,
                    payeeActorId: heroActorId,
                    payoutCoins: payoutCoins,
                    summary: $"{type.ToString().ToLowerInvariant()} contract",
                    notes: "auto-created from dialogue payload");
            }

            var stateKey = signalState.Trim().ToLowerInvariant();
            if (stateKey == "offered")
                return;
            if (stateKey == "accepted")
            {
                agreementService.TryAccept(record.agreementId);
                return;
            }

            if (stateKey == "in_progress")
            {
                agreementService.TryAccept(record.agreementId);
                agreementService.TryStart(record.agreementId);
                return;
            }

            if (stateKey == "completed")
            {
                agreementService.TryAccept(record.agreementId);
                agreementService.TryStart(record.agreementId);
                if (!agreementService.TryComplete(record.agreementId, inventoryService, out var completionError))
                    DialogueTelemetry.Log("AgreementCompleteFailed", $"{record.agreementId}:{completionError}");
                return;
            }

            if (stateKey == "failed")
            {
                agreementService.TryFail(record.agreementId, "failed from dialogue payload");
                return;
            }

            if (stateKey == "expired")
                agreementService.TryExpire(record.agreementId, "expired from dialogue payload");
        }

        static void ApplyOutcomeFallbackTransitions(
            AgreementService agreementService,
            string npcId,
            string interactionOutcome,
            AgreementType type)
        {
            if (string.IsNullOrWhiteSpace(interactionOutcome))
                return;
            var outcome = interactionOutcome.Trim().ToLowerInvariant();
            var rec = agreementService.FindMostRecentByNpcAndType(npcId, type);
            if (rec == null)
                return;

            if (outcome == "cooperate")
            {
                agreementService.TryAccept(rec.agreementId);
                return;
            }

            if (outcome == "reject" || outcome == "counter_offer")
                agreementService.TryFail(rec.agreementId, $"social_outcome:{outcome}");
        }

        static string ResolveStateSignal(AssistantModelPayload payload, AgreementType type)
        {
            if (payload == null)
                return string.Empty;
            var typeKey = type.ToString().ToLowerInvariant();
            var candidates = new[]
            {
                $"{typeKey}_agreement",
                $"{typeKey}_contract",
                $"agreement_{typeKey}",
                $"contract_{typeKey}"
            };

            foreach (var key in candidates)
            {
                if (payload.StateDeltas != null && payload.StateDeltas.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return NormalizeState(value);
            }

            if (payload.MilestoneSignals == null)
                return string.Empty;
            for (var i = 0; i < payload.MilestoneSignals.Count; i++)
            {
                var s = payload.MilestoneSignals[i];
                if (string.IsNullOrWhiteSpace(s))
                    continue;
                var parsed = TryParseMilestoneAgreementState(s, typeKey);
                if (!string.IsNullOrWhiteSpace(parsed))
                    return parsed;
            }

            return string.Empty;
        }

        static int ResolvePayoutCoins(AssistantModelPayload payload, AgreementType type)
        {
            if (payload == null || payload.StateDeltas == null)
                return 0;
            var typeKey = type.ToString().ToLowerInvariant();
            var keys = new[]
            {
                $"{typeKey}_agreement_coins",
                $"{typeKey}_contract_coins",
                "agreement_coins",
                "contract_coins"
            };
            foreach (var key in keys)
            {
                if (!payload.StateDeltas.TryGetValue(key, out var raw))
                    continue;
                if (int.TryParse(raw, out var parsed))
                    return Mathf.Max(0, parsed);
            }

            return 0;
        }

        static string TryParseMilestoneAgreementState(string signal, string typeKey)
        {
            // Supports milestone tokens like:
            // agreement:hire:offered
            // contract:advice:completed
            var parts = signal.Trim().ToLowerInvariant().Split(':');
            if (parts.Length < 3)
                return string.Empty;
            var root = parts[0];
            if (root != "agreement" && root != "contract")
                return string.Empty;
            if (parts[1] != typeKey)
                return string.Empty;
            return NormalizeState(parts[2]);
        }

        static string NormalizeState(string raw)
        {
            var key = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToLowerInvariant();
            if (key == "inprogress")
                return "in_progress";
            if (key == "offer")
                return "offered";
            if (key == "accept")
                return "accepted";
            if (key == "complete")
                return "completed";
            if (key == "fail")
                return "failed";
            if (key == "expire")
                return "expired";
            return key;
        }

        static IEnumerable<AgreementType> AllContractTypes()
        {
            yield return AgreementType.Hire;
            yield return AgreementType.Advice;
            yield return AgreementType.Persuasion;
        }
    }
}
