using System.Collections.Generic;
using System.Text;
using Rpg.Npc;
using Rpg.Player;
using UnityEngine;

namespace Rpg.Dialogue
{
    public sealed class NpcActionExecutor
    {
        readonly InventoryService _inventory;
        readonly LocationBindingRegistry _locations;
        readonly QuestStateService _quests;
        readonly NarrativeReferenceValidator _refs;

        public NpcActionExecutor(
            InventoryService inventory,
            LocationBindingRegistry locations = null,
            QuestStateService quests = null,
            NarrativeReferenceValidator refs = null)
        {
            _inventory = inventory;
            _locations = locations;
            _quests = quests;
            _refs = refs;
        }

        readonly HashSet<string> _allowed =
            new HashSet<string>
            {
                "move_to_location",
                "move_to_npc",
                "follow_hero",
                "give_object",
                "receive_object",
                "trade",
                "activate_object",
                "find_object",
                "inspect_location",
                "refer_to_npc"
            };

        public void ExecuteValidated(string npcId, string heroActorId, IReadOnlyList<NpcProposedAction> actions)
        {
            if (actions == null || actions.Count == 0)
                return;
            if (GhoulMenaceController.IsGhoulStoryNpcId(npcId))
                return;
            var normalizedActions = new List<NpcProposedAction>(actions);
            ResponseValidator.NormalizeGuideActionTypes(normalizedActions);
            var npcGo = ResolveNpcGameObject(npcId);
            var isSidekickNpc = npcGo != null && SidekickCompanion.FindForNpcBindingRoot(npcGo) != null;
            foreach (var action in normalizedActions)
            {
                if (action == null || string.IsNullOrWhiteSpace(action.ActionType))
                    continue;
                var type = action.ActionType.Trim().ToLowerInvariant();
                if (!_allowed.Contains(type))
                {
                    DialogueTelemetry.Log("NpcActionRejected", $"npc={npcId}, type={type}");
                    continue;
                }

                var qty = Mathf.Max(1, Mathf.RoundToInt(action.Quantity <= 0f ? 1f : action.Quantity));
                var target = string.IsNullOrWhiteSpace(action.TargetId) ? string.Empty : action.TargetId.Trim();
                CanonicalizeNpcVisitAction(ref type, ref target, action.Notes);
                action.ActionType = type;
                action.TargetId = target;

                if (isSidekickNpc && (type == "move_to_location" || type == "move_to_npc" || type == "refer_to_npc"))
                {
                    DialogueTelemetry.Log("NpcActionRejected", $"npc={npcId} sidekick override blocks guiding action type={type}");
                    continue;
                }

                if (!isSidekickNpc && type == "follow_hero")
                {
                    DialogueTelemetry.Log("NpcActionRejected", $"npc={npcId} non-sidekick cannot execute follow_hero");
                    continue;
                }

                var refIssues = _refs != null ? _refs.ValidateAction(action) : null;
                if (refIssues != null && refIssues.Count > 0)
                {
                    DialogueTelemetry.Log("NpcActionRefInvalid", string.Join(" | ", refIssues));
                    continue;
                }
                switch (type)
                {
                    case "give_object":
                        if (!string.IsNullOrWhiteSpace(target) && _inventory != null && _inventory.TryTransfer(npcId, heroActorId, target, qty))
                            DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} gave {target} x{qty} to {heroActorId}");
                        else
                            DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} failed give_object target={target}");
                        break;
                    case "receive_object":
                        if (!string.IsNullOrWhiteSpace(target) && _inventory != null && _inventory.TryTransfer(heroActorId, npcId, target, qty))
                        {
                            DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} received {target} x{qty} from {heroActorId}");
                            DialogueManager.Instance?.NotifyChickenTheftReparationFromNpcAction(npcId);
                        }
                        else
                            DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} failed receive_object target={target}");
                        break;
                    case "trade":
                        // Minimal trade v1: target item moves from NPC to hero when available.
                        if (!string.IsNullOrWhiteSpace(target) && _inventory != null && _inventory.TryTransfer(npcId, heroActorId, target, qty))
                            DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} traded(out) {target} x{qty}");
                        else
                            DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} failed trade target={target}");
                        break;
                    case "find_object":
                        if (!string.IsNullOrWhiteSpace(target) && _inventory != null)
                        {
                            _inventory.AddItem(npcId, target, qty);
                            DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} found {target} x{qty}");
                        }
                        break;
                    case "move_to_location":
                    case "move_to_npc":
                        if (TryResolveTargetWithFallback(target, out var anchor) && anchor != null)
                        {
                            var moverGo = ResolveNpcGameObject(npcId);
                            if (moverGo != null)
                            {
                                var guide = moverGo.GetComponent<NpcGuideToLocation>();
                                if (guide == null)
                                    guide = moverGo.AddComponent<NpcGuideToLocation>();
                                guide.Begin(npcId, target, anchor, speedMetersPerSec: ResolveHeroWalkSpeed(), stopDistanceMeters: 10f);
                                DialogueManager.Instance?.AppendGuideNavigationSystem($"NPC is heading to {target}.");
                                DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} guiding to {target} ({anchor.position})");
                            }
                            else
                                DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} no gameObject found for move_to_location");
                        }
                        else
                            DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} unresolved location target={target}");
                        break;
                    case "inspect_location":
                        if (_locations != null && _locations.TryResolve(target, out _))
                        {
                            _quests?.ApplySignals(npcId, new[] { "hint:" + target });
                            DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} inspected {target}");
                        }
                        else
                            DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} cannot inspect unresolved location={target}");
                        break;
                    case "activate_object":
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            _quests?.ApplySignals(npcId, new[] { "unlock:" + target });
                            DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} activated {target}");
                        }
                        break;
                    case "refer_to_npc":
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            if (TryResolveNpcAnchor(target, out var npcAnchor) && npcAnchor != null)
                            {
                                var moverGo = ResolveNpcGameObject(npcId);
                                if (moverGo != null)
                                {
                                    var guide = moverGo.GetComponent<NpcGuideToLocation>();
                                    if (guide == null)
                                        guide = moverGo.AddComponent<NpcGuideToLocation>();
                                    guide.Begin(npcId, target, npcAnchor, speedMetersPerSec: ResolveHeroWalkSpeed(), stopDistanceMeters: 10f);
                                    DialogueManager.Instance?.AppendGuideNavigationSystem($"NPC is heading to {target}.");
                                    DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} guiding to npc={target} ({npcAnchor.position})");
                                }
                                else
                                    DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} no gameObject found for refer_to_npc");
                            }
                            else
                                DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} unresolved npc target={target}");
                        }
                        break;
                    case "follow_hero":
                        if (TryEnableSidekickFollow(npcId, out var enabledName))
                            DialogueTelemetry.Log("NpcActionApplied", $"npc={npcId} sidekick follow enabled ({enabledName})");
                        else
                            DialogueTelemetry.Log("NpcActionNoop", $"npc={npcId} cannot follow hero (not sidekick or object missing)");
                        break;
                    default:
                        // Movement / location / activation hooks remain telemetry-only in v1.
                        Debug.Log($"[NpcActionExecutor] npc={npcId} action={type} target={action.TargetId} qty={action.Quantity} notes={action.Notes}");
                        break;
                }
            }
        }

        bool TryResolveTargetWithFallback(string target, out Transform anchor)
        {
            anchor = null;
            if (string.IsNullOrWhiteSpace(target))
                return false;
            if (TryResolveNpcAnchor(target, out anchor))
                return true;
            if (_locations != null && _locations.TryResolve(target, out anchor) && anchor != null)
                return true;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (string.Equals(t.gameObject.name, target, System.StringComparison.OrdinalIgnoreCase))
                {
                    anchor = t;
                    return true;
                }
            }
            return false;
        }

        static void CanonicalizeNpcVisitAction(ref string actionType, ref string target, string notes)
        {
            if (string.IsNullOrWhiteSpace(actionType))
                return;
            var t = actionType.Trim().ToLowerInvariant();
            if (t == "refer_to_npc" || t == "move_to_npc")
            {
                if (TryResolveNpcIdFromText(target, out var resolvedNpcId)
                    || TryResolveNpcIdFromText(notes, out resolvedNpcId))
                {
                    actionType = "refer_to_npc";
                    target = resolvedNpcId;
                }
                return;
            }

            if (t != "move_to_location")
                return;
            // Model sometimes emits location-like aliases (e.g. wagon_site) when the actual intent is visiting an NPC.
            var looksLikeAlias = !string.IsNullOrWhiteSpace(target)
                && (target.IndexOf("site", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || target.IndexOf("spot", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || target.IndexOf("place", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (!looksLikeAlias && string.IsNullOrWhiteSpace(notes))
                return;
            if (TryResolveNpcIdFromText(target, out var npcId) || TryResolveNpcIdFromText(notes, out npcId))
            {
                actionType = "refer_to_npc";
                target = npcId;
            }
        }

        static bool TryResolveNpcIdFromText(string text, out string npcId)
        {
            npcId = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var normalizedText = NormalizeTokenText(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
                return false;
            foreach (var b in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.Definition == null)
                    continue;
                var id = b.Definition.npcId?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var display = b.Definition.displayName?.Trim() ?? string.Empty;
                var goName = b.gameObject != null ? (b.gameObject.name ?? string.Empty).Trim() : string.Empty;

                if (ContainsKey(normalizedText, id)
                    || ContainsKey(normalizedText, display)
                    || ContainsKey(normalizedText, goName))
                {
                    npcId = id;
                    return true;
                }
            }
            return false;
        }

        static bool ContainsKey(string normalizedText, string rawKey)
        {
            if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(rawKey))
                return false;
            var key = NormalizeTokenText(rawKey);
            if (string.IsNullOrWhiteSpace(key))
                return false;
            if (normalizedText.Equals(key, System.StringComparison.Ordinal))
                return true;
            return normalizedText.Contains(" " + key + " ", System.StringComparison.Ordinal);
        }

        static string NormalizeTokenText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            var sb = new StringBuilder(raw.Length + 4);
            sb.Append(' ');
            for (var i = 0; i < raw.Length; i++)
            {
                var c = char.ToLowerInvariant(raw[i]);
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else
                    sb.Append(' ');
            }
            sb.Append(' ');
            return sb.ToString();
        }

        static bool TryResolveNpcAnchor(string target, out Transform anchor)
        {
            anchor = null;
            if (string.IsNullOrWhiteSpace(target))
                return false;
            var key = target.Trim();
            foreach (var b in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null)
                    continue;
                var npcId = b.Definition.npcId ?? string.Empty;
                var display = b.Definition.displayName ?? string.Empty;
                var goName = b.gameObject.name ?? string.Empty;
                if (string.Equals(npcId, key, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(display, key, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(goName, key, System.StringComparison.OrdinalIgnoreCase))
                {
                    anchor = b.transform;
                    return anchor != null;
                }
            }
            return false;
        }

        static GameObject ResolveNpcGameObject(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return null;
            foreach (var b in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.gameObject == null || b.Definition == null || string.IsNullOrWhiteSpace(b.Definition.npcId))
                    continue;
                if (string.Equals(b.Definition.npcId, npcId, System.StringComparison.OrdinalIgnoreCase))
                    return b.gameObject;
            }
            return null;
        }

        static float ResolveHeroWalkSpeed()
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.TryGetComponent<PlayerClickMove>(out var move))
                return Mathf.Clamp(move.WalkSpeed, 0.5f, 8f);
            return 4.5f;
        }

        static bool TryEnableSidekickFollow(string npcId, out string npcName)
        {
            npcName = string.Empty;
            var npcGo = ResolveNpcGameObject(npcId);
            if (npcGo == null)
                return false;
            npcName = npcGo.name ?? string.Empty;
            if (SidekickCompanion.FindForNpcBindingRoot(npcGo) == null)
                return false;
            var moveRoot = SidekickCompanion.ResolveLocomotionRoot(npcGo);
            if (moveRoot == null)
                return false;
            var follow = moveRoot.GetComponent<SidekickFollowHeroController>();
            if (follow == null)
                follow = moveRoot.AddComponent<SidekickFollowHeroController>();
            follow.StartFollowingHero();
            return true;
        }
    }
}
