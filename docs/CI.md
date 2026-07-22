# CI — Unity EditMode tests

GitHub Actions runs EditMode tests on pushes and PRs to `main` via `.github/workflows/unity-editmode-tests.yml` (game-ci `unity-test-runner` v4).

## Required repository secrets

| Secret | Purpose |
|--------|---------|
| `UNITY_LICENSE` | Unity `.ulf` license file contents (base64 or raw per game-ci docs) |
| `UNITY_EMAIL` | Unity ID email |
| `UNITY_PASSWORD` | Unity ID password |

`GITHUB_TOKEN` is provided automatically.

## Local equivalent

```bash
/Applications/Unity/Hub/Editor/6000.4.2f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics \
  -projectPath "$(pwd)" \
  -runTests -testPlatform EditMode \
  -testResults TestResults/editmode-results.xml \
  -logFile TestResults/editmode.log
```

## Notes

- Unity project version: `6000.4.2f1` (see `ProjectSettings/ProjectVersion.txt`).
- CI is EditMode-only; PlayMode and builds are out of scope for Option A.
- Forks without secrets will skip or fail license activation — expected until secrets are configured on the repo.
