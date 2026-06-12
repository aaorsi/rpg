import pytest

from app.dialogue_parsing import parse_dialogue_output
from app.models import LlmDialogueOutput


def _validate(raw: str) -> LlmDialogueOutput:
    return LlmDialogueOutput.model_validate(parse_dialogue_output(raw))


def test_markdown_fenced_json_is_unwrapped() -> None:
    raw = '```json\n{"say": "Hello there", "ackYear": true}\n```'
    out = _validate(raw)
    assert out.say == "Hello there"
    assert out.ack_year is True


def test_nested_dialogue_object_say_is_extracted() -> None:
    raw = '{"dialogue": {"say": "Nested line"}}'
    out = _validate(raw)
    assert out.say == "Nested line"


def test_say_array_is_joined() -> None:
    raw = '{"say": ["part one", "part two"]}'
    out = _validate(raw)
    assert out.say == "part one part two"


def test_string_token_action_is_parsed() -> None:
    raw = '{"say": "ok", "proposedNpcActions": ["give_object:apple"]}'
    out = _validate(raw)
    assert len(out.proposed_npc_actions) == 1
    assert out.proposed_npc_actions[0].action_type == "give_object"
    assert out.proposed_npc_actions[0].target_id == "apple"


def test_guide_synonyms_are_normalized() -> None:
    raw = '{"say": "follow me", "proposedNpcActions": [{"actionType": "guide_to_location", "targetId": "well"}]}'
    out = _validate(raw)
    assert out.proposed_npc_actions[0].action_type == "move_to_location"


def test_action_field_synonyms_are_recognized() -> None:
    raw = '{"say": "ok", "actions": [{"type": "trade", "target": "book", "qty": 2}]}'
    out = _validate(raw)
    action = out.proposed_npc_actions[0]
    assert action.action_type == "trade"
    assert action.target_id == "book"
    assert action.quantity == 2.0


def test_invalid_state_delta_values_are_dropped() -> None:
    raw = '{"say": "ok", "stateDeltas": {"trust": "up", "fear": "exploding"}}'
    out = _validate(raw)
    assert out.state_deltas == {"trust": "up"}


def test_unknown_interaction_outcome_falls_back_to_unspecified() -> None:
    raw = '{"say": "ok", "interactionOutcome": "make_friends"}'
    out = _validate(raw)
    assert out.interaction_outcome == "unspecified"


def test_social_outcomes_parse_supported_types_and_filter_invalid_entries() -> None:
    raw = (
        '{"say":"ok","socialOutcomes":['
        '{"outcomeType":"offer_task","taskId":"task_1"},'
        '{"outcomeType":"payment","amount":"7","currency":"gold"},'
        '{"outcomeType":"unknown_type","notes":"skip me"},'
        '{"notes":"missing type should be ignored"}'
        "]} "
    )
    out = _validate(raw)
    assert len(out.social_outcomes) == 2
    assert out.social_outcomes[0].outcome_type == "offer_task"
    assert out.social_outcomes[0].task_id == "task_1"
    assert out.social_outcomes[1].outcome_type == "payment"
    assert out.social_outcomes[1].amount == 7
    assert out.social_outcomes[1].currency == "gold"


def test_memories_use_canonical_keys_and_defaults() -> None:
    raw = '{"say": "ok", "memoriesToAdd": [{"text": "player likes apples"}]}'
    out = _validate(raw)
    mem = out.memories_to_add[0]
    assert mem == {"kind": "fact", "summary": "player likes apples", "subjectCharacterId": "player"}


def test_say_recovered_via_regex_when_json_is_broken() -> None:
    raw = 'garbage {"say": "still here", oops not json'
    out = parse_dialogue_output(raw)
    assert out["say"] == "still here"


def test_missing_say_raises() -> None:
    with pytest.raises(ValueError):
        parse_dialogue_output('{"ackYear": true}')


def test_empty_input_raises() -> None:
    with pytest.raises(ValueError):
        parse_dialogue_output("   ")
