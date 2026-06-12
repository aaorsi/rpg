from typing import List

import pytest
from fastapi.testclient import TestClient

from app.main import app, get_orchestrator
from app.models import MessageDto
from app.orchestrator import PolicyOrchestrator


class FakeAdapter:
    def __init__(self, reply: str) -> None:
        self.reply = reply

    async def chat(
        self,
        base_url: str,
        model: str,
        messages: List[MessageDto],
        api_token=None,
        max_tokens: int = 512,
    ) -> str:
        return self.reply


def _client(reply: str) -> TestClient:
    orchestrator = PolicyOrchestrator(FakeAdapter(reply))
    app.dependency_overrides[get_orchestrator] = lambda: orchestrator
    return TestClient(app)


@pytest.fixture(autouse=True)
def _clear_overrides():
    yield
    app.dependency_overrides.clear()


def test_healthz() -> None:
    with TestClient(app) as client:
        assert client.get("/healthz").json() == {"ok": True}


def test_dialogue_turn_success_and_policy_filtering() -> None:
    reply = (
        '{"say": "Take this", "ackYear": true, '
        '"proposedNpcActions": [{"actionType": "give_object", "targetId": "apple"}, '
        '{"actionType": "cast_spell", "targetId": "fireball"}]}'
    )
    body = {
        "model": "llama3.2",
        "npc": {"npcId": "merchant", "npcType": "normal"},
        "turn": {"latestPlayerLine": "hello"},
    }
    with _client(reply) as client:
        data = client.post("/v1/dialogue/turn", json=body).json()
    assert data["ok"] is True
    assert data["dialogue"]["say"] == "Take this"
    # cast_spell is not in the normal allow-list and must be filtered out.
    action_types = [a["actionType"] for a in data["dialogue"]["proposedActions"]]
    assert action_types == ["give_object"]


def test_ghoul_policy_forces_caps_and_strips_actions() -> None:
    reply = '{"say": "i see you", "interactionOutcome": "cooperate", "proposedNpcActions": [{"actionType": "trade"}]}'
    body = {
        "model": "llama3.2",
        "npc": {"npcId": "ghoul_1", "npcType": "ghoul"},
        "turn": {"latestPlayerLine": "hi"},
    }
    with _client(reply) as client:
        dialogue = client.post("/v1/dialogue/turn", json=body).json()["dialogue"]
    assert dialogue["say"] == "I SEE YOU"
    assert dialogue["interactionOutcome"] == "menace_flavor"
    assert dialogue["proposedActions"] == []


def test_dialogue_turn_unparseable_returns_error_envelope() -> None:
    body = {
        "model": "llama3.2",
        "npc": {"npcId": "merchant"},
        "turn": {"latestPlayerLine": "hello"},
    }
    with _client("no json here") as client:
        data = client.post("/v1/dialogue/turn", json=body).json()
    assert data["ok"] is False
    assert data["error"]["code"] == "dialogue_failed"


def test_unsupported_schema_version_rejected() -> None:
    body = {
        "schemaVersion": 99,
        "model": "llama3.2",
        "npc": {"npcId": "merchant"},
        "turn": {"latestPlayerLine": "hello"},
    }
    with _client('{"say": "hi"}') as client:
        data = client.post("/v1/dialogue/turn", json=body).json()
    assert data["ok"] is False
    assert data["error"]["code"] == "unsupported_schema_version"
