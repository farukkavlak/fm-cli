# AI checklist

Work through it in order. Why: [AI-ROADMAP.md](AI-ROADMAP.md). How: [AI-DESIGN.md](AI-DESIGN.md).

## Phase 0 — clean baseline
- [x] Remove training sentences that repeat test sentences (27 found)
- [x] Measure the clean baseline: `mpnet-t-q` held top-1 75.8% / tool+args 62.8% (noisy 66.9% / 52.0%)
- [x] Training refuses to start when `nl-check.py` fails
- [x] Clean lower-case model `mpnet-l-q` is the default: held top-1 78.8% / tool+args 64.6% (noisy 68.9% / 54.0%)
- [x] Fix metric names everywhere: top-1, recall@k, tool+args, ask, multi, none, false none (`tests/nl/README.md`)
- [x] Save a manifest with every result: data hash, split, model SHA, commit, machine (`nl-score.py --save` → `tests/nl/results/`)
- [ ] Split by sentence family, so near-copies stay on one side; keep a final test set nobody tunes on
- [ ] Grow the multi-ask set (8 held sentences today)

## Phase 1 — candidates + local LLM experiment
- [x] Measure recall@k: held recall@3 87.4% · recall@5 88.3% (top-1 78.8%); several asks @5 37.5% (n=8)
- [ ] Candidates from a wider pool of neighbours (e.g. 50); measure recall@5
- [ ] Several asks: split first, one candidate list per ask; measure recall@5
- [ ] Write the experiment plan and get it approved: models, versions, licenses, quantization, metrics, thresholds
- [ ] Measure with FM running: understanding · planning (tool + arguments from candidates) · conversation · memory · latency
- [ ] Arguments: LLM vs today's readers (readers alone: 76.7% held)
- [ ] Decision note: which jobs run locally, which need a cloud model

## Phase 2 — one safety policy
- [ ] Mark every tool read-only, reversible or high-impact, next to its definition
- [ ] CLI, MCP and sentences follow the same rules (today only sentences ask first)
- [ ] Safety bench: no game-changing tool runs without approval; traps like `next opponent`
- [ ] A multi-step run that fails half way reports what was done

## Phase 3 — FM27 + names from the save
- [ ] Port the bridge when FM27 is out (`docs/PORTING.md`, `tools/identity-check.py`)
- [ ] Players, clubs, offers from the save with stable ids; cache expires when the save changes
- [ ] Fuzzy matching for Turkish endings and typos ("frosinonenin rinaldi")
- [ ] Ask when two candidates fit
- [ ] Entity bench

## Phase 4 — Mahmut, read-only
- [ ] Model provider interface; cloud adapter with the user's key, off by default
- [ ] Session state: last turns, "him", "that player"
- [ ] Squad, injuries, fixtures, offers, answered only from what was read
- [ ] Show what was checked (which tool, which data)
- [ ] Grounding bench on saved game snapshots

## Phase 5 — plans, memory, advice
- [ ] Typed plan: steps, order, missing values, risk, approval, result
- [ ] Propose → approve → run → verify
- [ ] Save memory: fact / preference / guess / plan / outcome; list, fix, delete
- [ ] Transfer advice; tactical comments from real match data
- [ ] Plan bench

## Phase 6 — polish
- [ ] Mahmut Local, if Phase 1 says so
- [ ] Personality setting (serious ↔ fun); dialogue bench
- [ ] README: short demo, architecture, numbers, limits
- [ ] Publish the model with `tools/nl-release.sh` once the repo is public
