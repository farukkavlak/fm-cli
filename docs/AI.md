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

- [ ] T204 Players, clubs, offers held by the game's own ids (`Nl/Assistant.Names.cs`)
  - [x] Our players (youth too), our club, the next opponent
  - [x] Every club we play this season, from our own `Team.FixtureList` (no page; a league's whole field)
    - `Human.Team.Division.Teams` stays empty until a page binds it: not used
  - [x] Shortlist: its players and their clubs, kept when `shortlist` reads its page (`Nl/SeenNames.cs`), per club and save
    - The save is told by `app.LastSaveSummary.GameSummaryGameName`; another save of the club keeps its own list
  - [x] Offers: players and clubs of `TransferActivityPlayerListAll`, read with no page (seen with 11 bids on `window-bids`, TR)
  - [ ] Clubs as an argument: `friendly` picks from the game's own list of opponents, not the league's; left as typed
- [x] T205 Fuzzy names: Turkish endings, typos, a club before the name; ask when two fit (`Nl/NameFit.cs`)
  - The model sees our players as a name it was taught ("hreljaya bak" → "rinaldiye bak"); arguments keep the typed name
- [ ] T206 The router learns from the user (`Nl/Learned.cs`, `~/.config/fm-cli/learned.json`)
  - [x] Learns only what the user chose: a pick from the options, or a yes. Not what the router ran on its own.
  - [x] A choice is pending until the next command; when that command is another of the offered ones, it is forgotten.
    - Told apart by command name: `tactic` and `tactic slot` are one command here.
  - [x] Learns sentence → tool only; arguments are read again every time.
  - [x] A player picked between two who fit is kept by his id.
  - [ ] Nicknames for clubs ("cimbom" → the club's id): needs other clubs' ids (T204).
  - [ ] A choice the list did not offer (the user types the command after "none") is not learned.
  - [x] The user's sentences count twice; the nearest of all, from 0.9 similarity, decides.
  - [x] A weighty call still asks for a yes, however sure.
  - [x] `fm learned`: list and delete.
  - [x] Measured (`tools/nl-learn.py`, held): taught 0% → 100%, their noisy forms 9% → 76% top-1,
    sure-wrong on them 35% → 5%; the rest 86.2% → 85.5% top-1.
- [x] T207 Smart mode: router first, a cloud model when unsure (`Nl/Cloud.cs`, `fm ai`)
  - Any OpenAI-style provider: URL, key, model, extra headers (`~/.config/fm-cli/ai.json`, only the user reads it)
  - Asked only when the router is not sure; shown with the tools the 50 nearest examples point at (10)
  - Agrees with the router's pick: runs; asks a yes first when it changes the game. Another pick: offered first, marked `ai`
  - Its arguments: a name or number only when the sentence gives it; a choice only for a read; never confirm/send/sign
  - Picks none, fails or times out (20 s): the router's list as before
  - Measured (`tools/nl-cloud.py`, held, claude-haiku-5-5 via OpenCode Go), of the sentences the router is not sure of:
    - base 159: 45 run without a list (42 right); the cloud's pick first in the list 34 (17 right); median 1.1 s
    - noisy 393: 107 run without a list (91 right); first in the list 86 (45 right); median 1.3 s
    - gpt-6-luna and qwen3.8-flash about the same; deepseek-v4.1-flash gave no usable answer
- [ ] T208 Assistant, read-only: `fm ask <question>` (`Nl/Ask.cs`, `Nl/Chat.cs`)
  - [x] Provider interface (`IChatModel`); OpenAI-style chat with tools, the model from `fm ai set`
  - [x] Offered the read tools only (`Mcp.ReadTools`): Read tools, and tools that act only with some arguments, without them
  - [x] Each call checked again before it runs (`Mcp.RunRead`): an argument not shown or a call that could act is refused
  - [x] The commands that act are named in the prompt (not callable): asked to act, it names the command
  - [x] Talk kept per club and save for 2 h (`~/.cache/fm-cli/ask.json`); earlier answers are not facts: figures are read again
  - [x] Under the answer: what was read; a capitalised name no read, question or earlier turn holds is flagged
  - [ ] Anthropic's own API (no key to test with); OpenAI-style providers only for now
  - [x] Live: `tests/live/round79.toml` TR + EN (changes nothing, says what it read, follow-ups); `tools/ask-bench.py`
    - haiku-5-5, TR game: 20/20 answered, 2 with a name flagged, talks 8/8, median 16.7 s
    - Found and fixed: a yearly wage told as weekly; an earlier wrong answer taken as a fact; a player of another club
      taken as ours (`player` now shows the club)
- [ ] T209 Plans: steps, approval, run, verify
- [ ] T210 Save memory: facts, preferences, plans; list, fix, delete
- [ ] T211 Transfer and tactic advice from real match data
- [ ] T212 Personality setting
- [ ] T213 README demo
