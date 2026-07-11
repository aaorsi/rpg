from __future__ import annotations

from enum import Enum
from typing import Dict, List, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


# --- Shared bases -----------------------------------------------------------
# Every model speaks camelCase on the wire (matching the LLM contract and the
# generated *.schema.json) while keeping snake_case field names in Python.
# populate_by_name lets callers send either casing during migration.


class CamelModel(BaseModel):
    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        extra="ignore",
    )


class StrictCamelModel(BaseModel):
    """Inbound requests reject unknown fields so contract drift fails loudly."""

    model_config = ConfigDict(
        alias_generator=to_camel,
        populate_by_name=True,
        extra="forbid",
    )


# --- Shared enums / aliases -------------------------------------------------

SCHEMA_VERSION = 1

InteractionOutcome = Literal[
    "reject",
    "partial",
    "cooperate",
    "counter_offer",
    "defer",
    "menace_flavor",
    "unspecified",
]
SocialOutcomeType = Literal[
    "offer_task",
    "accept_task",
    "advice_given",
    "persuasion",
    "payment",
]
StateDeltaValue = Literal["down", "steady", "up"]
RelationshipShift = Literal["negative", "neutral", "positive"]


class NpcType(str, Enum):
    NORMAL = "normal"
    SIDEKICK = "sidekick"
    GHOUL = "ghoul"


class MessageDto(CamelModel):
    role: Literal["system", "user", "assistant"]
    content: str = ""


# --- Request payloads -------------------------------------------------------


class NpcContext(StrictCamelModel):
    npc_id: str
    display_name: str = ""
    npc_type: NpcType = NpcType.NORMAL
    role_summary: str = ""
    tone_and_vocabulary: str = ""
    safety_rules: str = ""
    personality: str = ""
    social_traits: Dict[str, str] = Field(default_factory=dict)
    goals: List[str] = Field(default_factory=list)
    capabilities: List[str] = Field(default_factory=list)
    active_plan_context: str = ""
    active_goals_context: str = ""


class TurnContext(StrictCamelModel):
    world_facts: str = ""
    memory_block: str = ""
    summary_block: str = ""
    inventory_block: str = ""
    surroundings_block: str = ""
    narrative_block: str = ""
    recent_turns: List[MessageDto] = Field(default_factory=list)
    latest_player_line: str


