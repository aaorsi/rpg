from __future__ import annotations

import logging
from contextlib import asynccontextmanager

import httpx
from fastapi import Depends, FastAPI, Request

from .models import (
    ConversationSummaryRequest,
    DialogueTurnRequest,
    NarrativeGenerationRequest,
    NpcDeliberationRequest,
    NpcPersonaGenerationRequest,
    PolicyEnvelope,
    TtsSynthesizeRequest,
)
from .ollama_adapter import OllamaAdapter
from .orchestrator import PolicyOrchestrator
from .tts_service import PocketTtsService

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("policy_orchestrator")


@asynccontextmanager
async def lifespan(app: FastAPI):
    client = httpx.AsyncClient()
    tts_service = PocketTtsService()
    app.state.http_client = client
    app.state.tts_service = tts_service
    app.state.orchestrator = PolicyOrchestrator(OllamaAdapter(client), tts_service=tts_service)
    if tts_service.enabled and tts_service.warmup_on_start:
        try:
            logger.info("TTS warmup starting...")
            tts_service.warmup()
            logger.info("TTS warmup finished.")
        except Exception as ex:
            logger.warning("TTS warmup failed: %s", ex)
    try:
        yield
    finally:
        await client.aclose()


app = FastAPI(title="RPG Policy Orchestrator", version="1.0.0", lifespan=lifespan)


def get_orchestrator(request: Request) -> PolicyOrchestrator:
    return request.app.state.orchestrator


@app.get("/healthz")
async def healthz(request: Request) -> dict:
    service = getattr(request.app.state, "tts_service", None)
    tts = service.stats() if service is not None else {"enabled": False}
    return {"ok": True, "tts": tts}


@app.post("/v1/dialogue/turn", response_model=PolicyEnvelope)
async def dialogue_turn(
    request: DialogueTurnRequest,
    orchestrator: PolicyOrchestrator = Depends(get_orchestrator),
) -> PolicyEnvelope:
    return await orchestrator.run_dialogue_turn(request)


@app.post("/v1/dialogue/summary", response_model=PolicyEnvelope)
async def dialogue_summary(
    request: ConversationSummaryRequest,
    orchestrator: PolicyOrchestrator = Depends(get_orchestrator),
) -> PolicyEnvelope:
    return await orchestrator.run_summary(request)


@app.post("/v1/narrative/generate", response_model=PolicyEnvelope)
async def narrative_generate(
    request: NarrativeGenerationRequest,
    orchestrator: PolicyOrchestrator = Depends(get_orchestrator),
) -> PolicyEnvelope:
    return await orchestrator.run_narrative_generation(request)


@app.post("/v1/npc/persona/generate", response_model=PolicyEnvelope)
async def npc_persona_generate(
    request: NpcPersonaGenerationRequest,
    orchestrator: PolicyOrchestrator = Depends(get_orchestrator),
) -> PolicyEnvelope:
    return await orchestrator.run_npc_persona_generation(request)


@app.post("/v1/npc/deliberate", response_model=PolicyEnvelope)
async def npc_deliberate(
    request: NpcDeliberationRequest,
    orchestrator: PolicyOrchestrator = Depends(get_orchestrator),
) -> PolicyEnvelope:
    return await orchestrator.run_npc_deliberation(request)



@app.post("/v1/tts/synthesize", response_model=PolicyEnvelope)
async def tts_synthesize(
    request: TtsSynthesizeRequest,
    orchestrator: PolicyOrchestrator = Depends(get_orchestrator),
) -> PolicyEnvelope:
    return await orchestrator.run_tts_synthesize(request)
