from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Set

from .models import DialogueTurnResponse, NpcPlanStep, NpcType, ProposedAction


@dataclass(frozen=True)
class NpcPolicy:
    npc_type: NpcType
    allowed_actions: Set[str]
    forbid_state_deltas: bool = False
    force_all_caps: bool = False
    forced_interaction_outcome: str = ""

    def normalize(self, response: DialogueTurnResponse) -> DialogueTurnResponse:
        normalized = response.model_copy(deep=True)
        normalized.proposed_actions = self._filter_actions(normalized.proposed_actions)
        if self.forbid_state_deltas:
            normalized.state_deltas = {}
            normalized.milestone_signals = []
        if self.force_all_caps:
            normalized.say = normalized.say.upper()
        if self.forced_interaction_outcome:
            normalized.interaction_outcome = self.forced_interaction_outcome
        return normalized

    def _filter_actions(self, actions: List[ProposedAction]) -> List[ProposedAction]:
        out: List[ProposedAction] = []
        for action in actions:
            key = (action.action_type or "").strip().lower()
            if key in self.allowed_actions:
                out.append(action)
        return out


class PolicyRegistry:
    """NPC action allow-lists. Keep in sync with Unity ``NpcActionTypes`` in
    ``Assets/Scripts/Dialogue/NpcActionTypes.cs``."""

    def __init__(self) -> None:
        self._by_type: Dict[NpcType, NpcPolicy] = {
            NpcType.NORMAL: NpcPolicy(
                npc_type=NpcType.NORMAL,
                allowed_actions={
                    "move_to_location",
                    "move_to_npc",
                    "give_object",
                    "receive_object",
                    "trade",
                    "activate_object",
                    "find_object",
                    "inspect_location",
                    "refer_to_npc",
                },
            ),
            NpcType.SIDEKICK: NpcPolicy(
                npc_type=NpcType.SIDEKICK,
                allowed_actions={
                    "follow_hero",
                    "give_object",
                    "receive_object",
                    "trade",
                    "find_object",
                    "activate_object",
                },
            ),
            NpcType.GHOUL: NpcPolicy(
                npc_type=NpcType.GHOUL,
                allowed_actions=set(),
                forbid_state_deltas=True,
                force_all_caps=True,
                forced_interaction_outcome="menace_flavor",
            ),
        }

    def for_type(self, npc_type: NpcType) -> NpcPolicy:
        return self._by_type.get(npc_type, self._by_type[NpcType.NORMAL])


@dataclass(frozen=True)
class DeliberationPolicy:
    allowed_primitives: Set[str]

    def normalize_steps(
        self,
        steps: List[NpcPlanStep],
        *,
        self_npc_id: str,
        location_ids: Set[str],
        npc_ids: Set[str],
        work_ids: Set[str],
    ) -> List[NpcPlanStep]:
        out: List[NpcPlanStep] = []
        for step in steps:
            primitive = (step.primitive_type or "").strip().lower()
            target = (step.target_id or "").strip()
            if primitive not in self.allowed_primitives:
                continue
            if not self._target_is_valid(
                primitive=primitive,
                target=target,
                self_npc_id=self_npc_id,
                location_ids=location_ids,
                npc_ids=npc_ids,
                work_ids=work_ids,
            ):
                continue
            out.append(
                NpcPlanStep(
                    primitive_type=primitive,
                    target_id=target,
                    duration_seconds=max(0.0, float(step.duration_seconds)),
                    notes=step.notes,
                )
            )
        return out

    @staticmethod
    def _target_is_valid(
        *,
        primitive: str,
        target: str,
        self_npc_id: str,
        location_ids: Set[str],
        npc_ids: Set[str],
        work_ids: Set[str],
    ) -> bool:
        if primitive == "goto_location":
            return target in location_ids
        if primitive == "wait_at":
            return not target or target in location_ids
        if primitive == "goto_npc":
            return target in npc_ids and target != self_npc_id
        if primitive == "chat_with_npc":
            return target in npc_ids and target != self_npc_id
        if primitive == "perform_work":
            return target in work_ids
        if primitive == "idle_home":
            return not target
        return False
