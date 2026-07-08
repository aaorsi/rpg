# Pydantic Resources (RPG project)

## Knowledge

- [Pydantic Docs — Models & validation](https://docs.pydantic.dev/latest/concepts/models/)
  Official reference for `BaseModel`, `model_validate`, `model_dump`, `ConfigDict`. Use for: any API detail not explained in a lesson.
- [Pydantic Docs — JSON Schema generation](https://docs.pydantic.dev/latest/concepts/json_schema/)
  How `model_json_schema()` works. Use for: understanding `schema_export.py` and generated files under `Assets/StreamingAssets/Dialogue/schema/`.
- [Pydantic Docs — Model validators](https://docs.pydantic.dev/latest/concepts/validators/#model-validators)
  `@model_validator` semantics. Use for: `PolicyEnvelope._check_payload` and future cross-field rules.
- [FastAPI Docs — Request body with Pydantic models](https://fastapi.tiangolo.com/tutorial/body/)
  How FastAPI auto-validates POST bodies into your models. Use for: `app/main.py` endpoint wiring.
- [Repo: `services/policy_orchestrator/README.md`](services/policy_orchestrator/README.md)
  Run commands, schema regeneration, test entry points for this codebase.

## Wisdom (Communities)

- [Pydantic GitHub Discussions](https://github.com/pydantic/pydantic/discussions)
  Maintainer-adjacent Q&A. Use for: edge cases in validation or JSON schema export.
- [r/Python](https://reddit.com/r/Python)
  Broad Python community. Use for: general patterns; filter for Pydantic v2 answers only.

## Gaps

- No dedicated walkthrough of how Unity's `PythonPolicyDtos.cs` stays aligned with `app/models.py` beyond shared JSON schema files — we teach this from the repo directly.
