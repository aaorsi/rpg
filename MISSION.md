# Mission: Pydantic in the RPG policy orchestrator

## Why

You are building and maintaining an LLM-driven Unity RPG with a Python sidecar (`services/policy_orchestrator`). Pydantic is the contract layer between Unity, the orchestrator, and Ollama — you need to read, extend, and debug it confidently without breaking the Unity dialogue pipeline.

## Success looks like

- Trace a dialogue turn from Unity HTTP payload → Pydantic request model → LLM output → validated response
- Know which file is the single source of truth for JSON shapes, and when to regenerate schemas
- Add or change a field on `LlmDialogueOutput` without breaking `test_schema_sync.py` or Unity validation
- Explain when the codebase uses `StrictCamelModel` vs `CamelModel`, and why

## Constraints

- Learn through this repo's real code first; general Pydantic theory only when it clarifies a local pattern
- Lessons stay short; one concept per session

## Out of scope

- General FastAPI tutorial unrelated to this service
- Unity C# `ResponseValidator` internals (covered only where they mirror Python)
- Deploying the orchestrator to production infrastructure
