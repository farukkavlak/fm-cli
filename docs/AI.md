# AI

Playing by sentence (`fm <sentence>`), and later an assistant.

## Rules

- A model only proposes. Our code checks, asks and runs.
- Facts come from the game. Say what was checked.
- Every feature is a command first.
- Local and free by default. A cloud model is the user's choice, with the user's key.
- Any provider: one interface (OpenAI-compatible and others); the user picks the model.
- The assistant has no name in the app.

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

## v2

Order: names first, then the learning router, then the cloud.

- [ ] T204 Players, clubs, offers held by the game's own ids
  - [x] Our players (youth too), our club, the next opponent (`Nl/Assistant.Names.cs`)
  - [ ] Other clubs: not readable without opening a page (`Human.Team.Division.Teams` is empty until one binds it); collect them from reads that carry ids
  - [ ] Offers, shortlist
- [x] T205 Fuzzy names: Turkish endings, typos, a club before the name; ask when two fit (`Nl/NameFit.cs`)
  - The model sees our players as a name it was taught ("hreljaya bak" → "rinaldiye bak"); arguments keep the typed name
- [ ] T206 The router learns from the user
  - Learns only what the user chose: a pick from the options, or a yes. Not what the router ran on its own.
  - A choice undone right away (another command at once) is forgotten.
  - Learns sentence → tool only; arguments are read again every time.
  - Learns nicknames for names ("cimbom" → the club's id), tied to T204.
  - The user's own sentences weigh more than the shipped examples.
  - A weighty call still asks for a yes, however sure.
  - `fm learned`: list and delete.
  - Measured: teach held misses as if picked, then ask their noisy forms.
- [ ] T207 Smart mode: router first, a cloud model when unsure; our code always asks for a missing value that changes the game
- [ ] T208 Assistant, read-only: provider interface, session memory ("him"), answers only from what was read
- [ ] T209 Plans: steps, approval, run, verify
- [ ] T210 Save memory: facts, preferences, plans; list, fix, delete
- [ ] T211 Transfer and tactic advice from real match data
- [ ] T212 Personality setting
- [ ] T213 README demo
