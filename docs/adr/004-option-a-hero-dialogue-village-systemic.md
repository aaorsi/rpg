# ADR 004: Option A — hero dialogue first, systemic village

## Status

Accepted (2026-07-19). Phase 6 completed — legacy interaction FSM removed.

## Context

Gameplay review found village interactions and NPC dialogue feel broken: conversations go nowhere, and NPCs lack interesting visible behavior. Root cause: two divergent pipelines — rich hero LLM dialogue vs autonomous interaction FSM that mostly runs template HUD lines and random weighted spawns (see ADR 003 consequences and `.cursor/plans/option-a-village-redesign.plan.md`).

Product decisions for Option A:

1. **Journal panel** for player-visible progression (Phase 5).
2. **Keep LLM deliberation** for NPC movement/planning.
3. **Hero dialogue narrowed to 1:1** in Option A (no multi-party / hero-join interaction FSM).
4. **Retire interaction FSM dialogue** in favor of systemic village simulation.

## Decision

1. `VillageSimulationMode.SystemicOnly` is the only village mode.
2. Village social drama is **systemic + rumor**, not simulated NPC↔NPC conversation:
   - `VillageSystemicEventResolver` + `VillageRumorFeed` on chat proximity
   - Gossip/opinion propagation via `VillageOpinionService`
   - Hero **E** opens direct 1:1 NPC dialogue only
3. Hero dialogue is the **only** LLM conversation surface:
   - `NarrativeGraphService` injects active section objectives into prompts
   - `TurnAuthorizer` tracks stall counts and escalates redirect hints
   - `VillageJournalPanel` (J) surfaces milestones and rumors
4. ADR 003 effect-boundary rules (LLM describes, code executes inventory/coins) **still apply** to hero dialogue actions via `InteractionEffectResolver` for hero-authorized actions.

## Consequences

- Interaction FSM spawn/tick, `InteractionDialogueScript`, and sidecar `run_interaction_line` are removed.
- `InteractionDefinitionRegistry` / `InteractionEffectResolver` remain for deterministic hero action effects and debug registry tests.
- F8 debug panel deprecates interaction start controls; systemic events and opinion debug remain.
- Multi-party / hero-join interaction dialogue APIs are stubbed to no-op.

## Supersedes

Partially supersedes the **player-facing scope** of ADR 003's interaction runner and `engage_dialogue` beats. ADR 003 deterministic effect execution remains valid for hero-triggered actions.
