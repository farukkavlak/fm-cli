# AI

Playing by sentence (`fm <sentence>`), and later an assistant (Mahmut).

## Rules

- A model only proposes. Our code checks, asks and runs.
- Facts come from the game. Say what was checked.
- Every feature is a command first.
- Local and free by default. A cloud model is the user's choice, with the user's key.

## Today

- A router picks the tool: a fine-tuned multilingual embedding model (ONNX, 275 MB, ~5 ms), kNN vote, rules.
- ≥80% sure: run it (ask first if it changes the game). 50–80%: offer 3. Below: offer 5.
- Code: `cli/fm/Nl/`. Tests: `tests/nl/`. Model installed by `fm doctor --install`.
- Smart mode (opt-in): Claude Haiku answers when the router is unsure.
- Local LLMs were measured on this Mac (29 Oct): all failed. Not used.

## First release

- Safety: task T153 in `docs/TASKS.md`.

## Later (v2)

- [ ] Players, clubs, offers from the save with stable ids
- [ ] Fuzzy names: Turkish endings, typos ("frosinonenin rinaldi"); ask when two fit
- [ ] Mahmut, read-only: provider interface, session memory ("him"), answers only from what was read
- [ ] Plans: steps, approval, run, verify
- [ ] Save memory: facts, preferences, plans; list, fix, delete
- [ ] Transfer and tactic advice from real match data
- [ ] Personality setting
- [ ] README demo; publish the model (`tools/nl-release.sh`) once the repo is public
