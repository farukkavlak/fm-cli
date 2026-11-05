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

How to work through this list: "Work in rounds" in `CLAUDE.md`.

- [x] T106 `captains --captain / --vice`: lists told by their element (`ChangeCaptain`, `ChangeViceCaptain`), Yes by the game's dialog handler, result read back. TR + EN, list `round1`: unknown name refused 3 ×, captain 3 ×, vice there and back 3 ×.
- [x] T107 A line that starts with the command it means is that command, every word kept: `dugout 1 zzz` says 'zzz' not understood. TR + EN in a match.
- [ ] T104 Scenario saves, each one load away: `tools/fm-round.py scenarios`, kept in `~/fm27_bep/scenarios`. Entering one takes about 85 s,
  a language switch about 10 s more; a simulated match 15 s. Kept: `calm`, `cup`, `cup-draw`, `kickoff`, `window`, `season-lists`, `sacked`, `interview`, `job-offer`, `newjob`, `spring`.
  - [ ] Season's end. Not reached: Pendikspor's board sacked the manager on holiday, Varzim (taken last in March) lost him at its last match, 5 May, both ways of going forward. Needs a club that is safe by May, from `interview`.
- [x] T105 One test tool: `tools/fm-round.py`, lists in `tests/live/*.toml`. `dry` finds a broken list without the game's time (scenario, step shape, command, flag,
  read path); `run` goes EN then TR and checks only `--json` and exit codes.
- [x] T109 `load` opened the last save whatever it was asked and said it loaded the named one: the main menu's tile sends `LoadSelectedGame` too. The list's
  button is told by also sending `ClosePanel`; the save's name is read back from `app.LastSaveSummary`. EN 3 scenarios, TR 1, unknown name refused.
- [x] T110 A game update stopped `fm start`: files left beside the launch binary made its signing fail. They are removed first. Update to 27.0.0.2493426 passed, the plugin rebuilt.
  - [x] `start` says at once when the launch failed, and carries an update through itself: the generating launch, then the real one. 99 s with the interop made out of date by hand.
- [x] T108 After a simulated match `status` kept no date (`game.CurrentGameDate` not bound for a while). The plugin keeps the day it last read while a flow state is there. TR + EN, 2 × each.
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
  - [x] The no-tactic case in a new job (scenario `newjob`): the wizard is waited for after the tactics page opens, and the slots-full message is said only for full slots. EN 3 ×, TR 3 ×.
- [x] T2 `vision` (`Club/Vision.cs`): buttons by their events, objectives by their rows' data path, kind by
  `#ActionableSection`, required by `ClubVisionMeetingCanObjectiveBeRemoved`, weight by `#ImportanceText`, a
  row's remove button by its row. TR + EN: read, remove + undo 3 runs each, two removed + reset, the
  negatives (a required one, suggest with nothing removed). Suggest prints the board's answer (its speech
  bubbles' rows); confirm and end read back (confirm closes, the meeting goes). Suggest + confirm TR, confirm + end TR + EN.
  `club` objectives: the tab clicked by `within=TabButtons&name=Vision` (`click?name=` was never a route). TR + EN, 3 runs.
- [ ] T93 Vision, new in FM27: an objective's weight stepper (`SIStepper` beside `#ImportanceText`), add an
  objective (`EventFmxAddClubVisionObjective`, `ClubVisionMeetingPossibleObjectivesToAdd`), the 5 Year Plan tab.
- [x] T3 `talk` echoes the chosen line in English in a Turkish game ("» Finalise Deal"). Fixed: speech bubbles are
  read in the game's words (`Report.Spoken`), match talks show answers by `GameWords.Of`. TR measured (a whole
  season-end team meeting); EN a player's transfer talk with a fee window.
- [x] T4 `tour` in the game's words, its buttons by the game's data: the way out by `IsExitButton` on its action, the way on by the button that moves the step up, the last step's by the exit. EN and TR: a whole tour 2 ×, skip, nothing open refused, a step that waits for the player.
- [x] T5 Calendar (`Match/Calendar.cs`) and training week (`Squad/Schedule.cs`) read the venue from
  `MatchVenueShort` (shown letter). Use the teams' ids as `status` does. Done (db727ca): home and away right in TR and EN, calendar and week, 3 runs each.
