# Backlog

What is left, what is partial, and the ideas we do not want to lose. `[ ]` open, `[~]` partial,
`[?]` uncertain. FM27 porting has its own file: `docs/PORTING.md`.

## Product decisions

- UX before feature count. A command that works but is hard to read is not done.
- Show the pitch in the terminal; a 4-3-3 should look like a 4-3-3, and a change should show on it.
- `match` asks: watch it or take an instant result. Neither is the silent default.
- Auto-save is the user's setting, not ours.
- In a talk, the user picks the line; the command shows the options and lets them choose.
- No default that makes a choice for the user.

## Core

- [ ] Pages that only exist under a "More" menu still open by bookmark; their data may not bind.
- [ ] Materialise one property without opening a page (a channel opens, the node cannot be created).
- [ ] Cloud save state.
- [?] Career creation: the multi-step wizard (leagues, database size, manager profile).
- [?] Manager profile editing: name, birth date, nation, appearance.
- [ ] Skin/theme. Not useful for the CLI, but the API has it.

## Squad and players

- [~] Hidden attributes: the club's view of potential is read; personality and consistency are not
  in `/pull` (personality is in `dynamics`).
- [~] Internationals: nation, caps, goals and this season's games; no camp or invitation status.
- [ ] Swap two starters, or take a player from outside the squad (their names are not bound on the
  tactic screen).
- [~] Set pieces: takers and routines read and changed; no defensive scenario, no adding/saving a
  routine, no dragging between places.
- [ ] Youth training.
- [~] Player search: filters and attributes on the visible rows; a query over the whole database
  is missing.
- [~] Loans: card, terms, option to buy, sending works; answer and the rest of the loan flow are
  not complete.
- [~] Swap offers: the card can add a player; the full swap flow is not tested.
- [~] Cancel a scouting assignment: the game's own handler does not cancel it either.
- [ ] Season comparison and trends.
- [ ] `fm watch`: watch a player or a value and report when it changes (`Bindings.Bind`).

## Match

- [~] In-match changes: mentality, shout and role work; the rest is missing.
- [~] Live match tracking: commentary, goals and score; the rest of the match screen is not shown.
- [~] Match stats: possession, shots, on target, xG, corners, fouls; no passes or heat map.
- [~] Text highlight summary; no minute-by-minute list of key events.
- [?] Real-time in-match management (a different problem).
- [~] Live match screen: the panel shows the score, clock, period, a shape pitch and the last eight
  events; plain output stays for scripts. Still to do: live player/ball positions, a key-moments-only
  view, keys for substitution/shout/tactic/speed/view, an injury prompt, config through a file and
  `fm settings`, the pitch refreshing after substitutions, and the optional ASCII 3D view.
- [?] Live match stats: the bindings carry `LastFixture`/`NextFixture` only, and during a match
  `IsLiveMatch` is false, so shots/possession are not live. Read the match stats page or use the
  post-match report.
- [ ] Sparkline/condition trend (match ratings already have a bar; condition history is not kept).

## Competitions and club

- [~] European cups: league-phase table and qualifiers through `stages`; the whole path is not
  covered.
- [~] Season review and awards: read from messages; no `fm season` and no awards command.
- [~] Club value and debt: remaining and original debt; club value is only a debug field.
- [~] Job security: the board's view by topic; the meaning of `JobSecurityStatus` is unknown.
- [~] Player complaints: one flow works (discuss / decline / ask the agent or captain); other
  complaint types have not been seen.
- [~] Social media: the news site is read; social media is not.
- [~] Loaded leagues: read; no add/remove league.
- [?] Detail level and notification settings: not in FM26's preferences.

## Quality and reliability

- [~] `fm start` window: it no longer takes focus and is a 320×208 window at the top-left for about
  45 s, then it disappears. Hiding it earlier needs Accessibility (denied here). An `open -g -j`
  wrapper hid the menu, but loading a save showed full-size windows, so it was not kept.
- [~] REPL: a quoted phrase counts as one word.
- [?] One long MCP run ended with the game quitting; the next run passed all 48 reads. Watch for it.
- [ ] Remaining position-based reads and fixed waits. `tools/sweep.sh` and a new save show them.
- [~] An English game end to end: the sweep and one match were checked; a full season has not been
  played in English.
- [~] MCP: 48 read tools pass with the Python SDK (`tools/mcp-check.py`, 98 tools listed);
  write tools have not been exercised.

## Portability tests

- [ ] Another country and league: a new career, then `tools/sweep.sh`, then play.
- [ ] Another theme and interface scale: control names, tree depth, virtual row counts.
- [ ] Intel, Epic, Game Pass and Windows: unsupported for now.
- [ ] A clean machine: the package and install checks pass, but the second macOS user has no Steam,
  so the game has not been launched there.

## Natural language (later; local only)

- Goal: a sentence becomes the right command, nothing else. No cloud or API key by default.
  Writes show what will happen and ask first.
- Layers: exact command; rule matcher for common phrases; local router (embeddings/ONNX) over
  command categories and examples; cloud only if the user turns it on.
- Start: a 12-category command map, example sentences for the 20 most used commands, a small ONNX
  embedding test inside `fm`; measure accuracy and RAM.
- Examples: "who is tired?", "I need a left back, 10M budget", "who should rest this week?",
  "analyse the opponent and suggest a tactic", "strengthen the squad before the window closes".
- Voice: STT in front, MCP behind; TTS match commentary.
- Assistant-manager mode; an LLM handles the post-match press conference.

## Wild ideas

- [ ] A Discord/Slack bot: manage the team from chat.
- [ ] Play a season automatically; ask only at key decisions.
- [ ] Two fm-cli instances, LLMs head to head.
- [ ] Git integration: one commit per season, squad changes as a diff.
- [ ] An automatic career diary from match data.
- [ ] A web dashboard (the plugin already speaks HTTP).
- [ ] Mobile notification: "X is injured".
- [ ] Time-series database and a Grafana career dashboard.
- [ ] The same architecture for other Unity + IL2CPP games.
