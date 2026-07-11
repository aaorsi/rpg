from app.orchestrator import PolicyOrchestrator


class _DummyAdapter:
    pass


def test_filter_proposed_interactions_rejects_unknown_action_types():
    orchestrator = PolicyOrchestrator(_DummyAdapter())
    candidates = [
        {
            "id": "valid_trade_chat",
            "phases": {
                "start": [
                    {"actionType": "move_to_npc", "actorRole": "initiator", "targetRole": "target"},
                    {"actionType": "engage_dialogue", "actorRole": "initiator", "targetRole": "target"},
                ]
            },
        },
        {
            "id": "invalid_magic",
            "phases": {
                "start": [{"actionType": "cast_fireball", "actorRole": "initiator"}],
            },
        },
        {"phases": {"start": []}},
    ]

    filtered = orchestrator._filter_proposed_interactions(candidates)

    assert len(filtered) == 1
    assert filtered[0]["id"] == "valid_trade_chat"
