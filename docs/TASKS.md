# Tasks

Everything left to do, in one list. Pick by id ("T12"). Tick a box in the commit that does it.
**The project is done (100%) when every box here is ticked**, the acceptance tests (section 11) last.
A task found along the way is added here before it is worked on.
Details live where the link says: `PORTING.md` (FM27 port notes), `reference/fm27/` (crawl data),
`BACKLOG.md` (ideas, not yet tasks).

Sources: the code scan for shown text and order (2 Nov), `fm crawl` TR + EN (`reference/fm27/crawl-*.json`),
match screens (`reference/fm27/tweaks.txt`), the FM26 → FM27 enum diff (`reference/fm27/new-in-fm27.txt`).
An event the code never names is not always a gap; each line was checked against the commands.

## 0. First, before anything else

- [ ] T0 The game never shows itself: `fm start` keeps it out of sight from launch to a loaded save and after,
  whatever the game's own display settings say. Today FM27's saved settings (full screen, native size,
  `com.SportsInteractive.FootballManager27` prefs) override `-screen-width 640` and the window covers the
  screen; a full-screen window cannot be minimised and, out of the Dock, not even switched away from.
  Without this the tool is unusable. Also: the crawl must not touch the display settings.
  - [x] start sets the game's display settings to a 1920×1080 window, stop puts the player's back
  - [x] the plugin makes the window transparent and click-through (`WindowHider`, `FMCLI_HIDDEN=1`)
  - [x] `tools/window-watch` proves it: 3 × start/squad/calendar/stop and 2 × next, never on screen
  - [ ] during a match (match screen, resolution changes), and the crawl kept off display settings
  - [ ] the player's display settings come back even when the game is not closed by `fm stop` (crash,
    force quit, Mac restart): put them back as soon as the game has read them at boot, and again at stop
  - [ ] a hidden game is never forgotten: `fm start` / `fm status` say when a hidden FM is running, and a
    second FM (opened from Steam) on the same save is warned about before two games write it

## 1. Held by text or order (the rule above all)

- [ ] T1 `tactic new` entry: "No Tactic Set" tile and "Create New Tactic" by label (`Match/Guided.TacticNew.cs`,
  `Match/Guided.cs:69`). The tactics page's `add-data` list gives "Create New Tactic" the value 1: choose by value.
  Once said "three slots taken" with the wizard already up; not reproduced.
- [ ] T2 `vision` (`Club/Vision.cs`): heading, weights, objective kinds, buttons by English words. Its record is
  known (`Club.ClubVisionMeetingEditSession`, PORTING). Needs the season-end meeting (Pendikspor, May 2028).
- [ ] T3 `talk` echoes the chosen line in English in a Turkish game ("» Finalise Deal").
- [ ] T4 `tour` prints the tour's text in English in a Turkish game.
- [ ] T5 Calendar (`Match/Calendar.cs`) and training week (`Squad/Schedule.cs`) read the venue from
  `MatchVenueShort` (shown letter). Use the teams' ids as `status` does.
- [ ] T6 `coaches`: the nine workload cells are read by order (`Club/Coaches.cs`).
- [ ] T7 `staff find` rules add "Position" / "Transfer Value" by choice name (`Club/StaffSearch.Rules.cs:157,187`).
- [ ] T8 `tactic`: the "Combined" view tab by label (`Match/Guided.Tactic.cs:308`).
- [ ] T86 Calendar / `fixtures --all` read a cup result's marks from the shown score ("u3-2" in Turkish,
  `^\d+-\d+(p|e)?$` in `Match/Calendar.cs`): extra time and penalties by the fixture's data, not its letters.
- [ ] T9 `--term` by a term's English name fails in a German game (PORTING).

## 2. New in FM27, no command

- [ ] T10 Tactical tweaks in a match: 15 team tweaks + Individual tab; `#tweaks-button` popup, `#tweak-row`,
  `#toggle-overlay`, Reset = `ResetTacticalTweaks` (`reference/fm27/tweaks.txt`).
- [ ] T11 TransferRoom (In, Out, Agent Offers): adverts by type and view, discard agents' offered players.
- [ ] T12 Squad Planner (planner, contracts, current best XI): add, move, remove, ask assistant, copy.
- [ ] T13 Tactic: personalised roles (`EventFmxTogglePositionPersonalisedRole`), swap two players
  (`EventFmxSwapPlayers`), clear a slot (`EventFmxTeamSelectionClearPlayer`).
- [ ] T14 Tactics advice options (`AdviceOptionsSelection`).
- [ ] T15 Training week card: copy / paste / reset a week, pick a session, confirm / cancel the card.
- [ ] T16 Analysis pages (read only): Data Hub, Tactical Effectiveness, Tactics Feedback, Team / Formation /
  Goal / Set Piece Analysis.
