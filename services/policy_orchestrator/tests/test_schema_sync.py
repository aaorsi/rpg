import pytest

from app.schema_export import SCHEMA_DIR, build_schemas


@pytest.mark.parametrize("file_name", list(build_schemas().keys()))
def test_committed_schema_matches_models(file_name: str) -> None:
    expected = build_schemas()[file_name]
    committed_path = SCHEMA_DIR / file_name
    assert committed_path.exists(), f"Missing {committed_path}; run scripts/generate_schemas.py"
    actual = committed_path.read_text(encoding="utf-8")
    assert actual == expected, (
        f"{file_name} is out of sync with the Pydantic models. "
        "Run: python scripts/generate_schemas.py"
    )
