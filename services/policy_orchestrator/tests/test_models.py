import pytest
from pydantic import ValidationError

from app.models import (
    DialogueTurnRequest,
    DialogueTurnResponse,
    NpcDeliberationRequest,
    NpcDeliberationResponse,
    PolicyEnvelope,
    PolicyError,
)


def test_successful_envelope_requires_payload() -> None:
    with pytest.raises(ValidationError):
        PolicyEnvelope(ok=True)


def test_failed_envelope_requires_error() -> None:
    with pytest.raises(ValidationError):
        PolicyEnvelope(ok=False)


def test_valid_success_envelope() -> None:
    env = PolicyEnvelope(ok=True, dialogue=DialogueTurnResponse(say="hi"))
    assert env.dialogue is not None


def test_valid_failure_envelope() -> None:
    env = PolicyEnvelope(ok=False, error=PolicyError(code="x", message="y"))
    assert env.error is not None


def test_response_serializes_camelcase() -> None:
    dumped = DialogueTurnResponse(say="hi", ack_year=True).model_dump(by_alias=True)
    assert dumped["ackYear"] is True
    assert "proposedActions" in dumped
    assert "rawAssistant" in dumped


def test_request_accepts_camelcase_and_rejects_unknown_fields() -> None:
    payload = {
        "model": "llama3.2",
        "npc": {"npcId": "n1"},
        "turn": {"latestPlayerLine": "hello"},
    }
    req = DialogueTurnRequest.model_validate(payload)
    assert req.npc.npc_id == "n1"
    assert req.turn.latest_player_line == "hello"

    with pytest.raises(ValidationError):
        DialogueTurnRequest.model_validate({**payload, "bogusField": 1})


def test_request_persona_autonomy_context_defaults_and_aliases() -> None:
    base = {
        "model": "llama3.2",
        "npc": {"npcId": "n1"},
        "turn": {"latestPlayerLine": "hello"},
    }
    req = DialogueTurnRequest.model_validate(base)
    assert req.npc.personality == ""
    assert req.npc.social_traits == {}
    assert req.npc.goals == []
    assert req.npc.capabilities == []
    assert req.npc.active_plan_context == ""
    assert req.npc.active_goals_context == ""

    with_context = {
        "model": "llama3.2",
        "npc": {
            "npcId": "n1",
            "personality": "calm",
            "socialTraits": {"helpfulness": "high"},
            "goals": ["protect village"],
            "capabilities": ["dialogue", "trade"],
            "activePlanContext": "Executing goto_location; remaining_steps=2.",
            "activeGoalsContext": "n1 goals: protect village",
        },
        "turn": {"latestPlayerLine": "hello"},
    }
    req2 = DialogueTurnRequest.model_validate(with_context)
    assert req2.npc.personality == "calm"
    assert req2.npc.social_traits == {"helpfulness": "high"}
    assert req2.npc.goals == ["protect village"]
    assert req2.npc.capabilities == ["dialogue", "trade"]
    assert req2.npc.active_plan_context.startswith("Executing goto_location")
    assert req2.npc.active_goals_context.startswith("n1 goals")


def test_deliberation_request_is_strict_inbound() -> None:
    payload = {
        "model": "llama3.2",
        "npcId": "npc_1",
        "goal": "do something useful",
        "targets": {"locationIds": ["square"]},
    }
    req = NpcDeliberationRequest.model_validate(payload)
    assert req.targets.location_ids == ["square"]
    with pytest.raises(ValidationError):
        NpcDeliberationRequest.model_validate({**payload, "unexpected": True})


def test_deliberation_response_can_fill_envelope_payload() -> None:
    env = PolicyEnvelope(
        ok=True,
        deliberation=NpcDeliberationResponse(
            request_id="r1",
            steps=[],
            used_fallback=True,
        ),
    )
    assert env.deliberation is not None
