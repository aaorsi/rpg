"""Tolerant parsing of model dialogue output.

Ported from the Unity-side ResponseValidator.cs so the sidecar is just as
forgiving of messy LLM output (markdown fences, key synonyms, nested objects,
string-token actions) while coercing values into the canonical, schema-valid
shape consumed by LlmDialogueOutput.
"""
from __future__ import annotations

import json
import re
from typing import Any, Dict, List, Optional

_SAY_REGEX = re.compile(r'"say"\s*:\s*"((?:\\.|[^"\\])*)"', re.DOTALL)

_VALID_OUTCOMES = {
    "reject",
    "partial",
    "cooperate",
    "counter_offer",
    "defer",
    "menace_flavor",
}
_VALID_SOCIAL_OUTCOME_TYPES = {
    "offer_task",
    "accept_task",
    "advice_given",
    "persuasion",
    "payment",
}
_VALID_STATE_DELTAS = {"down", "steady", "up"}

_GUIDE_ACTION_SYNONYMS = {
    "followhero": "follow_hero",
    "follow-hero": "follow_hero",
    "follow hero": "follow_hero",
    "escort_hero": "follow_hero",
    "accompany_hero": "follow_hero",
    "walk_with_hero": "follow_hero",
    "guide_to_location": "move_to_location",
    "guide_player_to_location": "move_to_location",
    "lead_to_location": "move_to_location",
    "lead_player_to_location": "move_to_location",
    "walk_to_location": "move_to_location",
    "navigate_to_location": "move_to_location",
    "escort_to_location": "move_to_location",
    "guide_to_npc": "refer_to_npc",
    "guide_player_to_npc": "refer_to_npc",
    "lead_to_npc": "refer_to_npc",
    "walk_to_npc": "refer_to_npc",
    "visit_npc": "refer_to_npc",
    "take_player_to_npc": "refer_to_npc",
}


def parse_dialogue_output(raw: str) -> Dict[str, Any]:
    """Return a camelCase dict ready for LlmDialogueOutput.model_validate.

    Raises ValueError when no usable ``say`` line can be recovered.
    """
    if not raw or not raw.strip():
        raise ValueError("Empty model response.")

    obj: Optional[Dict[str, Any]] = None
    try:
        obj = parse_json_object(raw)
    except ValueError:
        obj = None

    say = _extract_say(obj) if obj is not None else None
    if not say or not say.strip():
        say = _try_extract_say_with_regex(raw)
    if not say or not say.strip():
        raise ValueError("Dialogue response missing 'say'.")

    result: Dict[str, Any] = {"say": say.strip()}
    if obj is None:
        return result

    result["ackYear"] = _parse_ack_year(obj)
    result["interactionOutcome"] = _parse_interaction_outcome(obj)
    actions = _parse_proposed_actions(obj)
    _normalize_guide_action_types(actions)
    result["proposedNpcActions"] = actions
    result["socialOutcomes"] = _parse_social_outcomes(obj)
    result["stateDeltas"] = _parse_state_deltas(obj)
    result["milestoneSignals"] = _parse_milestones(obj)
    result["memoriesToAdd"] = _parse_memories_to_add(obj)
    return result


# --- JSON extraction --------------------------------------------------------


def normalize_json_payload(raw: str) -> str:
    """Strip markdown fences and leading prose, returning the JSON substring."""
    if not raw or not raw.strip():
        return raw
    t = raw.strip()
    if t.startswith("```"):
        first_nl = t.find("\n")
        if first_nl >= 0:
            t = t[first_nl + 1 :].lstrip()
        end_fence = t.rfind("```")
        if end_fence >= 0:
            t = t[:end_fence].strip()
    return t


def parse_json_object(raw: str) -> Dict[str, Any]:
    text = normalize_json_payload(raw)
    value = _decode_first_json_value(text)
    if isinstance(value, dict):
        return value
    if isinstance(value, list):
        for el in value:
            if isinstance(el, dict) and any(
                k in el for k in ("say", "proposedNpcActions", "actions", "dialogue")
            ):
                return el
        for el in value:
            if isinstance(el, dict):
                return el
    raise ValueError("No JSON object found in model response.")


