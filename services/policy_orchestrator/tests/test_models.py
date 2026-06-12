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
