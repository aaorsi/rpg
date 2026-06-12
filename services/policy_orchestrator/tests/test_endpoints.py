from typing import List

import pytest
from fastapi.testclient import TestClient

from app.main import app, get_orchestrator
from app.models import MessageDto
from app.orchestrator import PolicyOrchestrator


class FakeAdapter:
    def __init__(self, reply: str) -> None:
        self.reply = reply
        self.last_messages: List[MessageDto] = []

    async def chat(
        self,
        base_url: str,
        model: str,
        messages: List[MessageDto],
        api_token=None,
        max_tokens: int = 512,
    ) -> str:
        self.last_messages = list(messages)
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


def test_dialogue_turn_prompt_includes_persona_and_autonomy_context() -> None:
    adapter = FakeAdapter('{"say":"hi"}')
    orchestrator = PolicyOrchestrator(adapter)
    app.dependency_overrides[get_orchestrator] = lambda: orchestrator
    body = {
        "model": "llama3.2",
        "npc": {
            "npcId": "merchant",
            "personality": "wary but fair",
            "socialTraits": {"helpfulness": "medium"},
            "goals": ["protect trade routes"],
            "capabilities": ["dialogue", "trade"],
            "activePlanContext": "Executing goto_location; remaining_steps=2.",
            "activeGoalsContext": "merchant goals: protect trade routes",
        },
        "turn": {"latestPlayerLine": "hello"},
    }
    with TestClient(app) as client:
        data = client.post("/v1/dialogue/turn", json=body).json()
    assert data["ok"] is True
    assert len(adapter.last_messages) >= 1
    system = adapter.last_messages[0].content
    assert "PERSONALITY: wary but fair" in system
    assert "SOCIAL_TRAITS: {'helpfulness': 'medium'}" in system
    assert "GOALS: ['protect trade routes']" in system
    assert "CAPABILITIES: ['dialogue', 'trade']" in system
    assert "ACTIVE_PLAN_CONTEXT: Executing goto_location; remaining_steps=2." in system
    assert "ACTIVE_GOALS_CONTEXT: merchant goals: protect trade routes" in system


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


def test_npc_persona_generate_success() -> None:
    reply = (
        '{"personas": [{"npcId": "merchant_1", "name": "Mira", "npcType": "normal", '
        '"occupation": "Trader", "personality": "Cautious but fair", '
        '"socialTraits": {"helpfulness": "medium", "skepticism": "high"}, '
        '"keyInformation": ["Runs the west market stall"], '
        '"goals": ["Protect trade routes"], '
        '"capabilities": ["barter", "appraise goods"], '
        '"followerRecruitmentRequirements": ["Return her missing ledger"]}]}'
    )
    body = {
        "model": "llama3.2",
        "npcs": [
            {
                "npcId": "merchant_1",
                "name": "Mira",
                "npcType": "normal",
                "archetypeId": "cautious_trader",
                "archetypeOccupation": "Trader",
                "archetypePersonality": "Pragmatic and careful",
                "archetypeSocialTraits": {"helpfulness": "medium", "skepticism": "high"},
                "goalHints": ["Preserve profit margins"],
            }
        ],
    }
    with _client(reply) as client:
        data = client.post("/v1/npc/persona/generate", json=body).json()
    assert data["ok"] is True
    assert len(data["persona"]["personas"]) == 1
    persona = data["persona"]["personas"][0]
    assert persona["npcId"] == "merchant_1"
    assert persona["occupation"] == "Trader"
    assert "Protect trade routes" in persona["goals"]


def test_npc_persona_generate_malformed_output_falls_back_to_defaults() -> None:
    body = {
        "model": "llama3.2",
        "npcs": [
            {
                "npcId": "farmer_1",
                "name": "Ira",
                "npcType": "normal",
                "archetypeId": "loyal_farmer",
                "archetypeOccupation": "Farmer",
                "archetypePersonality": "Protective and disciplined",
                "archetypeSocialTraits": {"helpfulness": "high"},
                "keyInformationHints": ["Owns the northern wheat fields"],
                "goalHints": ["Keep the family safe"],
                "capabilityHints": ["farming"],
            },
            {
                "npcId": "guide_1",
                "name": "Sora",
                "npcType": "sidekick",
                "archetypeId": "scout",
                "followerRecruitmentHints": ["Complete the beacon route"],
            },
        ],
    }
    with _client("<<not json>>") as client:
        data = client.post("/v1/npc/persona/generate", json=body).json()
    assert data["ok"] is True
    personas = data["persona"]["personas"]
    assert [p["npcId"] for p in personas] == ["farmer_1", "guide_1"]
    assert personas[0]["occupation"] == "Farmer"
    assert personas[0]["personality"] == "Protective and disciplined"
    assert personas[0]["keyInformation"] == ["Owns the northern wheat fields"]
    assert personas[1]["occupation"] == "Villager"
    assert personas[1]["followerRecruitmentRequirements"] == ["Complete the beacon route"]


def test_npc_deliberate_success_with_vocab_normalization_and_target_filtering() -> None:
    reply = (
        '{"planSteps":[{"primitiveType":"move_to_location","targetId":"smithy","durationSeconds":0.5},'
        '{"primitiveType":"goto_npc","targetId":"blacksmith"},'
        '{"primitiveType":"chat_with_npc","targetId":"unknown_npc"},'
        '{"primitiveType":"idle_home","targetId":"not_allowed"}]}'
    )
    body = {
        "model": "llama3.2",
        "npcId": "villager_1",
        "goal": "check in with blacksmith then wrap up",
        "targets": {
            "locationIds": ["smithy", "market"],
            "npcIds": ["blacksmith"],
            "workIds": ["farming"],
        },
    }
    with _client(reply) as client:
        data = client.post("/v1/npc/deliberate", json=body).json()
    assert data["ok"] is True
    deliberation = data["deliberation"]
    assert deliberation["usedFallback"] is False
    assert [step["primitiveType"] for step in deliberation["steps"]] == ["goto_location", "goto_npc"]


def test_npc_deliberate_invalid_output_uses_deterministic_fallback() -> None:
    body = {
        "model": "llama3.2",
        "npcId": "worker_1",
        "goal": "do my shift",
        "targets": {
            "locationIds": ["plaza"],
            "npcIds": ["merchant_1"],
            "workIds": ["mill"],
        },
    }
    with _client("<<no json>>") as client:
        data = client.post("/v1/npc/deliberate", json=body).json()
    assert data["ok"] is True
    deliberation = data["deliberation"]
    assert deliberation["usedFallback"] is True
    assert [step["primitiveType"] for step in deliberation["steps"]] == ["perform_work", "idle_home"]
    assert deliberation["steps"][0]["targetId"] == "mill"
