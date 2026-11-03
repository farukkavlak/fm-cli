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

- [x] T0 The game never shows itself: `fm start` keeps it out of sight from launch to a loaded save and after,
  whatever the game's own display settings say. Today FM27's saved settings (full screen, native size,
  `com.SportsInteractive.FootballManager27` prefs) override `-screen-width 640` and the window covers the
  screen; a full-screen window cannot be minimised and, out of the Dock, not even switched away from.
  Without this the tool is unusable. Also: the crawl must not touch the display settings.
  - [x] start sets the game's display settings to a 1920×1080 window, stop puts the player's back
  - [x] the plugin makes the window transparent and click-through (`WindowHider`, `FMCLI_HIDDEN=1`)
  - [x] `tools/window-watch` proves it: 3 × start/squad/calendar/stop and 2 × next, never on screen
  - [x] during a match: a whole match watched through the tool (talks, half-time, stoppage time), never on screen
  - [x] the crawl kept off the display settings: it never reaches the preferences pages (checked in the
    crawl data); when they are crawled (T69), their display controls are left out
  - [x] a second FM opened by hand while the tool's game runs is closed before it draws, with a macOS
    note (plugin `SecondCopyGuard`; Faruk's choice: block the hand-opened one). Measured 3 of 3, no window.
    The game itself does not stop a second copy (Steam's FM came up beside the tool's game 2 of 3).
  - [x] `fm start` refuses beside a game opened by hand (`Launcher.OpenByHand`): 3 of 3; then 3 clean
    start/stop rounds after a Mac restart, never on screen
  - Known: a full-screen setting written while the hidden game boots puts it on a black full-screen
    Space (seen twice in tests); undoing it from the plugin did not work. Only a deliberate change can
    cause it: start writes a window before launch and keeps it until stop.

## 1. Held by text or order (the rule above all)

- [x] T1 `tactic new` entry: the add list (`add-data`) by its row's value 1, the slots-full case by
  `TacticCount`, the tour hint by `TacticCount` 0. TR + EN, 3 runs each: wizard opens, a full build lands
  in a free slot, slots full says so (the game lists the row and does nothing).
  - [ ] The no-tactic tile (`layout-tile-interaction-button` opening the tactics page, only when the team
    has no tactic) is not measured: it shows only in a new job. Measure it at the next new job.
- [x] T2 `vision` (`Club/Vision.cs`): buttons by their events, objectives by their rows' data path, kind by
  `#ActionableSection`, required by `ClubVisionMeetingCanObjectiveBeRemoved`, weight by `#ImportanceText`, a
  row's remove button by its row. TR + EN: read, remove + undo 3 runs each, two removed + reset, the
  negatives (a required one, suggest with nothing removed). Suggest prints the board's answer (its speech
  bubbles' rows); confirm and end read back (confirm closes, the meeting goes). Suggest + confirm TR, confirm + end TR + EN.
  `club` objectives: the tab clicked by `within=TabButtons&name=Vision` (`click?name=` was never a route). TR + EN, 3 runs.
- [ ] T93 Vision, new in FM27: an objective's weight stepper (`SIStepper` beside `#ImportanceText`), add an
  objective (`EventFmxAddClubVisionObjective`, `ClubVisionMeetingPossibleObjectivesToAdd`), the 5 Year Plan tab.
- [ ] T3 `talk` echoes the chosen line in English in a Turkish game ("» Finalise Deal"). Fixed: speech bubbles are
  read in the game's words (`Report.Spoken`), match talks show answers by `GameWords.Of`. TR measured (a whole
  season-end team meeting); EN still to see in one talk.
- [ ] T4 `tour` prints the tour's text in English in a Turkish game.
- [x] T5 Calendar (`Match/Calendar.cs`) and training week (`Squad/Schedule.cs`) read the venue from
  `MatchVenueShort` (shown letter). Use the teams' ids as `status` does. Done (db727ca): home and away right in TR and EN, calendar and week, 3 runs each.
- [x] T6 `coaches`: a cell's area by the record its switch writes (form `Of`: `AttackingTacticalCategory` …), load and
  lead rating by the page's record (`coachareas`: `CoachWorkload`, `LeadCoachRating`), stars by a control showing the same value.
  TR + EN: read, assign + remove 3 runs each, a coach the job forbids refused, from another page, `--auto`.
