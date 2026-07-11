# ADR 003: Village interaction pipeline boundaries

## Status

Accepted (2026-07-12)

## Context

Village social interactions (steal, bribe, romance, etc.) mix LLM dialogue with deterministic game state (inventory, movement, outcomes). Without clear stage boundaries, LLMs pretend to steal items or pay coins, and debug tooling cannot validate behavior.

## Decision

1. **Interaction types** are declarative (`interactions.seed.json` + runtime merge). **Action types** are atomic steps executed by `VillageAgentSimulation`.
2. **LLM is used only on `engage_dialogue` beats** via autonomous line generation (sidecar `/v1/interaction/line` or direct Ollama fallback). Lines do not commit into hero dialogue session history.
3. **Effects** (`exchange_item`, `exchange_coins`) run in `InteractionEffectResolver` — the LLM describes; code executes.
4. **Outcomes** derive from `stepLog` first; weighted rolls are fallback only.
5. **Runtime is 1–1** (actor + target). Extra participants are supported for group debug start and multi-party dialogue join; loop audience rotation uses `extraParticipantNpcIds`.
6. **World references** for validation/deliberation come from `VillageWorldReferenceSnapshot` refreshed with NPC positions every 10–30s.

## Consequences

- Debug panel (F8) can trigger and read interaction state without reproducing long hero dialogue.
- Hero joins joinable interactions via **E** when `heroJoinEnabled` is true.
- New interaction types require seed JSON + validator pass; LLM proposals stay `proposed` until promoted to `active`.
