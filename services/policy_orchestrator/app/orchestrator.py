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
    GeneratedNpcPersona,
    LlmDialogueOutput,
    InteractionLineRequest,
    InteractionLineResponse,
    MessageDto,
    NarrativeGenerationRequest,
    NarrativeGenerationResponse,
    NpcDeliberationRequest,
    NpcDeliberationResponse,
    NpcPlanStep,
    NpcPersonaGenerationRequest,
    NpcPersonaSeed,
    NpcPersonaGenerationResponse,
    PolicyEnvelope,
    PolicyError,
    SessionNarrativeCanon,
    TtsSynthesizeRequest,
    TtsSynthesizeResponse,
)
from .ollama_adapter import OllamaAdapter
from .policies import DeliberationPolicy, PolicyRegistry
from .tts_service import PocketTtsService

logger = logging.getLogger("policy_orchestrator")

_DEFAULT_PROVIDER = "http://127.0.0.1:11434"
_PRIMITIVE_SYNONYMS = {
    "goto_location": "goto_location",
    "go_to_location": "goto_location",
    "move_to_location": "goto_location",
    "goto_npc": "goto_npc",
    "go_to_npc": "goto_npc",
    "move_to_npc": "goto_npc",
    "wait_at": "wait_at",
    "wait": "wait_at",
    "perform_work": "perform_work",
    "do_work": "perform_work",
    "work": "perform_work",
    "chat_with_npc": "chat_with_npc",
    "chat": "chat_with_npc",
    "talk_to_npc": "chat_with_npc",
    "idle_home": "idle_home",
    "idle": "idle_home",
}