- [x] T6 `coaches`: a cell's area by the record its switch writes (form `Of`: `AttackingTacticalCategory` …), load and
  lead rating by the page's record (`coachareas`: `CoachWorkload`, `LeadCoachRating`), stars by a control showing the same value.
  TR + EN: read, assign + remove 3 runs each, a coach the job forbids refused, from another page, `--auto`.
- [x] T7 `players` search adds Position / Transfer Value by the row's code (`ppos`, `pval`); the value list by
  `cash-list-frame`, its figures from the game (any currency), read back by the kept amount.
- [x] T8 `tactic`: the pitch view is put back by the tab's element name (`#overview` in `#sivisible-tabs`).
- [ ] T86 Cup results by the game's numbers, not the shown letters ("u3-2", "p"). `stages` reads the round's own list (`Stage.CupTies` under the page's GUID, new plugin route `pullbound`): whole, no scrolling, no portal tile's ties mixed in, divisions back. Result codes: 1 win, 2 won in extra time, 3 won on penalties, 7 won a leg of a two-legged tie, 9 draw, 10 defeat. `calendar`, `fixtures`, `fixtures --all`, `report`: score, level with a winner is a shootout, code 2 is extra time. Croky Cup, every round, TR and EN agree: 154 ties, 13 penalties, 7 extra time. List `round3`, 3 ×.
  - [ ] Our own match settled by extra time or penalties: not met in simulated cup ties yet (we win them). `ResultType` on another club's fixture list reads empty, so code 2 on a `Team` fixture is not measured.
- [x] T111 `stages`: after a competition with no rounds the page has no stage list and every `stages` said "did not open". The competition list is held on its own; the competition says it has no rounds. TR + EN 3 ×.
- [x] T112 `report`: goals short of the score. One scorer's goals are one entry ("22', 31', pen 76'"), and only the last minute was kept. The game's text is now shown whole, nothing read inside it; `--json` has `HomeGoals`, `AwayGoals`. 13 played fixtures read against their scores; TR + EN list `round4`.
- [x] T89 `tactic copy`: the copy list is the add list's one row with a submenu, chosen by its path of indices
  (`select?path=2,0`), full slots by `TacticCount`. With no tactic named it lists them (it used to copy the only
  one unasked). TR + EN: copy each source, three slots full refused, unknown name refused; paths checked by dry run.
- [ ] T9 `--term` by a term's English name fails in a German game (PORTING).

## 2. New in FM27, no command

- [x] T10 Tactical tweaks in a match, team tab: `fm tweak` / `fm_tweak` (list, on, `--off`), by
  `TacticalTweak` id; works once kicked off (a pre-kick-off tweak is dropped by the game).
- [ ] T90 Tactical tweaks, rest:
  - `tweak --player <p>` and `tweak --reset team|individual|all` go through the match bar's tweaks popup
    (`tweaks-button`), no tactics panel; the popup is shut again. A player's place is his row's `Slot.Index`. While the
    ball rolls the game makes a change by itself, at a stop when play goes on; with the panel up, at its Confirm.
  - A player's list is his row's own `PlayerTweaksList`, bound once the row is expanded (`expand-toggle`, told by its
    `Source`). Greyed out is the game's `IsTweakDisabled` on the values the row binds. Checked against the screen, TR:
    goalkeeper 3 of 6, Stick to the Plan greyed for outfield players; a greyed one refused.
  - `TacticPendingTweakOps` is a log: a tweak's last entry is what it will be. The game's Reset leaves a waiting change, so
    `--reset` takes those back. TR, paused: team, individual, all, and each scope leaves the other; after play went on, none came on.
  - Measured EN and TR: list cold, repeated, another player, unknown player, a man sent off; on / off / already on.
  - [ ] Extra time.
- [ ] T99 `next` said "offer or half-way message" after a teammate failed to talk Atakan round, 16 Aug 2028: the answered
  message offered its choices again but was not named, though the same case with Utku was. `AllInbox` is now looked at too; not met again.
- [ ] T11 TransferRoom: `fm transferroom`. Adverts (transfer, loan, every row), offer a player, requirements (new, read, delete), agents' players (read, discard). TR + EN 3 × with the negatives (`tests/live/round2.toml`).
  - A player whose question has two answers: `offer` leaves the talk open for the player to answer.
  - [ ] Left: `responses` and `activity --loan` hold their tables by names not seen yet (`responses-transfers-table`, `responses-loans-table`, `activity-loans-table`); no such rows in the scenario. Offer through a list of several players: measured by hand once, EN only. `discard --all` and a wrong word to `--time`, `--type`, `--oop-role`: TR + EN (list `round5`). A real value through `--oop-position`, `--oop-role`, `--role`, `--time`, `--type`: not run yet.
- [ ] T12 Squad Planner: `fm planner`. Seasons, positions (new plugin route `pitchtiles`, a tile by its element name), add, move, remove, suggest. TR + EN 3 × with the negatives. Best XI and contracts: `fm read best-xi`, `planner-contracts`.
  - [ ] Left: copy a plan (EventFmxSquadPlannerCopy). The list's entry sent by `select` and by a row's submit changes nothing in the other season, so there is no command. Next: what a click sends that these do not; the rows' ticks (`SelectedPlayers`).
- [ ] T13 Tactic: personalised roles (`EventFmxTogglePositionPersonalisedRole`), swap two players
  (`EventFmxSwapPlayers`), clear a slot (`EventFmxTeamSelectionClearPlayer`).
  - [x] Swap two starters: `tactic player <position> <starter>` already does it (the two change places). Clear a place: `tactic player <position> --clear`, the place's `XIButton` told by its row's `PlayerIndex`, its right-click list sends `EventFmxTeamSelectionClearOrPickPlayerAuto`, read back by the place's `PlayerIndex` -1. TR + EN 3 × (list `round5`): wrong position, only that place, already empty, filled again, from another page.
  - [ ] Personalised roles, the rest of this task:
  Found: a row's `XIButton` sends the player's index, so a row is held by `Slots.PlayerIndex`; the toggle sits in the same `Items.<n>`; pressing it lights the row (seen by /shot). No data path found to read it back yet: not written.
  Measured 5 Nov (11 toggles on the page): a whole-tree diff (depth 6) moves only `CanUndo`. The button's parent is an `SIStyleSetter` beside `#PersonalisedInstructionsTooltip`. Next: the path that setter is bound to (a read of the setter's own binding); no style or colour is read.
