# AI

Playing by sentence (`fm <sentence>`) and the assistant. The one list of open work: tick a box in the commit that does it; what was done is in git.

## Rules

- A model only proposes. Our code checks, asks and runs.
- Facts come from the game. Say what was checked.
- Every feature is a command first.
- Local and free by default. A cloud model is the user's choice, with the user's key.
- Any provider: one interface (OpenAI-compatible and others); the user picks the model.
- The assistant has no name in the app.

## Today

- Router: a fine-tuned multilingual embedding model (ONNX, 280 MB, ~5 ms), kNN vote, rules (`cli/fm/Nl/`, tests `tests/nl/`).
  - ≥80% sure: run it (ask first if it changes the game). 50–80%: offer 3. Below: offer 5.
  - Model installed by `fm doctor --install`. Local LLMs measured on this Mac (29 Oct): all failed, not used.
- Names held by the game's ids: our players, our club, the season's opponents, the shortlist, offers (`Nl/Assistant.Names.cs`).
- Fuzzy names: Turkish endings, typos, a club before the name; asks when two fit (`Nl/NameFit.cs`).
- Learns from the user: the command picked from the options or a yes, a player picked between two; `fm learned` (`Nl/Learned.cs`).
- Smart mode: a cloud model (any OpenAI-style provider, the user's key) asked only when the router is not sure (`Nl/Cloud.cs`, `fm ai`).
- `fm ask`: read-only assistant; each call checked again, says what it read, flags names no read holds (`Nl/Ask.cs`).
- `fm plan`: steps proposed by the model, checked by our code, run on a yes, each read back (`Nl/Plan.cs`).
- Notes per save, given to `ask` and `plan` as the user's word; `ask` offers one, kept only on a yes; a plan against a note names it (`Nl/Memory.cs`, `fm memory`, `fm remember`).
- Live tests: `tests/live/round79.toml` (ask), `round80.toml` (plan), `round81.toml` (notes).

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

## Free play by talking

Most of a season is the same loop: find players, buy, sell, set training, set the tactic, go on,
read and answer the news, play the match, decide in it, go on. That loop in plain sentences, with
no model and no key, is the goal. A cloud model (haiku …) stays an extra, never a need.

- The game leads, not a model: at each stop the tool says what the game waits for, in the game's
  language, and what can be answered. Stops are read by identity (events, data paths), never by text.
- At a stop the router picks among that stop's few answers (accept, refuse, ask for more, the 2nd one),
  not among all commands: far surer, local, ~5 ms.
- A question at any stop ("kim yorgun", "rakip kim") is answered and the stop waits.
- Anything weighty is shown and runs on a yes, as now.

### Stops (source read 25 Nov, for T214–T215)

`next` already finds every stop by identity (`Core/Clock.cs`: `Passable`, `Explain`); today it ends by naming a command.

| Stop | Told by | Answers | Today |
|---|---|---|---|
| Free to go on, a page to look at | `State` CanContinue or a `Passable` page | go on | `next` |
| A message only to read | NeedAction + `OnlyToRead`, `Messages`, `NextUnreadMessage` | open it | `next` |
| A message with answers | inbox item `NewsItemIsPriority` + `CanRespond` | the message's own answers | `reply` |
| An offer for our player | `Respond` 3 + an incoming offer that `CanAccept` | accept, reject, stall, negotiate | `offers` |
| Our bid accepted | `Respond` 3, no incoming offer | contract talks, hand over, walk away | `reply`, `contract` |
| Set piece wizard | `Respond` 14 + a visible `EventFmxSetPieceCreator` button | six questions' options | `setpieces new` |
| Club vision meeting | its controls drawn (`VisionControls`) | remove, suggest, confirm, end | `vision` |
| A conversation open | `TalkOptions` not empty | its numbered lines, leave | `talk` |
| Guided tour | `TourOpen` | walk, skip | `tour` |
| Press conference | `State` PressConference | attend, skip; then lines | `reply`, `talk` |
| Match day | `State` Tactics / SubmitTeam | how (instant, play), the XI, the tactic | `match` |
| Team news, team talk, tunnel interview | `State` TeamNews / TeamTalk / TunnelInterview | lines | `talk`, `match` |
| In a match | `DuringMatch` | shout, sub, tweak, tactic; half-time talk | `shout`, `sub`, `tweak` |
| A confirmation window | `/dialog` `Type` | its buttons | `dialog` |
| No club | `Human.Team` none, `Club` null reference | jobs | `jobs` |
| A step with a button and nothing else | NeedAction, none of the above | none yet | gap |

- `Respond` is `continue.ContinueButtonMustRespondGrouping`; its kinds are native (no managed enum found by sigdump). Known: 0, 1, 3, 14.
- Two kinds of answers:
  - Fixed: accept, reject, go on, instant/play. A small set per stop, the same in every save: local examples per stop.
  - The game's own lines: message answers, talk and press lines, wizard options. Picked by number or by the user's words
    held against the lines as the game shows them; the pick is sent by its index or id, never by the text.
- T214 shape: `Explain` gives a stop (kind, its id, subject, answers) instead of only a line of text; the session shows it
  and takes the next sentence as an answer to it first, a command or question second.

Order (before T211–T213). New work is added in this file, by id.

- [ ] T221 The game's own lines by words: a message's answers, talk and press lines picked by a sentence held against
  the lines as shown; sent by index or id
  - [x] Coded: a message opened and a conversation are a stop of lines in the session (Core/StopLines.cs);
    bench `fm __lines tests/nl/lines/{cases,held}-{tr,en}.tsv` (held TR 16/23, EN 28/30, no wrong line without a yes)
  - [x] round85 live: TR + EN 3/3 all three tests
  - [ ] Team talks and the tunnel interview in `match` are still picked with the arrows
- [ ] T223 Readable output: find the outputs that read better as a table or with dividers, then do them in one round
  - [ ] Offer stop: 13 offers as one line each (`Club → Player · fee`); a table (club, player, fee, terms) reads better
  - [ ] Session: after an answer the whole stop comes back in full (a rule with the stop's name now starts each stop)
  - [ ] "Did you mean" at a stop: aligned columns (the router's list is done)
  - [ ] Sweep the other commands' output for the same (list in this box first, then do them)
- [ ] T224 Just after a load the session opens on "a message waits" (the offers' messages); the offer stop comes
  only after `next` (scn-offer-stop, TR, 26 Nov). Find out if `status` should read the game's sign as `next` does
- [ ] T222 `offers <#> stall` once did not find the offer's row on the transfer activity page (TR, 1 of 12 tries, round84):
  measure what the page shows when it misses before changing the wait
- [ ] T216 Transfers by talking: find ("sol bek ara, 23 yaş altı, 2 milyona kadar"), shortlist, bid, haggle, sell
- [ ] T217 The week by talking: training, intensity, the tactic and roles, who plays
- [ ] T218 Match day by talking: the XI and tactic asked, the match played, shouts, subs and tweaks in it, half time and the end told
- [ ] T219 Measured: a month of a season played by sentences only, TR + EN; every stuck place a tool bug

## Open

- [ ] T204 Clubs as an argument: `friendly` picks from the game's own list of opponents, not the league's
- [ ] T206 Nicknames for clubs ("cimbom" → the club's id)
- [ ] T208 Anthropic's own API (no key to test with); OpenAI-style providers only for now
- [ ] T209 A plan sometimes has a step for what already holds (shown as "already so", harmless)
- [ ] T211 Transfer and tactic advice from real match data
- [ ] T212 Personality setting
- [ ] T213 README demo
