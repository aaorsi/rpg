# ADR 004: Option A — hero dialogue first, systemic village

## Status

Accepted (2026-07-19)

## Context

Gameplay review found village interactions and NPC dialogue feel broken: conversations go nowhere, and NPCs lack interesting visible behavior. Root cause: two divergent pipelines — rich hero LLM dialogue vs autonomous interaction FSM that mostly runs template HUD lines and random weighted spawns (see ADR 003 consequences and `.cursor/plans/option-a-village-redesign.plan.md`).

Product decisions for Option A:

1. **Journal panel** for player-visible progression (implemented in later phases).
2. **Keep LLM deliberation** for NPC movement/planning.
3. **Hero dialogue narrowed to 1:1** in Option A (no multi-party / hero-join interaction FSM).
4. **Retire interaction FSM dialogue** in favor of systemic village simulation.

## Decision

1. Introduce `VillageSimulationMode.SystemicOnly` as the default village mode.
2. In `SystemicOnly`:
   - Disable interaction FSM spawn/tick and hero-join-interaction dialogue.
   - Hero **E** opens direct 1:1 NPC dialogue only.
   - No autonomous interaction dialogue beats (background/HUD/LLM interaction lines).
   - Retain deliberation, gossip, opinion, agreements, and hero dialogue LLM path.
3. `LegacyInteractionFsm` remains behind flag for rollback and tests until Phase 6 deletion.
4. ADR 003 effect-boundary rules (LLM describes, code executes inventory/coins) **still apply** to hero dialogue actions.

## Consequences

- Phase 1 stops fake village dialogue immediately.
- Later phases add narrative graph, turn authorization, rumor feed, and journal UI without interaction FSM.
- F8 debug panel deprecates interaction controls under `SystemicOnly`.
- Multi-party dialogue APIs are blocked in `SystemicOnly`; debug may still use legacy mode.

## Supersedes

Partially supersedes the **player-facing scope** of ADR 003's interaction runner and `engage_dialogue` beats. ADR 003 deterministic effect execution remains valid for hero-triggered actions.
