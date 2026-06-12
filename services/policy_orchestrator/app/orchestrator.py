from __future__ import annotations

import json
import logging
from typing import List, Optional

from pydantic import ValidationError

from .dialogue_parsing import parse_dialogue_output, parse_json_object
from .models import (
    SCHEMA_VERSION,
    ConversationSummaryRequest,
    ConversationSummaryResponse,
    DialogueTurnRequest,
    DialogueTurnResponse,
    LlmDialogueOutput,
    MessageDto,
    NarrativeGenerationRequest,
    NarrativeGenerationResponse,
    PolicyEnvelope,
    PolicyError,
    SessionNarrativeCanon,
)
from .ollama_adapter import OllamaAdapter
from .policies import PolicyRegistry

logger = logging.getLogger("policy_orchestrator")

_DEFAULT_PROVIDER = "http://127.0.0.1:11434"


class PolicyOrchestrator:
    def __init__(self, adapter: OllamaAdapter) -> None:
        self._adapter = adapter
        self._registry = PolicyRegistry()

    async def run_dialogue_turn(self, request: DialogueTurnRequest) -> PolicyEnvelope:
        rid = request.request_id
        version_error = self._reject_unsupported_version(request.schema_version, rid)
        if version_error is not None:
            return version_error
        try:
            messages = self._build_turn_messages(request)
            raw = await self._adapter.chat(
                base_url=request.provider_base_url or _DEFAULT_PROVIDER,
                model=request.model,
                messages=messages,
                api_token=request.api_token,
            )
            llm_output = LlmDialogueOutput.model_validate(parse_dialogue_output(raw))
            response = self._to_dialogue_response(llm_output, raw, rid)
            policy = self._registry.for_type(request.npc.npc_type)
            response = policy.normalize(response)
            return PolicyEnvelope(ok=True, dialogue=response)
        except Exception as ex:
            return self._fail("dialogue_failed", rid, ex)

    async def run_summary(self, request: ConversationSummaryRequest) -> PolicyEnvelope:
        rid = request.request_id
        version_error = self._reject_unsupported_version(request.schema_version, rid)
        if version_error is not None:
            return version_error
        try:
            system = (
                "Summarize dialogue. Return ONLY JSON object: "
                '{"summary":"...","learnedFacts":[...],"openThreads":[...],"relationshipShift":"negative|neutral|positive"}'
            )
            user = (
                f"npcId={request.npc_id}\n"
                f"transcript={json.dumps([m.model_dump(by_alias=True) for m in request.turns])}"
            )
            raw = await self._adapter.chat(
                base_url=request.provider_base_url or _DEFAULT_PROVIDER,
                model=request.model,
                messages=[
                    MessageDto(role="system", content=system),
                    MessageDto(role="user", content=user),
                ],
                api_token=request.api_token,
            )
            data = parse_json_object(raw)
            summary_text = str(data.get("summary") or "").strip()
            if not summary_text:
                raise ValueError("Summary response missing summary.")
            shift = str(data.get("relationshipShift") or data.get("relationship_shift") or "neutral").strip().lower()
            if shift not in ("negative", "neutral", "positive"):
                shift = "neutral"
            response = ConversationSummaryResponse(
                request_id=rid,
                summary=summary_text,
                learned_facts=_list_of_strings(data.get("learnedFacts") or data.get("learned_facts")),
                open_threads=_list_of_strings(data.get("openThreads") or data.get("open_threads")),
                relationship_shift=shift,
                raw_assistant=raw,
            )
            return PolicyEnvelope(ok=True, summary=response)
        except Exception as ex:
            return self._fail("summary_failed", rid, ex)

    async def run_narrative_generation(self, request: NarrativeGenerationRequest) -> PolicyEnvelope:
        rid = request.request_id
        version_error = self._reject_unsupported_version(request.schema_version, rid)
        if version_error is not None:
            return version_error
        try:
            system = "You are a game narrative generator. Return ONLY JSON matching the provided schema scaffold."
            user = (
                "Generate coherent narrative from this scaffold and preserve route counts >= 2.\n"
                + request.fallback_canon_json
            )
            raw = await self._adapter.chat(
                base_url=request.provider_base_url or _DEFAULT_PROVIDER,
                model=request.model,
                messages=[
                    MessageDto(role="system", content=system),
                    MessageDto(role="user", content=user),
                ],
                api_token=request.api_token,
            )
            obj = parse_json_object(raw)
            obj["seed"] = request.seed
            invalid = self._narrative_route_violation(obj)
            if invalid is not None:
                return PolicyEnvelope(
                    ok=False,
                    error=PolicyError(code="narrative_invalid", message=invalid),
                )
            response = NarrativeGenerationResponse(
                request_id=rid,
                canon_json=json.dumps(obj, separators=(",", ":")),
                raw_assistant=raw,
            )
            return PolicyEnvelope(ok=True, narrative=response)
        except Exception as ex:
            return self._fail("narrative_failed", rid, ex)

    # --- Helpers ------------------------------------------------------------

    def _to_dialogue_response(
        self, llm: LlmDialogueOutput, raw: str, request_id: str
    ) -> DialogueTurnResponse:
        return DialogueTurnResponse(
            request_id=request_id,
            say=llm.say,
            ack_year=llm.ack_year,
            interaction_outcome=llm.interaction_outcome,
            proposed_actions=llm.proposed_npc_actions,
            milestone_signals=llm.milestone_signals,
            state_deltas=dict(llm.state_deltas),
            memories_to_add=llm.memories_to_add,
            raw_assistant=raw,
        )

    @staticmethod
    def _narrative_route_violation(canon: dict) -> Optional[str]:
        try:
            parsed = SessionNarrativeCanon.model_validate(canon)
        except ValidationError as ex:
            return f"Narrative failed schema validation: {ex.error_count()} error(s)."
        offenders = [m for m, count in parsed.routes_by_milestone.items() if count < 2]
        if offenders:
            return f"Milestones with fewer than 2 routes: {sorted(offenders)}."
        return None

    def _reject_unsupported_version(self, schema_version: int, request_id: str) -> Optional[PolicyEnvelope]:
        if schema_version == SCHEMA_VERSION:
            return None
        message = f"Unsupported schemaVersion {schema_version}; expected {SCHEMA_VERSION}."
        logger.warning("[%s] %s", request_id or "-", message)
        return PolicyEnvelope(
            ok=False,
            error=PolicyError(code="unsupported_schema_version", message=message),
        )

    @staticmethod
    def _fail(code: str, request_id: str, ex: Exception) -> PolicyEnvelope:
        logger.exception("[%s] %s: %s", request_id or "-", code, ex)
        # Do not leak internal exception details to the client.
        return PolicyEnvelope(ok=False, error=PolicyError(code=code, message=code))

    def _build_turn_messages(self, request: DialogueTurnRequest) -> List[MessageDto]:
        npc = request.npc
        turn = request.turn
        policy = self._registry.for_type(npc.npc_type)
        runtime_rules = [
            f"NPC_TYPE: {npc.npc_type.value}",
            f"ALLOWED_ACTIONS: {sorted(list(policy.allowed_actions))}",
        ]
        if policy.forbid_state_deltas:
            runtime_rules.append("STATE_DELTAS_DISABLED: true")
        if policy.force_all_caps:
            runtime_rules.append("SPEECH_ALL_CAPS: true")
        if policy.forced_interaction_outcome:
            runtime_rules.append(f"FORCED_OUTCOME: {policy.forced_interaction_outcome}")

        system = (
            "You are an in-world character. Return ONLY JSON with keys: "
            'say, ackYear, interactionOutcome, proposedNpcActions, milestoneSignals, stateDeltas, memoriesToAdd.\n\n'
            f"NPC_ID: {npc.npc_id}\n"
            f"NPC_NAME: {npc.display_name}\n"
            f"ROLE:\n{npc.role_summary}\n\n"
            f"TONE:\n{npc.tone_and_vocabulary}\n\n"
            f"RULES:\n{npc.safety_rules}\n\n"
            f"PERSONALITY: {npc.personality}\n"
            f"SOCIAL_TRAITS: {npc.social_traits}\n"
            f"GOALS: {npc.goals}\n"
            f"CAPABILITIES: {npc.capabilities}\n\n"
            f"WORLD_FACTS:\n{turn.world_facts}\n\n"
            f"MEMORY:\n{turn.memory_block}\n\n"
            f"SUMMARY:\n{turn.summary_block}\n\n"
            f"INVENTORY:\n{turn.inventory_block}\n\n"
            f"SURROUNDINGS:\n{turn.surroundings_block}\n\n"
            f"NARRATIVE:\n{turn.narrative_block}\n\n"
            + "\n".join(runtime_rules)
        )
        messages: List[MessageDto] = [MessageDto(role="system", content=system)]
        messages.extend(turn.recent_turns)
        messages.append(MessageDto(role="user", content=turn.latest_player_line))
        return messages


def _list_of_strings(value: object) -> List[str]:
    if not isinstance(value, list):
        return []
    out: List[str] = []
    for item in value:
        s = str(item or "").strip()
        if s:
            out.append(s)
    return out
