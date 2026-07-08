# Project context from zoom-out session

The user has a broad map of the RPG repo: Unity `DialogueManager` talks to Ollama directly or via `services/policy_orchestrator`, and narrative JSON lives under `Assets/StreamingAssets/Dialogue/`. They have not yet demonstrated understanding of Pydantic-specific patterns (`CamelModel`, schema sync, `model_validate` flow).

**Implications:** First lessons should anchor on `app/models.py` and the request → parse → validate pipeline, not re-teach the Unity dialogue loop.
