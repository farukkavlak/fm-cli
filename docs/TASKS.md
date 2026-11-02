# Tasks

Everything left to do, in one list. Pick by id ("T12"). Tick a box in the commit that does it.
Details live where the link says: `PORTING.md` (FM27 port notes), `reference/fm27/` (crawl data),
`BACKLOG.md` (ideas, not yet tasks).

Sources: the code scan for shown text and order (2 Nov), `fm crawl` TR + EN (`reference/fm27/crawl-*.json`),
match screens (`reference/fm27/tweaks.txt`), the FM26 → FM27 enum diff (`reference/fm27/new-in-fm27.txt`).
An event the code never names is not always a gap; each line was checked against the commands.

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