def _decode_first_json_value(text: str) -> Any:
    decoder = json.JSONDecoder()
    for i, ch in enumerate(text):
        if ch in "{[":
            try:
                value, _ = decoder.raw_decode(text[i:])
                return value
            except json.JSONDecodeError:
                continue
    raise ValueError("No JSON value found in model response.")


# --- Field extractors -------------------------------------------------------


def _extract_say(obj: Dict[str, Any]) -> Optional[str]:
    direct = _first_str(obj, "say", "reply", "spoken", "line", "dialogue", "utterance", "npcLine")
    if direct:
        return direct

    for nest_name in ("dialogue", "response", "output", "npc", "message", "result", "turn"):
        nest = obj.get(nest_name)
        if not isinstance(nest, dict):
            continue
        inner = _first_str(nest, "say", "reply", "spoken", "line", "text", "content", "message")
        if inner:
            return inner
        content = nest.get("content")
        if isinstance(content, str) and content.strip():
            return content

    say_tok = obj.get("say")
    if isinstance(say_tok, list):
        parts = [str(el).strip() for el in say_tok if isinstance(el, str) and el.strip()]
        if parts:
            return " ".join(parts)
    if isinstance(say_tok, dict):
        nested = _first_str(say_tok, "text", "line", "content", "value")
        if nested:
            return nested
    return None


def _try_extract_say_with_regex(raw: str) -> Optional[str]:
    if not raw or not raw.strip():
        return None
    match = _SAY_REGEX.search(raw)
    if not match:
        return None
    captured = match.group(1)
    try:
        return json.loads('"' + captured + '"')
    except json.JSONDecodeError:
        return captured.replace('\\"', '"')


def _parse_ack_year(obj: Dict[str, Any]) -> bool:
    ack = obj.get("ackYear")
    if ack is None:
        ack = obj.get("ack_year")
    if isinstance(ack, bool):
        return ack
    if isinstance(ack, str):
        return ack.strip().lower() == "true"
    return False


def _parse_interaction_outcome(obj: Dict[str, Any]) -> str:
    raw = _first_str(obj, "interactionOutcome", "interaction_outcome", "outcome")
    if not raw:
        return "unspecified"
    normalized = raw.strip().lower()
    return normalized if normalized in _VALID_OUTCOMES else "unspecified"


def _parse_proposed_actions(obj: Dict[str, Any]) -> List[Dict[str, Any]]:
    target: List[Dict[str, Any]] = []
    arr = _first_list(
        obj,
        "proposedNpcActions",
        "npcActions",
        "actions",
        "proposedActions",
        "guidedActions",
        "navigationActions",
        "npcProposedActions",
    )
    if arr is None:
        for key in ("proposedNpcActions", "npcActions", "actions", "proposedActions", "guidedActions"):
            single = obj.get(key)
            if isinstance(single, dict):
                _append_one_action(single, target)
        for key in ("guide", "navigation", "guidedAction"):
            _append_one_action(obj.get(key), target)
        return target

    for el in arr:
        if isinstance(el, dict):
            _append_one_action(el, target)
        elif isinstance(el, str):
            _append_action_from_string_token(el, target)
    return target


def _append_one_action(ao: Any, target: List[Dict[str, Any]]) -> None:
    if not isinstance(ao, dict):
        return
    action_type = _first_str(ao, "actionType", "type", "action", "verb", "intent", "kind", "name")
    if not action_type:
        return
    quantity = 1.0
    qty = ao.get("quantity")
    if qty is None:
        qty = ao.get("qty")
    if qty is None:
        qty = ao.get("count")
    if qty is not None:
        try:
            quantity = float(qty)
        except (TypeError, ValueError):
            quantity = 1.0
    target.append(
        {
            "actionType": action_type.strip(),
            "targetId": _first_str(
                ao, "targetId", "target", "objectId", "locationId", "destinationId",
                "destination", "placeId", "place", "to", "where", "npcId", "location",
            )
            or "",
            "quantity": quantity,
            "notes": _first_str(ao, "notes", "reason", "detail", "description") or "",
        }
    )