- [ ] T17 Recruitment Objectives and Recruitment Feedback pages.

## 3. Existing commands, missing a part

- [ ] T18 `captains`: Quick Pick, stand-ins.
- [ ] T19 `responsibilities`: delegate all to recommended, take control of all, undo / redo.
- [ ] T20 `coaches`: remove all assignments.
- [ ] T21 Opposition scouting priorities: prioritise / cancel.
- [ ] T22 `search`: quick filters, scouted only; `staff find`: exclude / own-staff filters.
- [ ] T23 `shortlist`: more than one shortlist.
- [ ] T24 `inbox`: mark all read; `news`: mark read.
- [ ] T25 `dugout`: next / previous / dismiss advice.
- [ ] T26 `match`: skip replay, pause.
- [ ] T27 `holiday`: the "apply for jobs" choice.
- [ ] T28 `fm youth candidates`: rows not seen yet (intake each March; PORTING).
- [ ] T29 After a goal with a change pending, the "Confirm Tactical Changes" dialog (PORTING).
- [ ] T30 Rename a tactic (the active one is "xxxx…" since a test; PORTING).
- [ ] T31 `scout player --cancel`: the game keeps the assignment (PORTING).
- [ ] T32 `training --trait-suggest`: FM27 has no such menu entry (PORTING).

## 4. Pages no command reads (read only, lower priority)

- [ ] T33 Club history: overview, records, honours, landmarks, manager history, all-time best XI.
- [ ] T34 Club profile: affiliates, facts, stats, club news.
- [ ] T35 Career: my profile, history, relationships, managerial stats.
- [ ] T36 Competition: past winners, awards, records, rules, structure, qualification places, coefficients,
  nation agreements, rankings, season preview, suspensions, injury table, manager movements, rumours,
  transfer window report.
- [ ] T37 Transfers: history, window, clauses. Scouting: reports, priorities, range, not interested.
- [ ] T38 Squad: discipline, loans, promises, squad feedback. Medical centre: risk assessment, recently returned,
  season summary, player summary.

## 5. Robustness

- [ ] T39 `fm crawl`: two panels leave a popup that holds the event queue (`--skip 7bab4b19…,cd095383…`); close
  such popups instead of skipping.
- [ ] T40 `fm stop`: the game once hung on its way out (process stuck exiting, not killable); cause unknown.
- [ ] T41 Changing the game's language with the game open leaves Portal pages unopenable until a restart:
  `settings language` should say so (or restart).
- [ ] T42 `status` takes two minutes when the UI is held (inbox `Bound` waits 20 s, several times).
- [ ] T43 Squad numbers before a match: the screen stays up a moment after they are sent (PORTING).

