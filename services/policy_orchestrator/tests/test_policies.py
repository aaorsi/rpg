from app.models import DialogueTurnResponse, NpcType, ProposedAction
from app.models import NpcPlanStep
from app.policies import DeliberationPolicy, PolicyRegistry


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


def test_deliberation_policy_enforces_vocab_and_targets() -> None:
    policy = DeliberationPolicy(
        allowed_primitives={
            "goto_location",
            "goto_npc",
            "wait_at",
            "perform_work",
            "chat_with_npc",
            "idle_home",
        }
    )
    steps = [
        NpcPlanStep(primitive_type="goto_location", target_id="town_square"),
        NpcPlanStep(primitive_type="goto_npc", target_id="self_npc"),
        NpcPlanStep(primitive_type="chat_with_npc", target_id="merchant_1"),
        NpcPlanStep(primitive_type="perform_work", target_id="forge"),
        NpcPlanStep(primitive_type="idle_home", target_id="should_be_empty"),
    ]
    normalized = policy.normalize_steps(
        steps,
        self_npc_id="self_npc",
        location_ids={"town_square"},
        npc_ids={"self_npc", "merchant_1"},
        work_ids={"forge"},
    )
    assert [step.primitive_type for step in normalized] == [
        "goto_location",
        "chat_with_npc",
        "perform_work",
    ]