- [ ] T14 `tactic advice [evaluation | xi | opposition]`: the planner's Advice list by its event, choices by `FM.UI.AdviceOption`.
  Evaluation reads the panel's `CritiquesList` and closes it; xi is a conversation, `talk 1` changes the eleven (read back);
  opposition is `opponent`. TR and EN: 3 × evaluation, xi, opposition, wrong word. Left: upcoming match (2) and Data Hub (4) open
  pages no command reads (T16); a real critique line (only "no advice" seen).
- [x] T15 Training week card: `schedule copy | paste | clear [this|<weeks ahead>]` by the card's own events, left by Confirm or Cancel, read back from the week's sessions; a session by `schedule set`. TR + EN: copy, paste, clear 2 ×, nothing copied, nothing to clear, a week the card does not offer.
- [x] T16 Analysis pages (read only): Data Hub, Tactical Effectiveness, Tactics Feedback, Team / Formation /
  `fm read <page>`: the page by its GUID, its texts by `/shown` (in the body on top, by the game's hit test), tables by row. TR + EN, a list of 58 tests (`tests/live/read-pages.toml`), plus 3 repeats.
  Goal / Set Piece Analysis. Not found yet: how to read one page's texts. `texts?within=<page guid>` finds nothing;
  `within=MenuBody` gives the portal left open underneath, whichever page is up, even keeping only `Pages.Shown` ones (EN, 3 pages).
  Next: each page's own data paths in `/tree`, as `opponent` holds `Team.Description`.
- [x] T17 Recruitment Objectives and Recruitment Feedback pages.
  `fm read recruitment-objectives`, `recruitment-feedback`.

## 3. Existing commands, missing a part

- [x] T18 `captains --standins [quick | add | remove | clear]`: the stand-ins list (`StandInTable`, by its remove
  event), rows by their record (`binding.Player`, `Index`), the page's own squad table by `SquadCaptains`.
  TR + EN: 3 rounds of quick / remove / add / clear; captain, unknown, not a stand-in, already one, two names.
- [x] T19 `responsibilities delegate all | take all "<group or area>"`: the group's own buttons, told by the group record
  they sit on (`responsibilitygroup.ResponsibilityGroupID` + team type), then confirmed and read back. EN: an area
  (media) 3 × both ways, one group, nothing to do, unknown name; TR 3 rounds. Undo / redo not needed: every change is
  confirmed at once, and a failed one is undone with Reset.
- [x] T20 `coaches --clear` (`EventFmxClearCoaches`), read back; TR + EN 3 runs with `--auto` between.
- [ ] T21 Opposition scouting priorities: prioritise / cancel. Tactics › Oppo. › Team Report Priorities
  (`6e8ea46097c185d468f2723d4931e63b`, opened with `open?guid=&bar=1` from Oppo.). Buttons `EventFmxPrioritiseScoutingTeam`,
  `EventFmxCancelScoutingTeam`, list `scout`. TR, 30 Aug 2028: the list is empty and both are off; needs a game with assignments.
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
- [ ] T25 `dugout`: a card is told by `MatchDugoutSelectedEventType` (4 is a score update, left out), "can apply" by
  `MatchDugoutIsSelectedEventActionable`, its place by `MatchDugoutSelectedEventIndex` with two readings agreeing; apply is `DugoutAdviceAccept`
  (it never matched in FM27 before). EN and TR, paused and rolling: apply, dismiss, wrong number, nothing to apply; no score card listed.
  Next / previous not needed: `dugout <no>` jumps. `match` prints every new card.
  - What apply does: a tactical tweak on the match's tactic (the button's `DugoutAdviceAcceptEventContext` carries its id), not a change of mentality or instructions. Pending at once, on after play goes on; `dugout apply` (the newest card that can be) says which. Measured: yellow-card card → Go Narrower (EN paused). List `round4`: TR 1 of 1, EN 0 of 3 (no apply with a tweak seen in the whole match; why is not known, as the test keeps no failed try).
  - [ ] Next round: tried at the highest speeds (10x / 40x) in one match each: no card taken. The panel stays hidden, and in both languages `match` stopped at an injury that needs a substitute (scenario `kickoff`, 46'), so the list must answer with `sub`, or play at normal speed. Normal speed, first half (list `round5`): TR no card taken in 7 minutes, the panel away for most calls; EN the game died at team submission (T92). Next: a way to apply that does not need the panel up, or a scenario set just before a card.
  - The panel is hidden during a move, even paused: `dugout` waits while the ball rolls and says so when paused.
  - While the ball rolls the list can move between two calls, so a number can mean another card (T101 removes the need for numbers).
- [x] T26 `pause` / `resume` (MatchTogglePlayPause, read back by Playing): TR and EN, both already-states; no play button (a
  talk) says `match`; at the game's own stop the button does nothing and it says `match`. `match` after `pause` plays on. `skip`
  (MatchSkipReplay): only an enabled button counts. EN and TR: a replay skipped once each, no replay says so.
- [x] T27 `holiday … --jobs <kind> | --no-jobs`: the switch `holiday.applyforjobs` and the list
  `holiday.applyforjobsdropdownselectedoption` by its own values (−1 is the prompt), or by the shown words. `--dry` sets the
  page and leaves by its CancelButton. TR: 3 × two kinds, an unknown kind, a real week with `--no-jobs`; EN 3 × and a kind by the game's words.
- [ ] T28 `fm youth candidates`: rows not seen yet (intake each March; PORTING).
- [ ] T29 After a goal with a change pending, the game's question (dialog `Type` `cancelTweaks`). `match` stops and says
  `dialog`. EN 3 goals, TR 2: Yes makes the tweak, No drops every waiting change, active ones stay. Left: `cancelChanges`
  (a tactic change, not a tweak) not met.
- [x] T97 The match bar's Tactics button opens the panel every time. EN first half, paused and playing, 12 of 12; TR second half 5 of 5.
  What looked like a dead button: a Cancel pressed as the panel appears can be lost, the panel stays up and the button stays hidden. Cancel is read back.
- [ ] T98 `talk`: a journalist pressing with the same answers on offer read as "did not move on". Now the words said count
  too (`Says`). Not met again yet: check at the next one.
- [x] T30 The active tactic's name is right again ("pendik-yedek-t1", from `tactic load`). FM27's tactic menus have no
  rename (Edit Tactic and Manage Tactics open nothing to type in, TR); a tactic is named by `tactic save "<name>"`, then `tactic load`.
- [ ] T31 `scout player --cancel`: the game keeps the assignment (PORTING). Measured TR: the entry is Report › Cancel
  Assignments (`PersonStopScoutPlayersClub`), its question `Confirm1198281291` has a Yes with no event; answered by the
  dialog handler it closes, and the player's `IsBeingScouted` stays True (also after `next`). Read back now by
  `IsBeingScouted` (the person kept at `fmcli.scoutcancel`). Next: Scouting › Assignments (`ScoutingCentreActions`).
- [x] T32 `training --trait-suggest`: FM27 has no such entry in the actions menu and no button for it, so the flag leaves help, the MCP tool and the tests; typed by hand it says so (PORTING). The refusal itself not run again after this change.

## 4. Pages no command reads (read only, lower priority)

- [x] T33 Club history: overview, records, honours, landmarks, manager history, all-time best XI.
  `fm read club-history | club-honours | club-records | club-best-xi | club-landmarks | club-managers`.
- [x] T34 Club profile: affiliates, facts, stats, club news.
  `fm read club-profile | club-info | club-news | club-affiliates | club-facts | club-stats`.
- [x] T35 Career: my profile, history, relationships, managerial stats.
  - [x] Managerial stats: `fm manager` reads the page's `Person.*` paths (TR, 3 × from another page; the figures are
    the page's own, `Human` does not bind most of them). EN checked.
- [x] T36 Competition: past winners, awards, records, rules, structure, qualification places, coefficients,
  `fm read past-winners | award-winners | comp-records | comp-rules | comp-structure | qualification | coefficients | nation-agreements | rankings | season-preview | suspensions | injury-table | manager-movements | rumours | window-report`; the season preview is there only before a season, refused otherwise.
  nation agreements, rankings, season preview, suspensions, injury table, manager movements, rumours,
  transfer window report.
- [x] T37 Transfers: history, window, clauses. Scouting: reports, priorities, range, not interested.
  `fm read transfer-history | transfer-window | clauses | scout-reports | scout-priorities | scout-range | not-interested`.
- [x] T38 Squad: discipline, loans, promises, squad feedback. Medical centre: risk assessment, recently returned,
  `fm read discipline | loans | promises | squad-feedback | risk-assessment | recently-returned | medical-season | medical-player`.
  season summary, player summary.

## 5. Robustness

- [ ] T39 `fm crawl`: a popup that holds the event queue is closed and the queue let go, instead of stopping. Written; the one run over the opposition report's panels (14 pages) did not stop, but the queue was left held at its end. Check in the full crawl (T77).
- [ ] T40 `fm stop`: the game hangs on its way out now and then (process stuck exiting, state UE, not killable,
  its window kept until a restart); cause unknown. Seen again 2 Nov after `stop --save` straight after a
  match with a press question open. `stop` no longer counts such a process as running.
  Same evening even a killed `/bin/sleep` stuck exiting (UE): the Mac, not the game; check after a restart.
- [x] T41 `settings language` says the portal's pages do not open until the game is opened again (save, stop,
  start); the language setting is told by its pref `current_language`, not by the CLI's word for it.
