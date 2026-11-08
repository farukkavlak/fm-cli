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
  a language switch about 10 s more; a simulated match 15 s. Kept: `calm`, `cup`, `cup-draw`, `kickoff`, `window`, `season-lists`, `sacked`, `interview`, `job-offer`, `newjob`, `spring`,
  `deadline`, `intl-break`, `helmond-may`, `playoffs`, `playoff-final`, `season-end` (the list: `scenarios`).
  - [x] Season's end: `season-end`, 4 Jun 2030, Helmond Sport after a lost play-off final; the vision meeting and the pre-season date wait.
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
- [x] T93 Vision, new in FM27: `vision plan` reads the 5 Year Plan, `vision add <no>` adds an objective.
  The plan's tab by `Board-Vision-Button` and the value it writes (`selectedtab`); a season's button by the value it
  writes (`visionobjectiveyear`), its name by its text's data node; a season read when the meeting shows it and the
  rows drawn are the game's list (`ClubVisionMeetingObjectivesForYear`). Add: the list sending
  `EventFmxAddClubVisionObjective`, open once a season is shown; read back by `CanUndo` or the season's list.
  The weight stepper only draws the weight: the game has no event to change it. TR + EN 3/3 (`round10.toml`):
  read twice the same, add, undo, reset, wrong number, the objectives' tab after the plan's.
  - The objectives' tab is the `selectedtab` button that is not the plan's, read back by its rows being drawn whole.
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
- [x] T86 Cup results by the game's numbers, not the shown letters ("u3-2", "p"). `stages` reads the round's own list (`Stage.CupTies` under the page's GUID, new plugin route `pullbound`): whole, no scrolling, no portal tile's ties mixed in, divisions back. Result codes: 1 win, 2 won in extra time, 3 won on penalties, 7 won a leg of a two-legged tie, 9 draw, 10 defeat. `calendar`, `fixtures`, `fixtures --all`, `report`: score, level with a winner is a shootout, code 2 is extra time. Croky Cup, every round, TR and EN agree: 154 ties, 13 penalties, 7 extra time. List `round3`, 3 ×; our cup tie simulated, read by report, fixtures and the full list: TR + EN 3/3 (`round32.toml`).
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
  - [ ] Left: `responses` and `activity --loan` hold their tables by names not seen yet (`responses-transfers-table`, `responses-loans-table`, `activity-loans-table`); no such rows in the scenario. Offer through a list of several players: measured by hand once, EN only. `discard --all` and a wrong word to `--time`, `--type`, `--oop-role`: TR + EN (list `round5`). A real value through `--oop-position`, `--oop-role`, `--role`, `--time`, `--type`: not run yet. Measured EN 8 Nov
    (`window`): the lists take the game's words only ("Squad Player"), no number, so one test cannot run in both
    languages; a read of each list's choices (`transferroom new --position <p> --choices --json`) would let a test
    keep one. `--role` with a wrong word listed every role, not the position's.
    Done: a list's choice is taken by the game's own value too (a playing time's code, a role's bit; an error lists
    each choice with it), the same in every language; a requirement made by values, read back, deleted; the wrong
    words and `discard --all`: TR + EN 3/3 (`round33.toml`). The type list and some roles carry no value: by word.
- [ ] T12 Squad Planner: `fm planner`. Seasons, positions (new plugin route `pitchtiles`, a tile by its element name), add, move, remove, suggest. TR + EN 3 × with the negatives. Best XI and contracts: `fm read best-xi`, `planner-contracts`.
  - [ ] Left: copy a plan (EventFmxSquadPlannerCopy, its data `copytype`). The list's entry sent by `select`, by a row's
    submit, and by `select` with its events queued (`fire=1`, Season After, EN 8 Nov) changes nothing in the other
    season (its ST and DCR stayed). Next: what a click sends that these do not; the rows' ticks (`SelectedPlayers`).
- [x] T13 Tactic: personalised roles (`EventFmxTogglePositionPersonalisedRole`), swap two players
  (`EventFmxSwapPlayers`), clear a slot (`EventFmxTeamSelectionClearPlayer`).
  - [x] Swap two starters: `tactic player <position> <starter>` already does it (the two change places). Clear a place: `tactic player <position> --clear`, the place's `XIButton` told by its row's `PlayerIndex`, its right-click list sends `EventFmxTeamSelectionClearOrPickPlayerAuto`, read back by the place's `PlayerIndex` -1. TR + EN 3 × (list `round5`): wrong position, only that place, already empty, filled again, from another page.
  - [x] Personalised roles: `tactic personal` lists each starter's, `tactic personal <position> on | off` turns one.
    The toggle by its event and its row's PlayerIndex; on/off and the read-back by the game's value beside it
    (`Tactic.PositionRoleOverride`, 0 off), not by its colour. TR + EN (`round11.toml`): fresh load, another page,
    the page open, already on, wrong position, wrong word, `--dry` changes nothing; 3 ×.
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
  EN `calm` 7 Nov: empty too. FM27 has no club right-click menu: league table rows and a club's own page carry only
  person menus (`actions?menus=1`; `ClubButton` has no right-click events), so `ScoutTeam` is not reached that way.
  Next: find where the game fills `ScoutTeamPriorities.SpecificallyScoutedTeams` (scouting staff, a date with fixtures ahead).
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
- [x] T28 `fm youth candidates`: the club's own list (`Human.Club.AllYouthCandidates`), read once it holds the club's
  count (`YouthCandidatesCount`) with every field filled: name, age, positions, nation, potential as the club sees it.
  `youth candidates` is taken as a word (it fell to the language model). TR + EN 3/3, none before the intake
  (`round19.toml`, scenario `intake`, 5 Apr 2029).
- [ ] T29 After a goal with a change pending, the game's question (dialog `Type` `cancelTweaks`). `match` stops and says
  `dialog`. EN 3 goals, TR 2: Yes makes the tweak, No drops every waiting change, active ones stay. Left: `cancelChanges`
  (a tactic change, not a tweak) not met.
- [x] T97 The match bar's Tactics button opens the panel every time. EN first half, paused and playing, 12 of 12; TR second half 5 of 5.
  What looked like a dead button: a Cancel pressed as the panel appears can be lost, the panel stays up and the button stays hidden. Cancel is read back.
- [x] T113 `talk --json`: its answers came in the game's order, `talk <number>` counts the buttons' (a manager's contract: first listed Cancel, `talk 1` was Finalise Deal). Now `--json` lists them as `talk` takes them, with `Number`. Passed TR + EN (`round6.toml`, an interview).
- [x] T114 Deadline day, coverage taken part in: `game.CurrentGameDate` went unbound and `status` stood on 3 Sep
  while the game ran on to 7 Sep (match "today", fixtures 7 Sep). The plugin now takes `game.CurrentGameDay`
  when it is later or the packed date is missing. To check: `tests/live/round6.toml`, TR + EN. Round 6 did not
  reach it (an offer stopped `next`); round 7, offers rejected on the way, passed TR + EN: 3 → 6 Sep, and at the end
  `CurrentGameDate` was unbound while the date read 6 Sep from `CurrentGameDay`.
- [x] T115 Inbox lost its older messages: on 29 Sep 2029 (scenario `intl-break`) `inbox` lists 10–14 rows, the
  day's, and `reply "Four players called up for international duty"` finds nothing, while an hour earlier it was
  row 4 of 37. Fixed: FM27's list has no element name, so `inboxlist` finds it by its type
  (`MessagesStreamedObjectList`) and gives its scroll view's content, which `scroll` takes. 3/3 TR + EN (`round8.toml`).
- [x] T116 With the vision meeting open, `status` took 3 minutes and `reply` 64 s: the meeting covers the inbox, so
  every read of it waited out its bound. Both now say the meeting is open and stop; the whole inbox is read once.
  `status` 4 s, `reply` refused in 0.6 s. 3/3 TR + EN (`round10.toml`, `within` bounds a step's time).
- [ ] T117 A message's attachment (`reply`, `inbox` opening one).
  - [x] Held by the attachment's own elements (plugin `/attachment`, under `#MessageAttachments`): a table's headings
    (its `ScrollView_Headers`), its laid-out rows (its list's rows, with how many more it holds), every other group of
    texts; in the game's words (was English words in a Turkish game), the body left out. Nothing chosen by place;
    the order is only how it is shown. Deadline day's card now has its figures, headline deals and the activity
    table's headings; the portal's league table no longer mixed in. TR + EN by hand: deadline card, Code of Conduct,
    an opposition scout report.
  - [ ] A sign that the card is drawn (the open message's `NewsItem.DynamicCardId` node is there but unbound): taken
    now once two readings agree.
  - [x] A figure and its label are one line ("88 · Done Deals"): one of a set of look-alike items (siblings built
    of the same element types, two texts or more each) is one line however deep its texts sit. By shape only,
    no names or words; Fitness tests, Staffing Advice and Scouting update came out the same as before (EN).
- [x] T118 `tour` at a step that waits for something done on screen says only that: the new club's Tactics Introduction
  ("select the tactical display shown", no tactic set) passed once `tactic new` made a tactic. Name the command for
  such a step by the step's anchor (`FM.UI.TutorialAnchorID`) or action (`TutorialActionsID`), not its words.
  Done by the data that says why: a step with no way on while the team has no tactic (no planner, as `tactic` tells it)
  says `tactic new`. The check was there but asked TacticCount, which is not bound with no tactic. The induction steps
  carry no anchor of their own (only CurrentInductionStepIndex). TR + EN by hand (`helmond-may`): the hint is text, which
  the test tool does not read.
- [x] T119 `news <no>` printed "the story's text could not be read" for every story (EN, 31 May 2030): it walked the
  whole tree for the text. Now the story's title on screen names its record (`Under`), and the title and text are read
  together from that record (`newsitem.NewsBodyString`). TR + EN 3/3 (`round14.toml`).
  - [ ] The list's age reads "Just Now" in a Turkish game: find where the game's own words for it are bound.
- [x] T120 Found by the sweep (`tools/sweep.sh`, `helmond-june` and `kickoff`, TR + EN): `table` right after a load
  failed once ("Lists on the Stages page not found": the stage lists come a moment after the page; now waited for);
  `depth` and `targets` wrote positions in the CLI's English codes in a Turkish game (now the game's own, "KL", and
  `targets KL` is taken). TR + EN 3/3 (`round15.toml`); the sweep: 63 commands, `kickoff` TR + EN all pass.
- [x] T122 `reply` said "answered" when the game did nothing: a window whose only button closes it is now the game
  turning the answer down when the message still waits after it; its own reason is said, exit 1 ("choose a captain
  before confirming"). A list's option by number is only that place now ("9" of four took the date with a 9).
  TR + EN 3/3 (`round29.toml`, `caught-2029-03-31`).
- [x] T123 The manager's own new contract: `reply` → talks says "the terms: contract"; `talk` points there too;
  `contract` reads the terms (wage, length, clauses, the current deal's end), `--sign --yes` signs. TR + EN 3/3
  (`round29.toml`, `caught-2029-05-26`).
- [x] T124 Status flags in the game's words: `squad --json` Flags `Text` came raw, English in a Turkish game for some
  ("Unhappy", "Transfer listed", "Retiring"); now read with /pull `shown` (TR "Mutsuz", "Transfer listesinde"; EN
  "Unhappy"). The squad board left out the injury flag by its English word: now by its number (41,
  InjuredNotInTraining). TR + EN by hand (`caught-2029-02-02`, `caught-2029-03-31`).
- [x] T125 `squad` read only the rows the table had drawn: 22 of 33 on a 1080-high window (the injured, at the
  bottom, missing); `depth`, `squad --fitness` too. Now, when fewer rows carry the view's own field (Wage,
  Condition) than the team has players (`Human.Team.Players`), the table is scrolled down in four steps and the rows
  put together by name. The scroll holds by the cells the rows bind (the value range, the substitute mark); /scroll
  `all=1` moves every list binding one, since a table's other views stay laid out beside it. TR + EN 3/3, cold, page
  open, another page open (`round31.toml`).
- [ ] T98 `talk`: a journalist pressing with the same answers on offer read as "did not move on". Now the words said count
  too (`Says`). Not met again yet: check at the next one.
- [x] T30 The active tactic's name is right again ("pendik-yedek-t1", from `tactic load`). FM27's tactic menus have no
  rename (Edit Tactic and Manage Tactics open nothing to type in, TR); a tactic is named by `tactic save "<name>"`, then `tactic load`.
- [ ] T31 `scout player --cancel`: FM27 calls it off on Recruitment › Scouting › Scout Priorities
  (`6bc226cc…`, from the Scout Reports bar): the table in `RecruitmentScoutPrioritiesBlock`, his row told by its
  record's `UniqueId`, ticked alone, then the page's button that sends `EventFmxCancelScoutingPlayer` (off until a
  row is ticked). Read back by his `IsBeingScouted`; `--cancel --dry` changes nothing and answers 0 only while he
  is scouted. His menu's entry stays closed in FM27 (the old way). Tests: `round34.toml`.
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
  6 Nov, `kickoff` EN: 3 of 4 matches died at "Submitting the team" with nothing else asking (GC Finalizer, SIGSEGV
  at 0x20, GameAssembly → UnityPlayer). That day the plugin had begun reading every event context of every control
  for `Writes` on each `buttons?json=1`, which the submission asks many times; `Writes` is now only read when asked
  (`writes=1`, only `vision plan` asks). After: 3 of 3 kicked off, game alive (EN). Older crashes predate it: still open.
  Later the same night: round22 EN died once more at submission (GC Finalizer, `status` asked every 10 s); TR 3 of 3
  fine. Rarer, not gone. `shown`'s `Under` is also only read when asked (`remap=1`).
  A lead: every death that night was in an English run; the Turkish runs (6 of 6 in rounds 22–23) never died.
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
  - [x] Vision meeting: Negotiate, remove, a required one refused, undo, reset (closed with nothing changed). TR + EN, `round9.toml`.
  - [x] Contracts ending on the last day: `contracts`. TR + EN.
  - [x] A player's statuses by the game's numbers: `squad --json` `Flags` (`Id`: 20 retiring, 75 free agent; `Type`).
    The on-trial note is told by the flag, not its words. TR + EN (`round10.toml`).
  - [x] Retirements come as news stories ("Lopes retires"): `news`, `news <no>` (T119). Award winners: `read
    award-winners`, the table with its headings. TR + EN (`round14.toml`, scenario `helmond-june`, 31 May 2030).
  - [ ] This season's award, once given (not out by 31 May): its message or story, and the page's new row.
- [ ] T56 Transfer window open and deadline day: incoming bids, the window's own screens.
  - [x] Deadline day (`deadline`): its message's question (Take Part / Decline) answered with `reply`, read back by the
    message no longer waiting; a wrong answer number refused. The offers waiting are free-transfer ones the game answers
    only from their message: `offers <#> reject` says so (it called every refusal a stall). TR + EN 3/3 (`round13.toml`).
    Taking part brings a journalist's question that the game itself closes on the next hour (`next` presses Continue
    once, nothing else).
  - [x] Fee bids rejected (gone from the list) and accepted (status moves on) through `offers`. TR + EN 3/3
    (`round20.toml`, scenario `window-bids`).
  - [ ] The window's own pages (Transfer Activity).
- [x] T57 Cup draws: a draw's message offers skip or view; view opens the draw screen, `fm draw` runs it (`EventFmxDrawAllTeams`, read back by the button going off) and prints it. `fm read here` reads whatever is on top. TR + EN, nothing open refused.
- [ ] T58 International break: call-ups, the national side's screens.
  - [x] Who is away now: `internationals` by the status's ineligibility reason (3 joining, 4 coming back, 5 on duty)
    and the day he is back (`InternationalReturnDate`); `squad --json` Flags carry `Ineligible`. The career is shown as
    the game words it: it was taken apart by the English words "caps" / "goals" (0 in a Turkish game). TR + EN 3/3,
    nobody away outside a break (`round12.toml`).
  - [ ] The postponement request a call-up brings ("Request … postponement?"): answered with `reply`, read back.
- [x] T59 Youth intake (March): candidates, intake preview.
  - [x] Candidates (T28). The intake came on 4 Apr 2029 (Varzim) with no message of its own in the inbox.
  - [x] A candidate's status by the game's data (on trial until signed: no trial club once signed); a youth contract
    offered with `contract <candidate>` (the game's approach to sign, PersonApproachToSign, when there is no contract
    to renew), his answer by message (EN by hand: Agostinho signed, then "signed" in the list). The rest of his
    menu through `actions`. TR + EN 3/3 (`round25.toml`).
  - [x] Intake preview: `youth preview` (the club's `YouthCandidatesPreview`: title, verdict, status in the game's words,
    the positions foreseen by their masks in the game's abbreviations; the list bound by asking the preview for it).
    TR + EN 3/3 (`round26.toml`), no candidates before the intake.
- [ ] T60 Board meeting and board requests answered; job offers while employed; being sacked.
  - [x] Sacked (`sacked`): the message, `status` says unemployed, `jobs` lists 45; applications by the game's own button.
  - [x] Interview (`interview`) through `reply` and `talk`, to a job offer, its contract card (`contract` now reads an
    open card while unemployed: it was refused for want of a club), `talk` Finalise, the new club. TR + EN 3/3 for the
    card (`round19.toml`). Left: the card's heading has no name (" — Terms") and its last line speaks of an agent.
  - [x] Board requests (T85, `round16.toml`).
  - [x] Board meeting from its message (club objectives): "discuss" opens `vision`; remove, put to the board, accept.
    The board's "change season expectations": `reply <msg> --pick <n>` chooses the level, read back by the game's
    own mark; `board` shows it once time moves on (a page built before keeps the old level, the game's own).
    End of season: pre-season start date and camp place from the message's list. TR + EN 3/3 (`round29.toml`).
  - [ ] A job offer while employed.
- [ ] T61 Contract renewals: a player asking for a new deal, an unhappy player's demand.
  - [x] A renewal we start: `contract <player>` promises, `--next` terms, `--sign --yes` offered; the player's answer
    comes by message and `contracts` shows the new end (EN by hand: Ahmet Koşar, 30/6/2028 → 30/6/2030). A player the
    game gives no talks with is refused. TR + EN 3/3 (`round21.toml`, scenario `june`). A job offer's card has no
    player: its heading is the stage alone (was " — Terms").
  - [x] An unhappy player's demand (`unhappy`, 26 Jun 2028: a bid for Ozan rejected): his message answered with
    `reply` (talk to him), the conversation through `talk`, a fee proposed when he asks (`talk fee`, `talk confirm`),
    to its end; the message read back as answered. TR + EN 3/3 (`round27.toml`).
  - [x] Players' concerns (`round30.toml`, TR + EN 3/3): given to a team-mate to settle (the message goes); talked
    through with a promise; one thinking of leaving for free talked round; a transfer-list request granted, read back
    by the game's own status (flag 6) once the day moves on.
  - [ ] A player asking for a new deal himself (moment not met yet).
- [x] T62 Pre-season: friendlies, pre-season training, new-season expectations.
  - [x] Friendlies: `friendly` (new) lists the opponents the game offers (by reputation, with fees), the open dates and
    the rules; `friendly <#> [--date] [--away] [--rules] --yes` proposes one. The panel by its button (`ArrangeFriendly`),
    opponents by their binding (`selectedteam`), venue by the value its button sends, rules by `matchrules`, the date
    from the game's own open dates (they depend on opponent and venue, read after both are set) written where the
    picker writes (`selectedDate`); read back by the panel's record (`IsOppositionTeamValid`, `IsMatchVenueHome`,
    `IsValid`) and the panel closing. EN by hand: Almere City accepted, on the calendar; Gençlerbirliği rejected.
    A friendly proposed to us: `reply` accept, on the calendar after the game moves on. TR + EN 3/3 (`round20.toml`).
  - [x] Pre-season training and the new season's expectations (`preseason`, 3 Jul 2028). The team meeting
    ("Pre-Season Team Meeting") answered with `reply` and talked to its end with `talk`, read back as answered.
    `board` adds this season's expectation in each competition (`comp.TeamsBoardExpectationsInCompString`, bound only
    on Portal › Competitions: the page is opened and read whole once there are as many as
    `ClubSeasonExpectationsNumberCompetitions`); measured cold, page open, another page open. The pre-season stages are
    `schedule style` programs: `--json` lists them by the game's numbers (category.index), which `style` takes; the
    week under way is refused (the game's selector is off; the message said "passed"). TR + EN 3/3 (`round28.toml`).
  - [ ] The "Season expectations" message's attachment shows only the competitions' names (T117). The Code of
    Conduct attachment came out in English words in a Turkish game on 7 Nov ("Team Leaders", "Multiple Booking ban").
- [ ] T63 Match: substitution screen, FM27's shouts (or what replaced them), set pieces edited during a match,
  penalty shootout, extra time, injury during a match.
  - [x] FM27's shouts: a popup on the match bar (the floating widget inside `#shouts-cooldown-disable`), team shouts
    as `shout-button` rows sending their record (`TouchlineShoutAction`, `Title` in the game's words); read back by
    the match tactic's `HavePendingTouchlineShout`; while one waits the popup is gone, said as the cooldown.
    TR 3/3 (`round22.toml`); EN by hand (list, json, wrong name, Up Your Game shouted, cooldown); EN run died at
    submission (T92); round23 EN 2/3 passed, the third died at submission again (T92). The Individual tab: not yet.
  - [x] Injury during a match: the game's "Injured Players" window is answered with `dialog`, the player changed
    with `sub <off> <on>`, read back by `sub` (EN by hand: Sušak off, Ozan on).
  - [x] Set pieces during a match: opened through the match bar's tactics button (or, once a change waits, the
    pending card's edit: whatever sends MatchOpenTactics and is not a tab), then its `SetPieces` tab; a change is
    confirmed by the panel's own Confirm Changes, a read leaves by Cancel; both read back by the panel going.
    TR 3/3, EN 2/3 (one died at submission, T92), `round23.toml`.
  - [x] `status` in a match says a window the game opened (by the dialog route), with `dialog` to answer it.
- [x] T64 Press: every kind of press conference and media question (pre-match, post-match, transfer, unhappy player).
  - [x] The new manager's unveiling: its invitation answered with `reply` (Attend), every question with `talk`, the
    conference over (no answers left, `talk` then has no conversation). TR + EN 3/3 (`round24.toml`, scenario `press`).
  - [x] Pre- and post-match invitations are messages with Send / Delegate / Attend, answered with `reply` (seen in
    the Helmond spring run); attended ones are the same `talk`.
  - [x] Media questions about a player who wants out (a loan, a transfer list): `reply` Answer → `talk` to its end,
    or Decline; read back as answered. The post-match conference attended and talked to its end. TR + EN 3/3
    (`round30.toml`, `caught-2028-08-14`, `caught-2028-08-06`, `caught-2029-02-02`).

## 9. Depth the crawl has not reached

- [x] T65 Player interaction: `actions <player>` lists the game's Interaction menu (praise, criticise, advice…), `actions <player> "<entry>"` opens its talk, `talk` answers. TR + EN, a whole praise, an unknown entry refused.
- [x] T66 `fm meeting`: the squad page's own button by its element (`HoldTeamMeeting`), a talk answered with `talk`; a second one the same day is refused by the game and said. TR + EN.
- [ ] T67 Every list's submenus (tactic presets, Manage Tactics, Primary Trained Tactic, Copy Tactic …):
  crawl with the lists opened one level down.
- [ ] T68 Every row's right-click / actions menu, for each kind of row (player, staff, club, competition, nation).
  FM27, 7 Nov: club rows have none (see T21). `search` now gives a club result's `ClubPath`, and `searchopen?path=`
  opens that club's page by its `ClubSearchButton` (T70 can start there).
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
  - Found 6 Nov (grep of the source; each needs its identity measured in the game):
    - [x] `Club/BoardRequests.cs`: whole command broken in this FM27 build (the card it clicked is gone); the stadium
      and new-contract requests were found by English labels. Now FM27's one list (`#MakeBoardRequestButton`), each
      request by its menu path, sent by the list's own event; the answer is a dialog (`board_request_ok`), read back
      by the entry leaving the list. TR + EN 3/3 (`round16.toml`). A board takes only so many at once: later ones
      were left unsent with no answer (EN, by hand); said as "no answer came".
    - [x] `Squad/Registration.cs`: the rules by their data path (`squadreg.ExtraSquadRules`), each line as the game
      writes it (`register --json` Notes); TR + EN (`round16.toml`). The page's fallback is the button opening its
      GUID, not "the one before '<n> assigned'"; that fallback's case was not met (the page opened directly).
    - [x] `Squad/Query.cs`: the wage is the game's sort value; the English "p/w" fallback (never reached) removed. TR + EN.
    - [x] `Club/Internationals.cs` caps/goals from English words (T58); `Core/StatusFlags.cs` "Free Agent" (T55);
      `Match/News.cs` story text (T119); `depth` / `targets` positions in English codes (now the game's own).
    - [ ] Plugin audit 6 Nov: no literal text, colour, position or style compare found. Known: `Reads/Pages.cs` takes a
      menu's first row as the tab's own screen (order).
    - [x] `Transfer/ScoutFocus*.cs`: the form was found by the buttons' words "Back" / "Next" (Turkish only through the
      plugin's English); now from the step button itself (told by what it sends), plugin `form?ptr=`. Old and new
      reads the same on both steps (49 and 44 items, EN).
    - [x] `Transfer/Scouting.cs`: "no focus" was known by a 10 s wait running out; now by the club's list in its data
      (`Human.ScoutingInfo.RecruitmentFocusesList`): 4 s, none / one / none TR + EN (`round18.toml`).
    - [x] `Squad/TacticFiles.cs`: the save panel's title and note boxes by their bindings (`SelectedFileName`,
      `Description`), not by order.
    - [x] `Club/ClubCards.cs` `CardWords`: the game's values (grades, yes/no) now in the game's words (`GameWords.Of`:
      "Hayır" where the CLI said "yok"); the kinds of supporter are the CLI's own list (by their data fields), so its
      own words (`L`).
    - [ ] `Club/Youth.cs` overview (`youth`): first-team prospects read as "the cells after a name" (order) and the
      age as the number in "21-year-old full-back" (a sentence); waits on two reads agreeing.
    - [ ] Waits on "two reads agree" (about 100 places in 45 files: `settled`, `== last`): each is a time-based sign; move to
      the game's own sign where one is known (as T93, T119 did), one by one, measured. Tried and kept as it was:
      `squad --view` (the rows keep fields of views shown before and gain the new ones over several reads, so "the
      columns changed" is not a sign that the view has landed; TR + EN, `round18.toml`).
