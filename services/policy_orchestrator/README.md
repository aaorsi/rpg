# RPG Policy Orchestrator (FastAPI)

Local sidecar service that centralizes LLM prompt orchestration, NPC type policy enforcement, and strict payload validation.

## Endpoints

- `POST /v1/dialogue/turn`
- `POST /v1/dialogue/summary`
- `POST /v1/narrative/generate`
- `POST /v1/npc/persona/generate`
- `GET /healthz`

## Run locally

```bash
cd services/policy_orchestrator
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --host 127.0.0.1 --port 8787 --reload
```

## Tests

```bash
cd services/policy_orchestrator
source .venv/bin/activate
pytest
```

## Schemas (single source of truth)

The Pydantic models in `app/models.py` are canonical. The JSON schemas under
`Assets/StreamingAssets/Dialogue/schema/` are generated from them:

```bash
python scripts/generate_schemas.py
```

`tests/test_schema_sync.py` fails if the committed schema files drift from the
models, so regenerate after changing `LlmDialogueOutput` or `SessionNarrativeCanon`.