- [x] T7 `players` search adds Position / Transfer Value by the row's code (`ppos`, `pval`); the value list by
  `cash-list-frame`, its figures from the game (any currency), read back by the kept amount.
- [x] T8 `tactic`: the pitch view is put back by the tab's element name (`#overview` in `#sivisible-tabs`).
- [ ] T86 Calendar / `fixtures --all` read a cup result's marks from the shown score ("u3-2" in Turkish,
  `^\d+-\d+(p|e)?$` in `Match/Calendar.cs`): extra time and penalties by the fixture's data, not its letters.
- [x] T89 `tactic copy`: the copy list is the add list's one row with a submenu, chosen by its path of indices
  (`select?path=2,0`), full slots by `TacticCount`. With no tactic named it lists them (it used to copy the only
  one unasked). TR + EN: copy each source, three slots full refused, unknown name refused; paths checked by dry run.
- [ ] T9 `--term` by a term's English name fails in a German game (PORTING).

## 2. New in FM27, no command

- [x] T10 Tactical tweaks in a match, team tab: `fm tweak` / `fm_tweak` (list, on, `--off`), by
  `TacticalTweak` id; works once kicked off (a pre-kick-off tweak is dropped by the game).
- [ ] T90 Tactical tweaks, rest: the Individual tab (`TargetPlayer`, `TacticalTweakIndividualOptions`) and
  Reset (`ResetTacticalTweaksEvent`); extra time not measured; refresh `docs/MANUAL.md` with a `tweak` image
  at the next match.
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

- [x] T18 `captains --standins [quick | add | remove | clear]`: the stand-ins list (`StandInTable`, by its remove
  event), rows by their record (`binding.Player`, `Index`), the page's own squad table by `SquadCaptains`.
  TR + EN: 3 rounds of quick / remove / add / clear; captain, unknown, not a stand-in, already one, two names.
- [x] T19 `responsibilities delegate all | take all "<group or area>"`: the group's own buttons, told by the group record
  they sit on (`responsibilitygroup.ResponsibilityGroupID` + team type), then confirmed and read back. EN: an area
  (media) 3 × both ways, one group, nothing to do, unknown name; TR 3 rounds. Undo / redo not needed: every change is
  confirmed at once, and a failed one is undone with Reset.
