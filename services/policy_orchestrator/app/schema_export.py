from __future__ import annotations

import json
from pathlib import Path
from typing import Dict

from .models import LlmDialogueOutput, SessionNarrativeCanon

# Repo root is four levels up: app -> policy_orchestrator -> services -> <repo>.
REPO_ROOT = Path(__file__).resolve().parents[3]
SCHEMA_DIR = REPO_ROOT / "Assets" / "StreamingAssets" / "Dialogue" / "schema"

# Pydantic model -> committed schema file name.
SCHEMA_TARGETS = {
    "dialogue_turn_result.schema.json": LlmDialogueOutput,
    "session_narrative.schema.json": SessionNarrativeCanon,
}


def build_schemas() -> Dict[str, str]:
    """Return {filename: serialized JSON schema} generated from the SoT models."""
    out: Dict[str, str] = {}
    for file_name, model in SCHEMA_TARGETS.items():
        schema = model.model_json_schema(by_alias=True)
        out[file_name] = json.dumps(schema, indent=2, sort_keys=True) + "\n"
    return out


def write_schemas() -> None:
    SCHEMA_DIR.mkdir(parents=True, exist_ok=True)
    for file_name, contents in build_schemas().items():
        (SCHEMA_DIR / file_name).write_text(contents, encoding="utf-8")
