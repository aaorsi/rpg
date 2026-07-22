# Option A baseline playtest script

Use this checklist after merging Option A (Phases 1–7) and before tuning content. Record results in the metrics table at the bottom.

## Setup

1. Start a **new play session** (clears quest state, rumors, inventory per persistence policy).
2. Enable Python policy sidecar if you normally play with it (`usePythonPolicyOrchestrator`).
3. Do **not** open F8 unless verifying debug tools.

## Session A — Hero dialogue (3 conversations, ~15 min)

For each of three different NPCs (merchant-type, villager/gossip, quest-relevant):

| Step | Check |
|------|--------|
| Open dialogue with **E** | Direct 1:1 only; no “join interaction” flow |
| Opening line | Attitude subtitle under name; line feels contextual |
| 3+ turns | Prompt stays on-topic when a milestone is active |
| End session | At least one of: milestone signal, trade, memory, agreement, or opinion shift |

**Notes:** _______________________________________________

## Session B — Village observation (10 min, no F8)

| Step | Check |
|------|--------|
| Watch NPC movement | Deliberation-driven plans; pairs chat in proximity |
| Rumors | HUD toast or journal (**J**) shows village rumor within 10 min |
| Journal (**J**) | Milestones listed; “Current focus” matches active graph section |
| Background spam | **No** lines like “Alice and Bob continue Romantic Relationship” |

**Notes:** _______________________________________________

## Session C — Group ask (optional, if thresholds met)

| Step | Check |
|------|--------|
| Raise village standing via debug or play | Mayor/faith ask becomes **offered** |
| Player UI | Top banner or HUD toast mentions the ask |
| Hero dialogue | NPC can discuss leadership / faith arc |

**Notes:** _______________________________________________

## Metrics (baseline)

| Metric | Target | Your result |
|--------|--------|-------------|
| EditMode tests | 136/136 pass | |
| Hero turns with world state change | ≥ 1 per 3-turn conversation | /3 |
| Player can answer “what next?” from **J** without F8 | Yes | |
| Village feels alive without fake NPC dialogue | Subjective 1–5 | |
| Conversation “went somewhere” | Subjective 1–5 | |

## Regression watchlist

- Hero dialogue with sidecar includes narrative section + rumors + role rules
- E never opens multi-party interaction FSM
- Rumor feed clears on new play session
- Group-ask milestone signals not lost before quest state init
