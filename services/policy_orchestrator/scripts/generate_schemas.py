#!/usr/bin/env python
"""Regenerate the committed JSON schema files from the Pydantic SoT models.

Run from the service root:

    python scripts/generate_schemas.py
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from app.schema_export import SCHEMA_DIR, build_schemas, write_schemas  # noqa: E402


def main() -> None:
    write_schemas()
    for name in build_schemas():
        print(f"wrote {SCHEMA_DIR / name}")


if __name__ == "__main__":
    main()
