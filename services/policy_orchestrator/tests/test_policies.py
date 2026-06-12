from app.models import DialogueTurnResponse, NpcType, ProposedAction
from app.policies import PolicyRegistry


def test_sidekick_policy_blocks_guide_actions() -> None:
    registry = PolicyRegistry()
    policy = registry.for_type(NpcType.SIDEKICK)
    response = DialogueTurnResponse(
        say="Sure.",
        proposed_actions=[
            ProposedAction(action_type="follow_hero", target_id="hero"),
            ProposedAction(action_type="refer_to_npc", target_id="npc_a"),
        ],
    )
    normalized = policy.normalize(response)
    assert len(normalized.proposed_actions) == 1
    assert normalized.proposed_actions[0].action_type == "follow_hero"


def test_ghoul_policy_forces_menace_and_zero_actions() -> None:
    registry = PolicyRegistry()
    policy = registry.for_type(NpcType.GHOUL)
    response = DialogueTurnResponse(
        say="i whisper doom",
        interaction_outcome="cooperate",
        proposed_actions=[ProposedAction(action_type="trade", target_id="book")],
        milestone_signals=["unlock:foo"],
        state_deltas={"x": "y"},
    )
    normalized = policy.normalize(response)
    assert normalized.say == "I WHISPER DOOM"
    assert normalized.interaction_outcome == "menace_flavor"
    assert normalized.proposed_actions == []
    assert normalized.milestone_signals == []
    assert normalized.state_deltas == {}
