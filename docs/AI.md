# AI

Playing by sentence (`fm <sentence>`), and later an assistant (Mahmut).

## Rules

- A model only proposes. Our code checks, asks and runs.
- Facts come from the game. Say what was checked.
- Every feature is a command first.
- Local and free by default. A cloud model is the user's choice, with the user's key.

## Today

- A router picks the tool: a fine-tuned multilingual embedding model (ONNX, 280 MB, ~5 ms), kNN vote, rules.
- ≥80% sure: run it (ask first if it changes the game). 50–80%: offer 3. Below: offer 5.
- Code: `cli/fm/Nl/`. Tests: `tests/nl/`. Model installed by `fm doctor --install`.
- Local LLMs were measured on this Mac (29 Oct): all failed. Not used.

## Safety

- Every tool is marked next to its definition (`cli/fm/Core/Mcp.Tools.cs`), with the arguments that bring it about:
  - Read: changes nothing
  - Change: can be set back (tactic, training, settings, lists)
  - Play: moves the game on (Continue, a match, a word in a talk); not asked for
  - Weighty: cannot be taken back (money, contracts, people, saves and files, the job)
- One table for all three ways in:
  - CLI: a weighty command acts only on its own yes (`--yes`, `--send`, `--sign`) or the argument that is the act
  - MCP: hosts get `readOnlyHint` / `destructiveHint`; a call missing a required argument is refused, nothing run
  - Sentences: a weighty call is shown and runs only on a yes
- A chain of commands that stops part way says what was done and what was not.
- Bench: `tests/FmCli.Tests/SafetyTests.cs`; live: `tests/live/round68.toml` (read-only tools change nothing).

## Later (v2)

- [ ] Players, clubs, offers from the save with stable ids
- [ ] Fuzzy names: Turkish endings, typos ("frosinonenin rinaldi"); ask when two fit
- [ ] Mahmut, read-only: provider interface, session memory ("him"), answers only from what was read
- [ ] Plans: steps, approval, run, verify
- [ ] Save memory: facts, preferences, plans; list, fix, delete
- [ ] Transfer and tactic advice from real match data
- [ ] Personality setting
- [ ] README demo; publish the model (`tools/nl-release.sh`) once the repo is public