class PolicyOrchestrator:
    def __init__(self, adapter: OllamaAdapter, tts_service: Optional[PocketTtsService] = None) -> None:
        self._adapter = adapter
        self._tts_service = tts_service
        self._registry = PolicyRegistry()
        self._deliberation_policy = DeliberationPolicy(
            allowed_primitives={
                "goto_location",
                "goto_npc",
                "wait_at",
                "perform_work",
                "chat_with_npc",
                "idle_home",
            }
        )

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

    async def run_interaction_line(self, request: InteractionLineRequest) -> PolicyEnvelope:
        rid = request.request_id
        version_error = self._reject_unsupported_version(request.schema_version, rid)
        if version_error is not None:
            return version_error
        try:
            system = (
                "You are an NPC in a village RPG. Reply with ONLY valid JSON matching "
                '{"say":"one short in-character line"}. No markdown, no extra keys.'
            )
            raw = await self._adapter.chat(
                base_url=request.provider_base_url or _DEFAULT_PROVIDER,
                model=request.model,
                messages=[
                    MessageDto(role="system", content=system),
                    MessageDto(role="user", content=request.prompt or ""),
                ],
                api_token=request.api_token,
            )
            llm_output = LlmDialogueOutput.model_validate(parse_dialogue_output(raw))
            say = (llm_output.say or "").strip()
            if not say:
                raise ValueError("empty_say")
            response = InteractionLineResponse(
                request_id=rid,
                say=say,
                raw_assistant=raw,
            )
            return PolicyEnvelope(ok=True, interaction=response)
        except Exception as ex:
            return self._fail("interaction_line_failed", rid, ex)

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
            fallback_obj = parse_json_object(request.fallback_canon_json)
            fallback_obj["seed"] = request.seed
            system = "You are a game narrative generator. Return ONLY JSON matching the provided schema scaffold."
            user = (
                "Generate coherent narrative from this scaffold and preserve route counts >= 2. "
                "Use only NPC ids, item ids, and locations already present in the scaffold. "
                "Do not invent phantom quest items (no magic diamonds, portal cores, magic statues, or hidden castles).\n"
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
            try:
                obj = parse_json_object(raw)
            except ValueError:
                # Prefer continuity over failure: if the model emits non-JSON, keep play moving with fallback canon.
                obj = fallback_obj
            obj["seed"] = request.seed
            invalid = self._narrative_route_violation(obj)
            if invalid is not None:
                obj = fallback_obj
            response = NarrativeGenerationResponse(
                request_id=rid,
                canon_json=json.dumps(obj, separators=(",", ":")),
                raw_assistant=raw,
            )
            return PolicyEnvelope(ok=True, narrative=response)
        except Exception as ex:
            return self._fail("narrative_failed", rid, ex)

    async def run_npc_persona_generation(self, request: NpcPersonaGenerationRequest) -> PolicyEnvelope:
        rid = request.request_id
        version_error = self._reject_unsupported_version(request.schema_version, rid)
        if version_error is not None:
            return version_error
        try:
            raw = await self._adapter.chat(
                base_url=request.provider_base_url or _DEFAULT_PROVIDER,
                model=request.model,
                messages=self._build_persona_messages(request),
                api_token=request.api_token,
            )
            personas = self._parse_personas_with_fallback(raw=raw, seeds=request.npcs)
            response = NpcPersonaGenerationResponse(
                request_id=rid,
                personas=personas,
                raw_assistant=raw,
            )
            return PolicyEnvelope(ok=True, persona=response)
        except Exception as ex:
            return self._fail("npc_persona_generation_failed", rid, ex)

    async def run_npc_deliberation(self, request: NpcDeliberationRequest) -> PolicyEnvelope:
        rid = request.request_id
        version_error = self._reject_unsupported_version(request.schema_version, rid)
        if version_error is not None:
            return version_error
        try:
            raw = await self._adapter.chat(
                base_url=request.provider_base_url or _DEFAULT_PROVIDER,
                model=request.model,
                messages=self._build_deliberation_messages(request),
                api_token=request.api_token,
            )
            try:
                parsed = self._parse_deliberation_steps(raw, max_steps=request.max_steps)
            except ValueError:
                parsed = []
            try:
                proposed_interactions = self._filter_proposed_interactions(
                    self._parse_deliberation_proposed_interactions(raw)
                )
            except ValueError:
                proposed_interactions = []
            normalized = self._deliberation_policy.normalize_steps(
                parsed,
                self_npc_id=request.npc_id,
                location_ids=set(request.targets.location_ids),
                npc_ids=set(request.targets.npc_ids),
                work_ids=set(request.targets.work_ids),
            )
            used_fallback = False
            if not normalized:
                normalized = self._fallback_plan_steps(request)
                used_fallback = True
            response = NpcDeliberationResponse(
                request_id=rid,
                steps=normalized,
                proposed_interactions=proposed_interactions,
                used_fallback=used_fallback,
                raw_assistant=raw,
            )
            return PolicyEnvelope(ok=True, deliberation=response)
        except Exception as ex:
            return self._fail("npc_deliberation_failed", rid, ex)

    async def run_tts_synthesize(self, request: TtsSynthesizeRequest) -> PolicyEnvelope:
        rid = request.request_id
        version_error = self._reject_unsupported_version(request.schema_version, rid)
        if version_error is not None:
            return version_error
        if self._tts_service is None:
            return PolicyEnvelope(ok=False, error=PolicyError(code="tts_unavailable", message="tts_unavailable"))
        try:
            payload = self._tts_service.synthesize(
                text=request.text,
                voice_id=request.voice_id,
                language=request.language,
                quantize=request.quantize,
                speaker_role=request.speaker_role,
            )
            response = TtsSynthesizeResponse(
                request_id=rid,
                sample_rate=payload["sampleRate"],
                audio_format=payload["audioFormat"],
                audio_base64=payload["audioBase64"],
                synthesis_ms=payload["synthesisMs"],
                rtf=payload["rtf"],
                time_to_first_chunk_ms=payload["timeToFirstChunkMs"],
                speaker_role=payload["speakerRole"],
            )
            return PolicyEnvelope(ok=True, tts=response)
        except ValueError as ex:
            code = str(ex).strip() or "tts_bad_request"
            return PolicyEnvelope(ok=False, error=PolicyError(code=code, message=code))
        except Exception as ex:
            return self._fail("tts_failed", rid, ex)

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
            social_outcomes=llm.social_outcomes,
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
            f"ALLOWED_SOCIAL_OUTCOMES: {sorted(list(policy.allowed_social_outcome_types))}",
        ]
        if policy.forbid_state_deltas:
            runtime_rules.append("STATE_DELTAS_DISABLED: true")
        if policy.force_all_caps:
            runtime_rules.append("SPEECH_ALL_CAPS: true")
        if policy.forced_interaction_outcome:
            runtime_rules.append(f"FORCED_OUTCOME: {policy.forced_interaction_outcome}")

        system = (
            "You are an in-world character. Return ONLY JSON with keys: "
            'say, ackYear, interactionOutcome, proposedNpcActions, socialOutcomes, milestoneSignals, stateDeltas, memoriesToAdd.\n\n'
            f"NPC_ID: {npc.npc_id}\n"
            f"NPC_NAME: {npc.display_name}\n"
            f"ROLE:\n{npc.role_summary}\n\n"
            f"TONE:\n{npc.tone_and_vocabulary}\n\n"
            f"RULES:\n{npc.safety_rules}\n\n"
            f"PERSONALITY: {npc.personality}\n"
            f"SOCIAL_TRAITS: {npc.social_traits}\n"
            f"GOALS: {npc.goals}\n"
            f"CAPABILITIES: {npc.capabilities}\n"
            f"ACTIVE_PLAN_CONTEXT: {npc.active_plan_context}\n"
            f"ACTIVE_GOALS_CONTEXT: {npc.active_goals_context}\n\n"
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

    def _build_persona_messages(self, request: NpcPersonaGenerationRequest) -> List[MessageDto]:
        system = (
            "You synthesize RPG NPC personas from archetype inputs. "
            "Return ONLY a JSON object with key `personas`, where each entry uses keys: "
            "npcId, name, npcType, occupation, personality, socialTraits, keyInformation, goals, "
            "capabilities, followerRecruitmentRequirements. "
            "Every output persona must keep the same npcId from the input batch."
        )
        user = (
            "Generate one persona per input NPC. Use archetype fields as anchors and keep output concise.\n"
            f"batch={json.dumps([npc.model_dump(by_alias=True) for npc in request.npcs])}"
        )
        return [
            MessageDto(role="system", content=system),
            MessageDto(role="user", content=user),
        ]

    def _build_deliberation_messages(self, request: NpcDeliberationRequest) -> List[MessageDto]:
        allowed = sorted(self._deliberation_policy.allowed_primitives)
        world_facts = ""
        surroundings = ""
        if isinstance(request.world, dict):
            world_facts = str(request.world.get("worldFacts") or request.world.get("world_facts") or "").strip()
            surroundings = str(request.world.get("surroundingsBlock") or request.world.get("surroundings_block") or "").strip()
        system = (
            "You produce compact NPC execution plans from goals. "
            "Return ONLY JSON with key `steps` (array). "
            "Each step must include keys: primitiveType, targetId, durationSeconds, notes. "
            "Use only these primitiveType values exactly: "
            f"{allowed}. "
            "targetId must be empty for idle_home. "
            "targetId for goto_location/wait_at must be from locationIds, "
            "for goto_npc/chat_with_npc from npcIds, and for perform_work from workIds. "
            "Treat locationIds/npcIds/workIds as authoritative and never invent IDs. "
            "If the goal mentions unavailable entities, plan only with the provided valid IDs."
        )
        user = (
            f"npcId={request.npc_id}\n"
            f"goal={request.goal}\n"
            f"maxSteps={request.max_steps}\n"
            f"locationIds={json.dumps(request.targets.location_ids)}\n"
            f"npcIds={json.dumps(request.targets.npc_ids)}\n"
            f"workIds={json.dumps(request.targets.work_ids)}\n"
            f"worldFacts={world_facts}\n"
            f"surroundings={surroundings}"
        )
        return [
            MessageDto(role="system", content=system),
            MessageDto(role="user", content=user),
        ]

    def _parse_personas_with_fallback(self, raw: str, seeds: List[NpcPersonaSeed]) -> List[GeneratedNpcPersona]:
        seed_lookup = {seed.npc_id: seed for seed in seeds}
        by_npc_id: dict[str, GeneratedNpcPersona] = {}
        try:
            obj = parse_json_object(raw)
            candidates = obj.get("personas") or obj.get("npcProfiles") or obj.get("profiles")
            if isinstance(candidates, dict):
                candidates = [candidates]
            if not isinstance(candidates, list):
                candidates = []
            for candidate in candidates:
                if not isinstance(candidate, dict):
                    continue
                npc_id = str(candidate.get("npcId") or candidate.get("npc_id") or "").strip()
                if not npc_id:
                    continue
                seed = seed_lookup.get(npc_id)
                if seed is None:
                    continue
                try:
                    by_npc_id[npc_id] = self._normalize_persona(candidate, seed)
                except ValidationError:
                    by_npc_id[npc_id] = self._fallback_persona(seed)
        except ValueError:
            by_npc_id = {}

        return [by_npc_id.get(seed.npc_id) or self._fallback_persona(seed) for seed in seeds]

    def _normalize_persona(self, raw_persona: dict, seed: NpcPersonaSeed) -> GeneratedNpcPersona:
        fallback = self._fallback_persona(seed)
        raw_social_traits = raw_persona.get("socialTraits") or raw_persona.get("social_traits")
        social_traits: dict[str, str] = {}
        if isinstance(raw_social_traits, dict):
            for key, value in raw_social_traits.items():
                k = str(key or "").strip()
                v = str(value or "").strip().lower()
                if k and v:
                    social_traits[k] = v
        persona = GeneratedNpcPersona(
            npc_id=str(raw_persona.get("npcId") or raw_persona.get("npc_id") or fallback.npc_id).strip()
            or fallback.npc_id,
            name=str(raw_persona.get("name") or fallback.name).strip() or fallback.name,
            npc_type=str(raw_persona.get("npcType") or raw_persona.get("npc_type") or fallback.npc_type).strip()
            or fallback.npc_type,
            occupation=str(raw_persona.get("occupation") or fallback.occupation).strip() or fallback.occupation,
            personality=str(raw_persona.get("personality") or fallback.personality).strip() or fallback.personality,
            social_traits=social_traits or dict(fallback.social_traits),
            key_information=_list_of_strings(raw_persona.get("keyInformation") or raw_persona.get("key_information"))
            or list(fallback.key_information),
            goals=_list_of_strings(raw_persona.get("goals")) or list(fallback.goals),
            capabilities=_list_of_strings(raw_persona.get("capabilities")) or list(fallback.capabilities),
            follower_recruitment_requirements=_list_of_strings(
                raw_persona.get("followerRecruitmentRequirements")
                or raw_persona.get("follower_recruitment_requirements")
            )
            or list(fallback.follower_recruitment_requirements),
        )
        if persona.npc_id != seed.npc_id:
            return fallback
        return persona

    def _fallback_persona(self, seed: NpcPersonaSeed) -> GeneratedNpcPersona:
        return GeneratedNpcPersona(
            npc_id=seed.npc_id,
            name=seed.name or seed.npc_id,
            npc_type=seed.npc_type.value,
            occupation=seed.archetype_occupation or "Villager",
            personality=seed.archetype_personality or "Reserved and practical.",
            social_traits=dict(seed.archetype_social_traits),
            key_information=list(seed.key_information_hints),
            goals=list(seed.goal_hints),
            capabilities=list(seed.capability_hints),
            follower_recruitment_requirements=list(seed.follower_recruitment_hints),
        )

    def _parse_deliberation_steps(self, raw: str, *, max_steps: int) -> List[NpcPlanStep]:
        obj = parse_json_object(raw)
        candidates = obj.get("steps") or obj.get("planSteps") or obj.get("plan")
        if isinstance(candidates, dict):
            candidates = [candidates]
        if not isinstance(candidates, list):
            return []

        steps: List[NpcPlanStep] = []
        for candidate in candidates[:max_steps]:
            if not isinstance(candidate, dict):
                continue
            primitive_raw = str(
                candidate.get("primitiveType")
                or candidate.get("primitive_type")
                or candidate.get("type")
                or candidate.get("action")
                or ""
            ).strip()
            primitive = _PRIMITIVE_SYNONYMS.get(primitive_raw.lower())
            if primitive is None:
                continue
            target = str(candidate.get("targetId") or candidate.get("target_id") or "").strip()
            duration = candidate.get("durationSeconds")
            if duration is None:
                duration = candidate.get("duration_seconds")
            try:
                parsed_duration = max(0.0, float(duration)) if duration is not None else 0.0
            except (TypeError, ValueError):
                parsed_duration = 0.0
            notes = str(candidate.get("notes") or "").strip()
            steps.append(
                NpcPlanStep(
                    primitive_type=primitive,
                    target_id=target,
                    duration_seconds=parsed_duration,
                    notes=notes,
                )
            )
        return steps

    def _parse_deliberation_proposed_interactions(self, raw: str) -> List[dict]:
        obj = parse_json_object(raw)
        candidates = (
            obj.get("proposedInteractions")
            or obj.get("proposed_interactions")
            or obj.get("newInteractions")
            or obj.get("new_interactions")
        )
        if isinstance(candidates, dict):
            candidates = [candidates]
        if not isinstance(candidates, list):
            return []
        out: List[dict] = []
        for candidate in candidates:
            if isinstance(candidate, dict):
                out.append(candidate)
        return out

    _ALLOWED_INTERACTION_ACTION_TYPES = {
        "move_to_location",
        "move_to_npc",
        "move_to_hero",
        "engage_dialogue",
        "exchange_item",
        "exchange_coins",
    }

    def _filter_proposed_interactions(self, candidates: List[dict]) -> List[dict]:
        if not candidates:
            return []
        out: List[dict] = []
        for candidate in candidates:
            if not isinstance(candidate, dict):
                continue
            interaction_id = str(candidate.get("id") or "").strip()
            if not interaction_id:
                continue
            phases = candidate.get("phases")
            if phases is not None and not isinstance(phases, dict):
                continue
            invalid_action = False
            if isinstance(phases, dict):
                for phase_key in ("start", "loop", "end"):
                    steps = phases.get(phase_key) or []
                    if not isinstance(steps, list):
                        continue
                    for step in steps:
                        if not isinstance(step, dict):
                            continue
                        action = str(
                            step.get("actionType") or step.get("action_type") or ""
                        ).strip().lower()
                        if action and action not in self._ALLOWED_INTERACTION_ACTION_TYPES:
                            invalid_action = True
                            break
                    if invalid_action:
                        break
            if invalid_action:
                continue
            out.append(candidate)
        return out

    def _fallback_plan_steps(self, request: NpcDeliberationRequest) -> List[NpcPlanStep]:
        steps: List[NpcPlanStep] = []
        if request.targets.work_ids:
            steps.append(NpcPlanStep(primitive_type="perform_work", target_id=request.targets.work_ids[0]))
            steps.append(NpcPlanStep(primitive_type="idle_home"))
            return steps[: request.max_steps]
        if request.targets.location_ids:
            location = request.targets.location_ids[0]
            steps.append(NpcPlanStep(primitive_type="goto_location", target_id=location))
            steps.append(NpcPlanStep(primitive_type="wait_at", target_id=location, duration_seconds=2.0))
            return steps[: request.max_steps]
        npc_candidates = [npc_id for npc_id in request.targets.npc_ids if npc_id != request.npc_id]
        if npc_candidates:
            target = npc_candidates[0]
            steps.append(NpcPlanStep(primitive_type="goto_npc", target_id=target))
            steps.append(NpcPlanStep(primitive_type="chat_with_npc", target_id=target, duration_seconds=2.0))
            return steps[: request.max_steps]
        return [NpcPlanStep(primitive_type="idle_home")]


def _list_of_strings(value: object) -> List[str]:
    if not isinstance(value, list):
        return []
    out: List[str] = []
    for item in value:
        s = str(item or "").strip()
        if s:
            out.append(s)
    return out