class DialogueTurnRequest(StrictCamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    model: str
    npc: NpcContext
    turn: TurnContext
    api_token: Optional[str] = None
    provider_base_url: Optional[str] = None


class ConversationSummaryRequest(StrictCamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    model: str
    npc_id: str
    turns: List[MessageDto] = Field(default_factory=list)
    api_token: Optional[str] = None
    provider_base_url: Optional[str] = None


class NarrativeGenerationRequest(StrictCamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    model: str
    seed: int
    fallback_canon_json: str
    api_token: Optional[str] = None
    provider_base_url: Optional[str] = None


class NpcPersonaSeed(StrictCamelModel):
    npc_id: str
    name: str = ""
    npc_type: NpcType = NpcType.NORMAL
    archetype_id: str = ""
    archetype_occupation: str = ""
    archetype_personality: str = ""
    archetype_social_traits: Dict[str, str] = Field(default_factory=dict)
    key_information_hints: List[str] = Field(default_factory=list)
    goal_hints: List[str] = Field(default_factory=list)
    capability_hints: List[str] = Field(default_factory=list)
    follower_recruitment_hints: List[str] = Field(default_factory=list)


class NpcPersonaGenerationRequest(StrictCamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    model: str
    npcs: List[NpcPersonaSeed] = Field(default_factory=list, min_length=1)
    api_token: Optional[str] = None
    provider_base_url: Optional[str] = None


class DeliberationTargets(StrictCamelModel):
    location_ids: List[str] = Field(default_factory=list)
    npc_ids: List[str] = Field(default_factory=list)
    work_ids: List[str] = Field(default_factory=list)


class NpcDeliberationRequest(StrictCamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    model: str
    npc_id: str = ""
    goal: str = ""
    max_steps: int = Field(default=4, ge=1, le=12)
    targets: DeliberationTargets = Field(default_factory=DeliberationTargets)
    api_token: Optional[str] = None
    provider_base_url: Optional[str] = None
    # Legacy Unity payload compatibility:
    # { reason, npc: { npcId }, currentGoals, currentPlan, agreements }
    reason: str = ""
    npc: Optional[dict] = None
    world: Optional[dict] = None
    current_goals: List[str] = Field(default_factory=list)
    current_plan: List[dict] = Field(default_factory=list)
    agreements: List[str] = Field(default_factory=list)

    @model_validator(mode="after")
    def _normalize_legacy_payload(self) -> "NpcDeliberationRequest":
        if not self.npc_id and isinstance(self.npc, dict):
            npc_id = str(self.npc.get("npcId") or self.npc.get("npc_id") or "").strip()
            if npc_id:
                self.npc_id = npc_id

        if not self.goal:
            if self.current_goals:
                candidate = str(self.current_goals[0] or "").strip()
                if candidate:
                    self.goal = candidate
            if not self.goal:
                fallback = str(self.reason or "").strip()
                self.goal = fallback or "continue current routine"

        # Best-effort extraction of target IDs from legacy currentPlan payload.
        if self.current_plan and not (
            self.targets.location_ids or self.targets.npc_ids or self.targets.work_ids
        ):
            npc_ids: List[str] = []
            work_ids: List[str] = []
            for step in self.current_plan:
                if not isinstance(step, dict):
                    continue
                npc_id = str(step.get("targetNpcId") or step.get("target_npc_id") or "").strip()
                if npc_id and npc_id not in npc_ids:
                    npc_ids.append(npc_id)
                work_id = str(step.get("workId") or step.get("work_id") or "").strip()
                if work_id and work_id not in work_ids:
                    work_ids.append(work_id)
            if npc_ids:
                self.targets.npc_ids = npc_ids
            if work_ids:
                self.targets.work_ids = work_ids

        if not self.npc_id:
            raise ValueError("npcId is required.")
        return self


class TtsSynthesizeRequest(StrictCamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    text: str
    voice_id: str = ""
    language: str = "english"
    quantize: bool = True
    speaker_role: Literal["npc", "hero", "system"] = "npc"


class InteractionLineRequest(StrictCamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    model: str
    npc_id: str = ""
    display_name: str = ""
    prompt: str
    api_token: Optional[str] = None
    provider_base_url: Optional[str] = None


# --- LLM-output single source of truth --------------------------------------
# These mirror Assets/StreamingAssets/Dialogue/schema/*.schema.json, which are
# generated from these models via scripts/generate_schemas.py.


class ProposedAction(CamelModel):
    action_type: str = ""
    target_id: str = ""
    quantity: float = 1.0
    notes: str = ""


class SocialOutcome(CamelModel):
    outcome_type: SocialOutcomeType
    task_id: str = ""
    target_npc_id: str = ""
    amount: float = 0.0
    currency: str = ""
    persuasion: str = ""
    advice_topic: str = ""
    notes: str = ""


class LlmDialogueOutput(CamelModel):
    """Canonical shape the dialogue model must emit (dialogue_turn_result.schema.json)."""

    say: str
    ack_year: bool = False
    interaction_outcome: InteractionOutcome = "unspecified"
    proposed_npc_actions: List[ProposedAction] = Field(default_factory=list)
    social_outcomes: List[SocialOutcome] = Field(default_factory=list)
    state_deltas: Dict[str, StateDeltaValue] = Field(default_factory=dict)
    milestone_signals: List[str] = Field(default_factory=list)
    memories_to_add: List[Dict[str, str]] = Field(default_factory=list)


class TradeRequirement(CamelModel):
    id: str = ""
    owner_npc_id: str = ""
    gives_item_id: str = ""
    wants_item_id: str = ""
    unlocks: str = ""
    notes: str = ""


class SessionNarrativeCanon(CamelModel):
    """Canonical session narrative (session_narrative.schema.json)."""

    schema_version: int = SCHEMA_VERSION
    session_id: str
    seed: int
    premise_id: str
    world_id: str
    summary: str = ""
    opening_intro_lines: List[str] = Field(default_factory=list)
    world_backstory: str = ""
    global_knowledge: List[str] = Field(default_factory=list)
    final_objective: str
    victory_sequence: List[str] = Field(default_factory=list)
    critical_milestones: List[str] = Field(default_factory=list)
    routes_by_milestone: Dict[str, int] = Field(default_factory=dict)
    trade_requirements: List[TradeRequirement] = Field(default_factory=list)


PrimitiveType = Literal[
    "goto_location",
    "goto_npc",
    "wait_at",
    "perform_work",
    "chat_with_npc",
    "idle_home",
]


class NpcPlanStep(CamelModel):
    primitive_type: PrimitiveType
    target_id: str = ""
    duration_seconds: float = Field(default=0.0, ge=0.0)
    notes: str = ""


# --- Response payloads ------------------------------------------------------


class DialogueTurnResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    say: str
    ack_year: bool = False
    interaction_outcome: InteractionOutcome = "unspecified"
    proposed_actions: List[ProposedAction] = Field(default_factory=list)
    social_outcomes: List[SocialOutcome] = Field(default_factory=list)
    milestone_signals: List[str] = Field(default_factory=list)
    state_deltas: Dict[str, str] = Field(default_factory=dict)
    memories_to_add: List[Dict[str, str]] = Field(default_factory=list)
    raw_assistant: str = ""


class ConversationSummaryResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    summary: str
    learned_facts: List[str] = Field(default_factory=list)
    open_threads: List[str] = Field(default_factory=list)
    relationship_shift: RelationshipShift = "neutral"
    raw_assistant: str = ""


class NarrativeGenerationResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    canon_json: str
    raw_assistant: str = ""


class GeneratedNpcPersona(CamelModel):
    npc_id: str
    name: str = ""
    npc_type: str = NpcType.NORMAL.value
    occupation: str = ""
    personality: str = ""
    social_traits: Dict[str, str] = Field(default_factory=dict)
    key_information: List[str] = Field(default_factory=list)
    goals: List[str] = Field(default_factory=list)
    capabilities: List[str] = Field(default_factory=list)
    follower_recruitment_requirements: List[str] = Field(default_factory=list)


class NpcPersonaGenerationResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    personas: List[GeneratedNpcPersona] = Field(default_factory=list)
    raw_assistant: str = ""


class NpcDeliberationResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    steps: List[NpcPlanStep] = Field(default_factory=list)
    proposed_interactions: List[dict] = Field(default_factory=list)
    used_fallback: bool = False
    raw_assistant: str = ""
    # Legacy Unity response compatibility.
    npc_id: str = ""
    goals: List[str] = Field(default_factory=list)
    plan_steps: List[NpcPlanStep] = Field(default_factory=list)

    @model_validator(mode="after")
    def _sync_legacy_steps(self) -> "NpcDeliberationResponse":
        if self.steps and not self.plan_steps:
            self.plan_steps = list(self.steps)
        elif self.plan_steps and not self.steps:
            self.steps = list(self.plan_steps)
        return self


class TtsSynthesizeResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    sample_rate: int
    audio_format: Literal["wav"] = "wav"
    audio_base64: str
    synthesis_ms: int = 0
    rtf: float = 0.0
    time_to_first_chunk_ms: int = 0
    speaker_role: Literal["npc", "hero", "system"] = "npc"


class InteractionLineResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    say: str
    raw_assistant: str = ""


class PolicyError(CamelModel):
    code: str
    message: str


class PolicyEnvelope(CamelModel):
    ok: bool = True
    error: Optional[PolicyError] = None
    dialogue: Optional[DialogueTurnResponse] = None
    summary: Optional[ConversationSummaryResponse] = None
    narrative: Optional[NarrativeGenerationResponse] = None
    persona: Optional[NpcPersonaGenerationResponse] = None
    deliberation: Optional[NpcDeliberationResponse] = None
    tts: Optional[TtsSynthesizeResponse] = None
    interaction: Optional[InteractionLineResponse] = None

    @model_validator(mode="after")
    def _check_payload(self) -> "PolicyEnvelope":
        payloads = [self.dialogue, self.summary, self.narrative, self.persona, self.deliberation, self.tts, self.interaction]
        if self.ok and not any(payloads):
            raise ValueError("Successful envelope must include a payload.")
        if not self.ok and self.error is None:
            raise ValueError("Failed envelope must include error.")
        return self
