using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Rpg.Dialogue
{
    public static class ResponseValidator
    {
        static readonly HashSet<string> ValidSocialOutcomeTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "offer_task",
            "accept_task",
            "advice_given",
            "persuasion",
            "payment"
        };

        static readonly Regex SayStringRegex = new Regex(
            @"""say""\s*:\s*""((?:\\.|[^""\\])*)""",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        /// <summary>
        /// Models often wrap JSON in markdown fences or add preamble; extract a single JSON object for parsing.
        /// Uses <see cref="JsonTextReader"/> so strings containing <c>}</c> do not truncate the payload (unlike naive brace slicing).
        /// </summary>
        public static string NormalizeJsonPayload(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return raw;

            var t = raw.Trim();

            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNl = t.IndexOf('\n');
                if (firstNl >= 0)
                    t = t.Substring(firstNl + 1).TrimStart();
                var endFence = t.LastIndexOf("```", StringComparison.Ordinal);
                if (endFence >= 0)
                    t = t.Substring(0, endFence).Trim();
            }

            var start = t.IndexOf('{');
            if (start < 0)
                return t.Trim();
            var slice = t.Substring(start);
            try
            {
                using var sr = new StringReader(slice);
                using var jr = new JsonTextReader(sr) { SupportMultipleContent = true };
                if (!jr.Read())
                    return slice.Trim();
                if (jr.TokenType == JsonToken.StartObject)
                {
                    var jo = JObject.Load(jr);
                    return jo.ToString(Formatting.None);
                }

                if (jr.TokenType == JsonToken.StartArray)
                {
                    var arr = JArray.Load(jr);
                    foreach (var el in arr)
                    {
                        if (el is not JObject wrapped)
                            continue;
                        if (wrapped["say"] != null || wrapped["proposedNpcActions"] != null || wrapped["actions"] != null
                            || wrapped["dialogue"] != null)
                            return wrapped.ToString(Formatting.None);
                    }

                    foreach (var el in arr)
                    {
                        if (el is JObject wrapped)
                            return wrapped.ToString(Formatting.None);
                    }
                }
            }
            catch
            {
                // fall through
            }

            return slice.Trim();
        }

        public static bool TryParseModelJson(string raw, out string say, out bool ackYear)
        {
            if (!TryParseModelResponse(raw, out var payload))
            {
                say = null;
                ackYear = false;
                return false;
            }

            say = payload.Say;
            ackYear = payload.AckYear;
            return true;
        }

        public static bool TryParseModelResponse(string raw, out AssistantModelPayload payload)
        {
            payload = new AssistantModelPayload();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var normalized = NormalizeJsonPayload(raw);
            JObject obj = null;
            try
            {
                obj = JObject.Parse(normalized);
            }
            catch
            {
                obj = null;
            }

            var say = obj != null ? ExtractSayField(obj) : null;
            if (string.IsNullOrWhiteSpace(say))
                say = TryExtractSayWithRegex(raw) ?? TryExtractSayWithRegex(normalized);
            if (string.IsNullOrWhiteSpace(say))
                return false;

            payload.Say = say.Trim();
            if (obj == null)
                return true;

            try
            {
                PopulatePayloadFromJsonObject(obj, payload);
                return true;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Maps a Python sidecar dialogue DTO into <see cref="AssistantModelPayload"/> using the same field parsers as the fallback path.
        /// </summary>
        public static AssistantModelPayload BuildPayloadFromDialogueDto(PythonDialogueTurnResponseDto dto)
        {
            var payload = new AssistantModelPayload
            {
                Say = dto != null ? dto.say ?? string.Empty : string.Empty,
                AckYear = dto != null && dto.ackYear
            };
            if (dto == null)
                return payload;

            if (dto.proposedActions != null)
            {
                foreach (var action in dto.proposedActions)
                {
                    if (action == null)
                        continue;
                    payload.ProposedActions.Add(new NpcProposedAction
                    {
                        ActionType = action.ActionType,
                        TargetId = action.TargetId,
                        Quantity = action.Quantity,
                        Notes = action.Notes
                    });
                }

                NormalizeGuideActionTypes(payload.ProposedActions);
            }

            var obj = JObject.FromObject(dto);
            ParseMemoriesToAdd(obj, payload.MemoryAdds);
            ParseInteractionOutcome(obj, payload);
            ParseStateDeltas(obj, payload.StateDeltas);
            ParseMilestones(obj, payload.MilestoneSignals);
            ParseSocialOutcomes(obj, payload.SocialOutcomes);
            return payload;
        }

        /// <summary>
        /// Maps a Python sidecar summary DTO into <see cref="NpcConversationSummary"/>.
        /// </summary>
        public static bool TryBuildSummaryFromSidecarDto(PythonSummaryResponseDto dto, out NpcConversationSummary summary)
        {
            summary = null;
            if (dto == null || string.IsNullOrWhiteSpace(dto.summary))
                return false;

            summary = new NpcConversationSummary
            {
                summary = dto.summary,
                learnedFacts = dto.learnedFacts ?? new List<string>(),
                openThreads = dto.openThreads ?? new List<string>(),
                relationshipShift = string.IsNullOrWhiteSpace(dto.relationshipShift)
                    ? "neutral"
                    : dto.relationshipShift,
                createdUtc = DateTime.UtcNow.ToString("o")
            };
            return true;
        }

        /// <summary>
        /// Parses and validates narrative canon JSON from a Python sidecar DTO.
        /// </summary>
        public static bool TryBuildNarrativeCanonFromSidecarDto(
            PythonNarrativeResponseDto dto,
            out NarrativeSessionCanon canon,
            out string parseError)
        {
            canon = null;
            parseError = null;
            if (dto == null || string.IsNullOrWhiteSpace(dto.canonJson))
                return false;

            try
            {
                var parsed = JsonConvert.DeserializeObject<NarrativeSessionCanon>(dto.canonJson);
                if (parsed != null && NarrativeGenerationService.ValidateAndRepair(parsed))
                {
                    canon = parsed;
                    return true;
                }
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            return false;
        }

        static void PopulatePayloadFromJsonObject(JObject obj, AssistantModelPayload payload)
        {
            var ack = obj["ackYear"] ?? obj["ack_year"];
            if (ack != null && ack.Type != JTokenType.Null)
            {
                if (ack.Type == JTokenType.Boolean)
                    payload.AckYear = ack.Value<bool>();
                else if (bool.TryParse(ack.ToString(), out var parsedAck))
                    payload.AckYear = parsedAck;
            }

            ParseMemoriesToAdd(obj, payload.MemoryAdds);
            ParseInteractionOutcome(obj, payload);
            ParseProposedActions(obj, payload.ProposedActions);
            NormalizeGuideActionTypes(payload.ProposedActions);
            ParseSocialOutcomes(obj, payload.SocialOutcomes);
            ParseStateDeltas(obj, payload.StateDeltas);
            ParseMilestones(obj, payload.MilestoneSignals);
        }

        /// <summary>Maps model synonyms (e.g. guide_to_location) to executor-supported action types.</summary>
        public static void NormalizeGuideActionTypes(List<NpcProposedAction> actions)
        {
            if (actions == null)
                return;
            for (var i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                if (a == null || string.IsNullOrWhiteSpace(a.ActionType))
                    continue;
                var t = a.ActionType.Trim().ToLowerInvariant();
                switch (t)
                {
                    case "followhero":
                    case "follow-hero":
                    case "follow hero":
                    case "escort_hero":
                    case "accompany_hero":
                    case "walk_with_hero":
                        a.ActionType = "follow_hero";
                        break;
                    case "guide_to_location":
                    case "guide_player_to_location":
                    case "lead_to_location":
                    case "lead_player_to_location":
                    case "walk_to_location":
                    case "navigate_to_location":
                    case "escort_to_location":
                        a.ActionType = "move_to_location";
                        break;
                    case "guide_to_npc":
                    case "guide_player_to_npc":
                    case "lead_to_npc":
                    case "walk_to_npc":
                    case "visit_npc":
                    case "take_player_to_npc":
                        a.ActionType = "refer_to_npc";
                        break;
                }
            }
        }

        static string TryExtractSayWithRegex(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var m = SayStringRegex.Match(raw);
            if (!m.Success || m.Groups.Count < 2)
                return null;
            try
            {
                return Regex.Unescape(m.Groups[1].Value);
            }
            catch
            {
                return m.Groups[1].Value.Replace("\\\"", "\"");
            }
        }

        static string ExtractSayField(JObject obj)
        {
            var direct = FirstNonEmptyString(obj, "say", "reply", "spoken", "line", "dialogue", "utterance", "npcLine");
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            foreach (var nestName in new[] { "dialogue", "response", "output", "npc", "message", "result", "turn" })
            {
                if (obj[nestName] is not JObject nest)
                    continue;
                var inner = FirstNonEmptyString(nest, "say", "reply", "spoken", "line", "text", "content", "message");
                if (!string.IsNullOrWhiteSpace(inner))
                    return inner;
                if (nest["content"] is JValue cv && cv.Type == JTokenType.String)
                {
                    var s = cv.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }

            var sayTok = obj["say"];
            if (sayTok?.Type == JTokenType.Array)
            {
                var sb = new StringBuilder();
                foreach (var el in (JArray)sayTok)
                {
                    if (el.Type != JTokenType.String)
                        continue;
                    var part = el.ToString();
                    if (string.IsNullOrWhiteSpace(part))
                        continue;
                    if (sb.Length > 0)
                        sb.Append(' ');
                    sb.Append(part.Trim());
                }

                if (sb.Length > 0)
                    return sb.ToString();
            }

            if (sayTok is JObject sayObj)
            {
                var t = FirstNonEmptyString(sayObj, "text", "line", "content", "value");
                if (!string.IsNullOrWhiteSpace(t))
                    return t;
            }

            return null;
        }

        static void ParseMemoriesToAdd(JObject obj, List<NpcMemoryCandidate> target)
        {
            var arr = obj["memoriesToAdd"] as JArray
                ?? obj["memoryAdds"] as JArray
                ?? obj["newMemories"] as JArray;
            if (arr == null)
                return;

            foreach (var el in arr)
            {
                if (el is not JObject mo)
                    continue;
                var summary = FirstNonEmptyString(mo, "summary", "text", "note", "detail");
                if (string.IsNullOrWhiteSpace(summary))
                    continue;
                var kindRaw = FirstNonEmptyString(mo, "kind", "type");
                var kind = string.IsNullOrWhiteSpace(kindRaw) ? "fact" : kindRaw.Trim();
                var subject = FirstNonEmptyString(mo, "subjectCharacterId", "subject", "characterId", "character");
                if (string.IsNullOrWhiteSpace(subject))
                    subject = "player";
                target.Add(new NpcMemoryCandidate(kind, summary.Trim(), subject.Trim()));
            }
        }

        static void ParseInteractionOutcome(JObject obj, AssistantModelPayload payload)
        {
            var raw = FirstNonEmptyString(obj, "interactionOutcome", "interaction_outcome", "outcome");
            if (string.IsNullOrWhiteSpace(raw))
                return;
            payload.InteractionOutcome = raw.Trim().ToLowerInvariant();
        }

        static void ParseProposedActions(JObject obj, List<NpcProposedAction> target)
        {
            var arr = obj["proposedNpcActions"] as JArray
                      ?? obj["npcActions"] as JArray
                      ?? obj["actions"] as JArray
                      ?? obj["proposedActions"] as JArray
                      ?? obj["guidedActions"] as JArray
                      ?? obj["navigationActions"] as JArray
                      ?? obj["npcProposedActions"] as JArray;
            if (arr == null)
            {
                foreach (var key in new[] { "proposedNpcActions", "npcActions", "actions", "proposedActions", "guidedActions" })
                {
                    if (obj[key] is JObject single)
                        AppendOneAction(single, target);
                }

                TryAppendSingleActionObject(obj["guide"] as JObject, target);
                TryAppendSingleActionObject(obj["navigation"] as JObject, target);
                TryAppendSingleActionObject(obj["guidedAction"] as JObject, target);
                return;
            }

            foreach (var el in arr)
            {
                if (el == null || el.Type == JTokenType.Null)
                    continue;
                if (el is JObject ao)
                    AppendOneAction(ao, target);
                else if (el.Type == JTokenType.String)
                    TryAppendActionFromStringToken(el.ToString(), target);
            }
        }

        static void TryAppendSingleActionObject(JObject ao, List<NpcProposedAction> target)
        {
            if (ao == null)
                return;
            AppendOneAction(ao, target);
        }

        static void AppendOneAction(JObject ao, List<NpcProposedAction> target)
        {
            var actionType = FirstNonEmptyString(ao, "actionType", "type", "action", "verb", "intent", "kind", "name");
            if (string.IsNullOrWhiteSpace(actionType))
                return;
            var action = new NpcProposedAction
            {
                ActionType = actionType.Trim(),
                TargetId = FirstNonEmptyString(ao, "targetId", "target", "objectId", "locationId", "destinationId",
                    "destination", "placeId", "place", "to", "where", "npcId", "location"),
                Notes = FirstNonEmptyString(ao, "notes", "reason", "detail", "description")
            };
            var qty = ao["quantity"] ?? ao["qty"] ?? ao["count"];
            if (qty != null && qty.Type != JTokenType.Null && float.TryParse(qty.ToString(), out var parsed))
                action.Quantity = parsed;
            target.Add(action);
        }

        static void TryAppendActionFromStringToken(string token, List<NpcProposedAction> target)
        {
            if (string.IsNullOrWhiteSpace(token))
                return;
            var t = token.Trim();
            var colon = t.IndexOf(':');
            if (colon > 0 && colon < t.Length - 1)
            {
                var type = t.Substring(0, colon).Trim();
                var rest = t.Substring(colon + 1).Trim();
                if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(rest))
                    target.Add(new NpcProposedAction { ActionType = type, TargetId = rest, Quantity = 1f });
            }
        }

        static void ParseStateDeltas(JObject obj, Dictionary<string, string> target)
        {
            var o = obj["stateDeltas"] as JObject ?? obj["state_deltas"] as JObject;
            if (o == null)
                return;
            foreach (var prop in o.Properties())
            {
                var key = prop.Name?.Trim();
                var val = prop.Value?.ToString()?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(val))
                    continue;
                target[key] = val;
            }
        }

        static void ParseSocialOutcomes(JObject obj, List<NpcSocialOutcome> target)
        {
            var arr = obj["socialOutcomes"] as JArray ?? obj["social_outcomes"] as JArray;
            if (arr == null)
            {
                if (obj["socialOutcome"] is JObject single)
                    TryAppendSocialOutcome(single, target);
                return;
            }

            foreach (var el in arr)
            {
                if (el is JObject so)
                    TryAppendSocialOutcome(so, target);
            }
        }

        static void TryAppendSocialOutcome(JObject so, List<NpcSocialOutcome> target)
        {
            var type = NormalizeSocialOutcomeType(
                FirstNonEmptyString(so, "outcomeType", "outcome_type", "type", "outcome", "event"));
            if (string.IsNullOrWhiteSpace(type))
                return;

            target.Add(new NpcSocialOutcome
            {
                OutcomeType = type,
                TaskId = FirstNonEmptyString(so, "taskId", "task_id", "task", "questId", "quest_id"),
                TargetNpcId = FirstNonEmptyString(so, "targetNpcId", "target_npc_id", "targetId", "target", "npcId"),
                Amount = ParseAmount(so["amount"] ?? so["paymentAmount"] ?? so["value"] ?? so["price"]),
                Currency = FirstNonEmptyString(so, "currency", "currencyCode", "paymentCurrency"),
                Persuasion = FirstNonEmptyString(so, "persuasion", "persuasionMode", "method", "approach"),
                AdviceTopic = FirstNonEmptyString(so, "adviceTopic", "advice_topic", "topic", "subject"),
                Notes = FirstNonEmptyString(so, "notes", "reason", "detail", "description")
            });
        }

        static void ParseMilestones(JObject obj, List<string> target)
        {
            var arr = obj["milestoneSignals"] as JArray ?? obj["milestones"] as JArray;
            if (arr == null)
                return;
            foreach (var el in arr)
            {
                var s = el?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    target.Add(s.Trim());
            }
        }

        static string NormalizeSocialOutcomeType(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var normalized = raw.Trim().ToLowerInvariant();
            return ValidSocialOutcomeTypes.Contains(normalized) ? normalized : null;
        }

        static float ParseAmount(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return 0f;
            if (float.TryParse(token.ToString(), out var parsed))
                return parsed;
            return 0f;
        }

        static string FirstNonEmptyString(JObject obj, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var t = obj[name];
                if (t == null || t.Type == JTokenType.Null)
                    continue;
                if (t.Type == JTokenType.String || t.Type == JTokenType.Integer || t.Type == JTokenType.Float
                    || t.Type == JTokenType.Date)
                {
                    var s = t.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }

            return null;
        }
    }
}
