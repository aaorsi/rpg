from __future__ import annotations

import logging
from contextlib import asynccontextmanager

import httpx
from fastapi import Depends, FastAPI, Request

from .models import (
    ConversationSummaryRequest,
    DialogueTurnRequest,
    NarrativeGenerationRequest,
    PolicyEnvelope,
)
from .ollama_adapter import OllamaAdapter
from .orchestrator import PolicyOrchestrator

logging.basicConfig(level=logging.INFO)


@asynccontextmanager
async def lifespan(app: FastAPI):
    client = httpx.AsyncClient()
    app.state.http_client = client
    app.state.orchestrator = PolicyOrchestrator(OllamaAdapter(client))
    try:
        yield
    finally:
        await client.aclose()


app = FastAPI(title="RPG Policy Orchestrator", version="1.0.0", lifespan=lifespan)


def get_orchestrator(request: Request) -> PolicyOrchestrator:
    return request.app.state.orchestrator


@app.get("/healthz")
async def healthz() -> dict:
    return {"ok": True}


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