def _append_action_from_string_token(token: str, target: List[Dict[str, Any]]) -> None:
    if not token or not token.strip():
        return
    t = token.strip()
    colon = t.find(":")
    if 0 < colon < len(t) - 1:
        action_type = t[:colon].strip()
        rest = t[colon + 1 :].strip()
        if action_type and rest:
            target.append({"actionType": action_type, "targetId": rest, "quantity": 1.0, "notes": ""})


def _normalize_guide_action_types(actions: List[Dict[str, Any]]) -> None:
    for action in actions:
        action_type = (action.get("actionType") or "").strip().lower()
        mapped = _GUIDE_ACTION_SYNONYMS.get(action_type)
        if mapped:
            action["actionType"] = mapped


def _parse_state_deltas(obj: Dict[str, Any]) -> Dict[str, str]:
    source = obj.get("stateDeltas")
    if not isinstance(source, dict):
        source = obj.get("state_deltas")
    if not isinstance(source, dict):
        return {}
    out: Dict[str, str] = {}
    for key, value in source.items():
        k = str(key or "").strip()
        v = str(value or "").strip().lower()
        if k and v in _VALID_STATE_DELTAS:
            out[k] = v
    return out


def _parse_social_outcomes(obj: Dict[str, Any]) -> List[Dict[str, Any]]:
    arr = _first_list(obj, "socialOutcomes", "social_outcomes", "socialSignals")
    if arr is None:
        single = obj.get("socialOutcome")
        arr = [single] if isinstance(single, dict) else []
    out: List[Dict[str, Any]] = []
    for el in arr:
        if not isinstance(el, dict):
            continue
        outcome_type_raw = _first_str(el, "outcomeType", "outcome_type", "type", "outcome", "event")
        if not outcome_type_raw:
            continue
        outcome_type = outcome_type_raw.strip().lower()
        if outcome_type not in _VALID_SOCIAL_OUTCOME_TYPES:
            continue
        out.append(
            {
                "outcomeType": outcome_type,
                "taskId": _first_str(el, "taskId", "task_id", "task", "questId", "quest_id") or "",
                "targetNpcId": _first_str(el, "targetNpcId", "target_npc_id", "targetId", "target", "npcId") or "",
                "amount": _try_parse_amount(
                    el.get("amount") or el.get("paymentAmount") or el.get("value") or el.get("price")
                ),
                "currency": _first_str(el, "currency", "currencyCode", "paymentCurrency") or "",
                "persuasion": _first_str(el, "persuasion", "persuasionMode", "method", "approach") or "",
                "adviceTopic": _first_str(el, "adviceTopic", "advice_topic", "topic", "subject") or "",
                "notes": _first_str(el, "notes", "reason", "detail", "description") or "",
            }
        )
    return out


def _parse_milestones(obj: Dict[str, Any]) -> List[str]:
    arr = _first_list(obj, "milestoneSignals", "milestones")
    if arr is None:
        return []
    out: List[str] = []
    for el in arr:
        s = str(el or "").strip()
        if s:
            out.append(s)
    return out


def _parse_memories_to_add(obj: Dict[str, Any]) -> List[Dict[str, str]]:
    arr = _first_list(obj, "memoriesToAdd", "memoryAdds", "newMemories")
    if arr is None:
        return []
    out: List[Dict[str, str]] = []
    for el in arr:
        if not isinstance(el, dict):
            continue
        summary = _first_str(el, "summary", "text", "note", "detail")
        if not summary:
            continue
        kind = _first_str(el, "kind", "type") or "fact"
        subject = _first_str(el, "subjectCharacterId", "subject", "characterId", "character") or "player"
        out.append(
            {
                "kind": kind.strip(),
                "summary": summary.strip(),
                "subjectCharacterId": subject.strip(),
            }
        )
    return out


# --- Small helpers ----------------------------------------------------------


def _first_str(obj: Dict[str, Any], *names: str) -> Optional[str]:
    for name in names:
        value = obj.get(name)
        if value is None:
            continue
        if isinstance(value, bool):
            continue
        if isinstance(value, (str, int, float)):
            s = str(value)
            if s.strip():
                return s
    return None


def _first_list(obj: Dict[str, Any], *names: str) -> Optional[List[Any]]:
    for name in names:
        value = obj.get(name)
        if isinstance(value, list):
            return value
    return None


def _try_parse_amount(value: Any) -> float:
    if value is None:
        return 0.0
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0
