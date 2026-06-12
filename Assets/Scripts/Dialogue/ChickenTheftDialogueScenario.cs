using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rpg.Core;
using Rpg.Npc;

namespace Rpg.Dialogue
{
    /// <summary>Chicken-theft confrontation state and helpers extracted from <see cref="DialogueManager"/>.</summary>
    public sealed class ChickenTheftDialogueScenario
    {
        const string ShoutFallback = "Hey—stop there! You're stealing my chicken!";

        string _activeNpcId;
        bool _exchangeCompleted;
        /// <summary>Only this NPC may treat the hero's live chicken as a theft against them in dialogue prompts.</summary>
        string _incidentVictimNpcId;

        public string IncidentVictimNpcId => _incidentVictimNpcId;

        public bool CanStartConfrontationDialogue(NpcDefinition npc, string forcedNpcOpening)
        {
            return npc != null && !string.IsNullOrWhiteSpace(forcedNpcOpening);
        }

        public void OnDialogueStart(string npcId, bool chickenTheftSession)
        {
            if (chickenTheftSession)
            {
                _activeNpcId = npcId;
                _exchangeCompleted = false;
            }
            else
            {
                _activeNpcId = null;
                _exchangeCompleted = false;
            }
        }

        /// <summary>
        /// Runs theft-session cleanup when dialogue ends. Returns the NPC id to skip guide return for, if any.
        /// </summary>
        public string OnDialogueEnd(string closingNpcId)
        {
            var theftSession = !string.IsNullOrWhiteSpace(_activeNpcId)
                && !string.IsNullOrWhiteSpace(closingNpcId)
                && string.Equals(_activeNpcId, closingNpcId, StringComparison.OrdinalIgnoreCase);
            var theftResolved = _exchangeCompleted;
            if (theftSession && !theftResolved)
                NpcChickenTheftConfrontation.NotifyDialogueClosedWithoutTrade(closingNpcId);

            _activeNpcId = null;
            _exchangeCompleted = false;

            return theftSession ? closingNpcId : null;
        }

        public void RegisterIncidentVictim(string npcId)
        {
            _incidentVictimNpcId = string.IsNullOrWhiteSpace(npcId) ? null : npcId.Trim();
        }

        public void ClearIncidentVictim()
        {
            _incidentVictimNpcId = null;
        }

        /// <summary>
        /// While the hero carries a live chicken, hide it from LLM inventory text unless the speaking NPC is the
        /// registered theft victim (or no victim yet — then hide from everyone until a confrontation assigns one).
        /// </summary>
        public bool ShouldRedactLiveChickenInPromptForNpc(InventoryService inventory, string speakingNpcId)
        {
            if (inventory == null || string.IsNullOrWhiteSpace(speakingNpcId))
                return false;
            if (!inventory.HasAtLeast(InventoryService.HeroActorId, GameConstants.LiveChickenItemId, 1))
                return false;
            if (string.IsNullOrWhiteSpace(_incidentVictimNpcId))
                return true;
            return !string.Equals(
                speakingNpcId.Trim(),
                _incidentVictimNpcId,
                StringComparison.OrdinalIgnoreCase);
        }

        public void NotifyReparationIfNeeded(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId) || string.IsNullOrWhiteSpace(_activeNpcId))
                return;
            if (!string.Equals(npcId, _activeNpcId, StringComparison.OrdinalIgnoreCase))
                return;
            _exchangeCompleted = true;
            NpcChickenTheftConfrontation.NotifyReparationComplete(npcId);
        }

        /// <summary>Chicken-theft confrontation always accepts hero-to-NPC receive/trade during an active session.</summary>
        public bool TryGetWillingnessForTransfer(string npcId, string intent, out string reason)
        {
            reason = "not enough trust in this context.";
            var intentKey = (intent ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(_activeNpcId)
                && string.Equals(npcId, _activeNpcId, StringComparison.OrdinalIgnoreCase)
                && (intentKey == "receive" || intentKey == "trade"))
            {
                reason = "this settles the matter of my chicken.";
                return true;
            }

            return false;
        }

        public async Task<string> RequestShoutLineAsync(
            OllamaClient ollamaClient,
            OllamaSettings ollamaSettings,
            CancellationToken cancellationToken)
        {
            if (ollamaClient == null || ollamaSettings == null)
                return ShoutFallback;
            var system =
                "You write one short line of shouted dialogue for a fantasy villager whose chicken was stolen. "
                + "No quotes, no stage directions, no JSON, one sentence only, under 140 characters.";
            var user =
                "Write one line the villager yells at the thief, like: Hey, stop — you're stealing my chickens! "
                + "Vary the wording but keep the same meaning.";
            try
            {
                var messages = new List<OllamaMessageDto>
                {
                    new OllamaMessageDto("system", system),
                    new OllamaMessageDto("user", user)
                };
                var result = await ollamaClient.ChatAsync(
                    messages,
                    ollamaSettings.model,
                    cancellationToken);
                if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.AssistantContent))
                    return result.AssistantContent.Trim();
                DialogueTelemetry.Log("ChickenTheftShoutFail", result.Error ?? "empty assistant content");
            }
            catch (Exception ex)
            {
                DialogueTelemetry.Log("ChickenTheftShoutFail", ex.Message);
            }

            return ShoutFallback;
        }
    }
}