- [ ] T87 Playing FM by hand and through the tool on one install (the real install goes into Steam's own
  game folder; `~/fm27_bep` is only our test copy):
  - a game opened from Steam by hand loads BepInEx and the plugin too: it must stay visible and normal
    (no hiding, Dock icon kept: LSUIElement only for the tool's launch, not in Steam's Info.plist)
  - a Steam update replaces the game's files: the tool tells a changed game version, and puts BepInEx
    back or says how, instead of failing to start
  - a game left by hand on a dialog, a tutorial or another screen: every command first gets past it
    (`status` / `dialog` name it) instead of getting stuck
  - what the tool changes for good in the game (language, a tactic's name, preferences) is only what
    the player asked for; tests leave nothing behind

## 6. Plugin: FM27 API changes still open (PORTING "API")

- [ ] T44 `SI.Bindable.TableRowNavigatable` gone (`Controls/TableRows.cs`, `Reads/Registration.cs`).
- [ ] T45 `BindableTextEditBox.m_textField` moved to `BindableEditBox<T>` (`Controls/TextBoxes.cs`).
- [ ] T46 `Bindings.GetDescendantCount` gone (`Bridge/FmApi.cs`, status line only).
- [ ] T47 `IdentifierInfo.DisplayName` gone (`Bridge/Schema.cs`).
- [ ] T48 `EventFmxMatchFocusSelect*` gone (`Match/Prep.cs`); `EventFmxSelectMatchFocusType` remains.

## 7. Platform (macOS-only today)

- [ ] T49 `fm start` / `stop`: `/bin/sh` + `run_bepinex_arm64.sh`, the `fm27_bep/fm.app` bundle, `PlistBuddy`,
  `pgrep` / `pkill`, Steam's macOS path (`Core/Launcher.cs`). Windows has none of it.
- [ ] T50 Window hiding by `osascript` (`Launcher.Minimise/Hide/DropFromDock`); Windows needs its own.
- [ ] T51 `doctor` (`Core/InstallDoctor.cs`) still checks FM26 paths (`fm26_bep`, "Football Manager 26").
- [ ] T52 Publishing: `tools/install.sh` builds `osx-arm64` only.
- [ ] T53 Plugin on Windows x64: untried (Harmony would work there; arm64 has no MonoMod).
- [ ] T54 Save and user folders on Windows (`Core/GameFolders.cs` has the path; untried).

## 8. Moments the crawl has not seen (crawl them when they come: `fm crawl out.json --here`)

Each: crawl the screen, map it to a command, open a task for what is missing.

- [ ] T55 Season's end: club vision meeting, end-of-season awards, players whose contracts end, retirements.
- [ ] T56 Transfer window open and deadline day: incoming bids, the window's own screens.
- [ ] T57 Cup draws and the cup's own screens.
- [ ] T58 International break: call-ups, the national side's screens.
- [ ] T59 Youth intake (March): candidates, intake preview.
- [ ] T60 Board meeting and board requests answered; job offers while employed; being sacked.
- [ ] T61 Contract renewals: a player asking for a new deal, an unhappy player's demand.
- [ ] T62 Pre-season: friendlies, pre-season training, new-season expectations.
- [ ] T63 Match: substitution screen, FM27's shouts (or what replaced them), set pieces edited during a match,
  penalty shootout, extra time, injury during a match.
- [ ] T64 Press: every kind of press conference and media question (pre-match, post-match, transfer, unhappy player).

## 9. Depth the crawl has not reached

- [ ] T65 Player interaction: praise, criticise, promise, warn, talk about form or a transfer (no command today).
- [ ] T66 Team meeting started by the manager (the game's own; `talk` only answers one the game opened).
- [ ] T67 Every list's submenus (tactic presets, Manage Tactics, Primary Trained Tactic, Copy Tactic …):
  crawl with the lists opened one level down.
- [ ] T68 Every row's right-click / actions menu, for each kind of row (player, staff, club, competition, nation).
- [ ] T69 Game settings and preferences pages; the main menu (new career, load, online).
- [ ] T70 Club pages of a club that is not ours (what a rival's pages offer), a nation's and a competition's pages
  from the world view.

## 10. Quality, for everything above

- [ ] T71 Every new command has its MCP tool (`Core/Mcp.Tools.cs`) and help line (`HelpEnglish.cs`, labels).
- [ ] T72 Every new command is reachable from the natural-language layer (`cli/fm/Nl`), `tools/nl-check.py` passes.
- [ ] T73 `docs/MANUAL.md` images refreshed for every changed output (`tools/manual-shot.py`).
- [ ] T74 Full sweep passes after the last task: `tools/sweep.sh` (every read command) and `tools/mcp-check.py`,
  TR and EN.
- [ ] T75 `docs/AI-CHECKLIST.md` open items done or moved here.
- [ ] T76 `docs/BACKLOG.md` ideas decided: each made a task here or dropped.
- [ ] T77 `fm crawl` TR + EN again at the end; the report shows no event or page without a command (or a reason).

## 11. Acceptance tests (last; each is passed in one go, through the tool only, no bridge)

- [ ] T78 Two full seasons played only through the CLI (`fm …`), Turkish game: every match, message, talk,
  transfer window, season end and vision meeting. Every stop where the tool fails is a task, then the test restarts.
- [ ] T79 Two full seasons played only through MCP (`fm_*`), English game, same rule.
- [ ] T80 A third language (German or Spanish): one month through the CLI, the sweep passes.
- [ ] T81 Windows (Steam): install from the README, start, play one month, sweep, stop.
- [ ] T82 Epic Games Store copy (macOS and Windows): install, start, play one month, stop.
- [ ] T83 A fresh install on a clean machine from the README alone (`tools/install.sh`, plugin, first start).
- [ ] T84 A new career started through the tool (or the documented manual step), then one month played.

## 12. Final check (after section 11; the last box)

- [ ] T85 A full audit of `cli/fm` and the plugin for anything held by other than identity, the rule above all
  (CLAUDE.md). Every hit is fixed or written here with why it cannot be:
  - shown text, translated or not: labels, titles, choice names, dialog words, `Contains`/`==` on text
  - language: English (or any one language's) words, date and number formats, currency
  - place: pixels, coordinates, screen or button positions, sizes, scrolling by distance
  - look: colours, style classes, icons, fonts
  - order: `[i]`, `[^1]`, `First()`/`Last()` on screen lists, "the text after the heading", row numbers
  - one of a kind: one club, league, player, save, platform or store
  - time: fixed waits (`Task.Delay`, `sleep`) used as the mechanism, not as an upper bound
  Done when the audit finds nothing new, read by a second pass (another person or agent) as well.
