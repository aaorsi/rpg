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


# --- LLM-output single source of truth --------------------------------------
# These mirror Assets/StreamingAssets/Dialogue/schema/*.schema.json, which are
# generated from these models via scripts/generate_schemas.py.


class ProposedAction(CamelModel):
    action_type: str = ""
    target_id: str = ""
    quantity: float = 1.0
    notes: str = ""


class LlmDialogueOutput(CamelModel):
    """Canonical shape the dialogue model must emit (dialogue_turn_result.schema.json)."""

    say: str
    ack_year: bool = False
    interaction_outcome: InteractionOutcome = "unspecified"
    proposed_npc_actions: List[ProposedAction] = Field(default_factory=list)
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


# --- Response payloads ------------------------------------------------------


class DialogueTurnResponse(CamelModel):
    schema_version: int = SCHEMA_VERSION
    request_id: str = ""
    say: str
    ack_year: bool = False
    interaction_outcome: InteractionOutcome = "unspecified"
    proposed_actions: List[ProposedAction] = Field(default_factory=list)
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


class PolicyError(CamelModel):
    code: str
    message: str


class PolicyEnvelope(CamelModel):
    ok: bool = True
    error: Optional[PolicyError] = None
    dialogue: Optional[DialogueTurnResponse] = None
    summary: Optional[ConversationSummaryResponse] = None
    narrative: Optional[NarrativeGenerationResponse] = None

    @model_validator(mode="after")
    def _check_payload(self) -> "PolicyEnvelope":
        payloads = [self.dialogue, self.summary, self.narrative]
        if self.ok and not any(payloads):
            raise ValueError("Successful envelope must include a payload.")
        if not self.ok and self.error is None:
            raise ValueError("Failed envelope must include error.")
        return self
