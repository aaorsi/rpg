# RPG Policy Orchestrator (FastAPI)

Local sidecar service that centralizes LLM prompt orchestration, NPC type policy enforcement, and strict payload validation.

## Endpoints

- `POST /v1/dialogue/turn`
- `POST /v1/dialogue/summary`
- `POST /v1/narrative/generate`
- `POST /v1/npc/persona/generate`
- `POST /v1/npc/deliberate`
- `GET /healthz`

## Run locally

```bash
cd services/policy_orchestrator
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --host 127.0.0.1 --port 8787 --reload
```

## Pocket TTS setup (CPU)

Pocket TTS is optional and disabled by default unless enabled by environment variables.

```bash
cd services/policy_orchestrator
source .venv/bin/activate
pip install --index-url https://download.pytorch.org/whl/cpu torch
pip install pocket-tts
```

Environment toggles:

- `TTS_ENABLED=true`
- `TTS_LANGUAGE=english`
- `TTS_QUANTIZE=true`
- `TTS_DEFAULT_VOICE=alba`
- `TTS_MAX_TEXT_CHARS=280`

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