- [x] T42 `status` with a held event queue: the inbox's wait gives up when its event still waits after 3 s. 5 s measured (it was 2 minutes).
- [x] T43 Squad numbers and the squad list at a season's start (scenario `season-lists`): the assistant's pick by `EventFmxAutoSelect`, Confirm by being the page's primary button that raises the question, answered by the game's handler. `numbers --auto` and `register --auto`, `--send`: TR + EN.

- [x] T95 `next` stopped at "The game does not move on: TransferHub" when the window opened (28 Jun 2028): FM27's
  look-only screens (TransferHub, NewsForYou, CompetitionOverview, DataHub, YouthSetup; `ContinueFlowFlags`) are
  passed like CompetitionHub. Training is left out until seen. Passed once, EN.
- [x] T96 A name typed as several words matches by the starts of the name's words when no part is typed whole
  ("Enes Şahin Orduspor" for 52 Orduspor FK's offer).
- [x] T91 `fm start` hung at the main menu when a dead game (UE) still held the port the file named: the menu check
  asked `health` with the long timeout. Now it asks briefly first (`Answers`, 3 s) and the port file is read on every
  call. Measured with 7777 held by a dead game: 2 normal starts and one with the file set to 7777, 75–78 s each.
- [ ] T92 The game crashed once leaving a match (15 Apr 2028, tweaks used in that match with a wrong bound path). Twice more on 5 Nov at kick-off, in a terminal with `status` asked every 2 s from another process (SIGBUS on the GC Finalizer thread); not with it asked every 5 s or not at all. Cause not known. Again 5 Nov, EN, list `round4`: gone before kick-off with `tweak` asked every second; the two reports of that day on the GC Finalizer jump to a bad address from UnityPlayer. `until` now takes `every`. Again 5 Nov 20:03, EN, ten seconds after `match` began submitting the team, with `tweak` asked every 5 s.
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

- [x] T44 `SI.Bindable.TableRowNavigatable` gone (`Controls/TableRows.cs`, `Reads/Registration.cs`).
- [x] T45 `BindableTextEditBox.m_textField` moved to `BindableEditBox<T>` (`Controls/TextBoxes.cs`).
- [x] T46 `Bindings.GetDescendantCount` gone (`Bridge/FmApi.cs`, status line only).
- [x] T47 `IdentifierInfo.DisplayName` gone (`Bridge/Schema.cs`).
- [x] T48 `EventFmxMatchFocusSelect*` gone (`Match/Prep.cs`); `prep` reads the training calendar's sessions on FM27, changed by `schedule set`.

## 7. Platform (macOS-only today)

- [ ] T49 `fm start` / `stop`: `/bin/sh` + `run_bepinex_arm64.sh`, the `fm27_bep/fm.app` bundle, `PlistBuddy`,
  `pgrep` / `pkill`, Steam's macOS path (`Core/Launcher.cs`). Windows has none of it.
- [ ] T50 Window hiding by `osascript` (`Launcher.Minimise/Hide/DropFromDock`); Windows needs its own.
- [x] T51 `doctor` (`Core/InstallDoctor.cs`) still checks FM26 paths (`fm26_bep`, "Football Manager 26").
  `doctor` and `install.sh`, `package.sh`, README hold FM27's layout (`~/fm27_bep/game`). `doctor --install` passes here; a clean machine is T83.
- [ ] T52 Publishing: `tools/install.sh` builds `osx-arm64` only.
- [ ] T53 Plugin on Windows x64: untried (Harmony would work there; arm64 has no MonoMod).
- [ ] T54 Save and user folders on Windows (`Core/GameFolders.cs` has the path; untried).

## 8. Moments the crawl has not seen (crawl them when they come: `fm crawl out.json --here`)

Each: crawl the screen, map it to a command, open a task for what is missing.

- [ ] T55 Season's end: club vision meeting, end-of-season awards, players whose contracts end, retirements.
- [ ] T56 Transfer window open and deadline day: incoming bids, the window's own screens.
- [x] T57 Cup draws: a draw's message offers skip or view; view opens the draw screen, `fm draw` runs it (`EventFmxDrawAllTeams`, read back by the button going off) and prints it. `fm read here` reads whatever is on top. TR + EN, nothing open refused.
- [ ] T58 International break: call-ups, the national side's screens.
- [ ] T59 Youth intake (March): candidates, intake preview.
- [ ] T60 Board meeting and board requests answered; job offers while employed; being sacked.
- [ ] T61 Contract renewals: a player asking for a new deal, an unhappy player's demand.
- [ ] T62 Pre-season: friendlies, pre-season training, new-season expectations.
- [ ] T63 Match: substitution screen, FM27's shouts (or what replaced them), set pieces edited during a match,
  penalty shootout, extra time, injury during a match.
- [ ] T64 Press: every kind of press conference and media question (pre-match, post-match, transfer, unhappy player).

## 9. Depth the crawl has not reached

- [x] T65 Player interaction: `actions <player>` lists the game's Interaction menu (praise, criticise, advice…), `actions <player> "<entry>"` opens its talk, `talk` answers. TR + EN, a whole praise, an unknown entry refused.
- [x] T66 `fm meeting`: the squad page's own button by its element (`HoldTeamMeeting`), a talk answered with `talk`; a second one the same day is refused by the game and said. TR + EN.
- [ ] T67 Every list's submenus (tactic presets, Manage Tactics, Primary Trained Tactic, Copy Tactic …):
  crawl with the lists opened one level down.
- [ ] T68 Every row's right-click / actions menu, for each kind of row (player, staff, club, competition, nation).
- [ ] T69 Game settings and preferences pages; the main menu (new career, load, online). Leave the
  display controls (window mode, resolution) unpressed, and block them in the tool: full screen while
  hidden is a black screen.
- [ ] T70 Club pages of a club that is not ours (what a rival's pages offer), a nation's and a competition's pages
  from the world view.

## 10. Quality, for everything above

- [x] T71 Every new command has its MCP tool (`Core/Mcp.Tools.cs`) and help line (`HelpEnglish.cs`, labels).
- [x] T72 Every new command is reachable from the natural-language layer (`cli/fm/Nl`), `tools/nl-check.py` passes.
- [ ] T73 `docs/MANUAL.md` images refreshed for every changed output (`tools/manual-shot.py`). `live-match.png` shows the new key line. To capture: `tools/live-capture.py … -- fm match --play --no-talk` (the `--` is needed).
- [ ] T74 Full sweep passes after the last task: `tools/sweep.sh` (every read command) and `tools/mcp-check.py`,
  TR and EN.
- [ ] T75 `docs/AI-CHECKLIST.md` open items done or moved here.
- [ ] T76 `docs/BACKLOG.md` ideas decided: each made a task here or dropped.
- [ ] T77 `fm crawl` TR + EN again at the end; the report shows no event or page without a command (or a reason).

UX: `fm` is a conversation, so no running command should make you leave it or open a second terminal. Found by reading the code;
not yet tried in a live terminal.

- [x] T100 `:` on the live panel pauses play and opens `fm›` under it; any command runs there, a key goes back, play goes on. Measured in a terminal, EN and TR.
  Today only the fixed keys work (`s h t v a r + -`, `MatchDay.cs`).
- [x] T101 Keys on the live panel: `p` pause / resume (the panel stays up while paused), `k` skip a replay, `d` dugout. New advice joins the panel's events, every card. EN and TR.
- [x] T102 Long commands: `holiday` and `next` show the day on one line that writes over itself, on a terminal only. The prompt comes back with Ctrl-C (T103). TR + EN.
- [x] T103 Ctrl-C in a session ends the command (every request to the game stops), says the game goes on with what it was doing, and returns to `fm›`; at the prompt it starts a new line. A holiday stays on. TR + EN, in a real terminal.

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
  - [x] Comparisons against the CLI's own labels gone: `register`'s menu by its entries' places, `contracts` terms by
    their field, the table's form by the game's codes.
  - Known: `Transfer/Contracts.cs` leaves out terms whose value reads "-", "N/A", "None", "No" (English words the plugin gives).