- [x] T20 `coaches --clear` (`EventFmxClearCoaches`), read back; TR + EN 3 runs with `--auto` between.
- [ ] T21 Opposition scouting priorities: prioritise / cancel.
- [x] T22 `players --filter` by the quick list's own values (`SelectedQuickFilter`; `--free` is value 4 and failed in
  Turkish before), `--scouted / --unscouted` (`EventFmxFilterSetScoutedOnly`); `staff find --own / --no-own`
  (`EventFmxFilterSetIncludeOwnStaff`), its `--filter` on its own list (`SelectedExcludeOption`; it took the player
  search's list left open underneath). Players and staff TR + EN 3 ×.
- [x] T23 `shortlist --lists | --list "<name>" | --new "<name>"`: the selector `human.humanselectedshortlist`, each list's
  record (`DisplayString`, `CountString`; the one shown is the selector's own choice), a new one by `create-shortlist-button`
  and `EventFmxCreateShortlist`. `shortlist add … --to "<list>"` picks the list's folder in the menu. EN: made one, add /
  show / remove 3 ×, unknown list, the name taken; TR add / remove 3 ×. Delete / rename a list: not yet.
- [x] T24 `inbox --read-all` (`EventFmxMarkAllHumanMessagesAsRead`), read back by `NewsItemHasBeenRead`; TR 33 → 0,
  then "already read". `news <#>` marks a story read already: its tile sends `MarkMediaNewsAsRead` with `RunNewsAction`.
  EN 9 → 0, then "already read". The `--tasks` filter's fixed 500 ms wait is now "the list reads differently".
- [ ] T25 `dugout`: next / previous / dismiss advice.
- [ ] T26 `match`: skip replay, pause.
- [x] T27 `holiday … --jobs <kind> | --no-jobs`: the switch `holiday.applyforjobs` and the list
  `holiday.applyforjobsdropdownselectedoption` by its own values (−1 is the prompt), or by the shown words. `--dry` sets the
  page and leaves by its CancelButton. TR: 3 × two kinds, an unknown kind, a real week with `--no-jobs`; EN 3 × and a kind by the game's words.
- [ ] T28 `fm youth candidates`: rows not seen yet (intake each March; PORTING).
- [ ] T29 After a goal with a change pending, the "Confirm Tactical Changes" dialog (PORTING).
- [x] T30 The active tactic's name is right again ("pendik-yedek-t1", from `tactic load`). FM27's tactic menus have no
  rename (Edit Tactic and Manage Tactics open nothing to type in, TR); a tactic is named by `tactic save "<name>"`, then `tactic load`.
- [ ] T31 `scout player --cancel`: the game keeps the assignment (PORTING). Measured TR: the entry is Report › Cancel
  Assignments (`PersonStopScoutPlayersClub`), its question `Confirm1198281291` has a Yes with no event; answered by the
  dialog handler it closes, and the player's `IsBeingScouted` stays True (also after `next`). Read back now by
  `IsBeingScouted` (the person kept at `fmcli.scoutcancel`). Next: Scouting › Assignments (`ScoutingCentreActions`).
- [ ] T32 `training --trait-suggest`: FM27 has no such menu entry (PORTING).

## 4. Pages no command reads (read only, lower priority)

- [ ] T33 Club history: overview, records, honours, landmarks, manager history, all-time best XI.
- [ ] T34 Club profile: affiliates, facts, stats, club news.
- [ ] T35 Career: my profile, history, relationships, managerial stats.
  - [x] Managerial stats: `fm manager` reads the page's `Person.*` paths (TR, 3 × from another page; the figures are
    the page's own, `Human` does not bind most of them). EN checked.
- [ ] T36 Competition: past winners, awards, records, rules, structure, qualification places, coefficients,
  nation agreements, rankings, season preview, suspensions, injury table, manager movements, rumours,
  transfer window report.
- [ ] T37 Transfers: history, window, clauses. Scouting: reports, priorities, range, not interested.
- [ ] T38 Squad: discipline, loans, promises, squad feedback. Medical centre: risk assessment, recently returned,
  season summary, player summary.

## 5. Robustness

- [ ] T39 `fm crawl`: two panels leave a popup that holds the event queue (`--skip 7bab4b19…,cd095383…`); close
  such popups instead of skipping.
- [ ] T40 `fm stop`: the game hangs on its way out now and then (process stuck exiting, state UE, not killable,
  its window kept until a restart); cause unknown. Seen again 2 Nov after `stop --save` straight after a
  match with a press question open. `stop` no longer counts such a process as running.
  Same evening even a killed `/bin/sleep` stuck exiting (UE): the Mac, not the game; check after a restart.
- [x] T41 `settings language` says the portal's pages do not open until the game is opened again (save, stop,
  start); the language setting is told by its pref `current_language`, not by the CLI's word for it.
- [ ] T42 `status` takes two minutes when the UI is held (inbox `Bound` waits 20 s, several times).
- [ ] T43 Squad numbers before a match: the screen stays up a moment after they are sent (PORTING).

- [x] T91 `fm start` hung at the main menu when a dead game (UE) still held the port the file named: the menu check
  asked `health` with the long timeout. Now it asks briefly first (`Answers`, 3 s) and the port file is read on every
  call. Measured with 7777 held by a dead game: 2 normal starts and one with the file set to 7777, 75–78 s each.
- [ ] T92 The game crashed once leaving a match (after the post-match talk, 15 Apr 2028, tweaks used in that
  match with a wrong bound path); not seen again in 4 matches. Watch for it.
- [x] T88 `tactic clear`: the delete question (`deleteTacticConfirmation`) was answered by a simulated click,
  which often closed nothing; now by the game's dialog handler (`dialog?press=primary`). `tactic slot` lists
  every slot's name from the tactic's own `TacticSlot{n}Name` / `…Valid`. Copy then clear: TR 6/6, EN 3/3;
  an inactive slot, three slots, only one tactic (refused).
- [x] T94 `tactic slot <n>`: the switch button by the slot its event sends (`Sends`, 0-based), not its shown
  number. TR + EN: 1↔2 and 1/3/2 three runs each, another page open, slot 4 / "x" refused, clearing an inactive slot.
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
- [ ] T69 Game settings and preferences pages; the main menu (new career, load, online). Leave the
  display controls (window mode, resolution) unpressed, and block them in the tool: full screen while
  hidden is a black screen.
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
  - Known: comparisons against the CLI's own labels that work in any language but break the rule:
    `Squad/Registration.cs` (menu actions), `Transfer/Contracts.cs` ("After tax", "Wage"), `Match/Standings.cs` (form W/L).
