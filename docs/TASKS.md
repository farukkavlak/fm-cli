# Tasks

One list. Pick by id ("T12"). Tick a box in the commit that does it.
Done = every box ticked; acceptance tests (section 11) last. New work is added here first.
Details of past work: git history. Port notes: `PORTING.md`. Crawl data: `reference/fm27/`.

## 0. Basics

- [x] T0 The game never shows itself (hidden from launch to a loaded save)
- [x] T104 Scenario saves, one load away (`tools/fm-round.py scenarios`)
- [x] T105 One test tool: `tools/fm-round.py`, lists in `tests/live/*.toml`
- [x] T106 `captains --captain / --vice`
- [x] T107 A line starting with a command is that command, every word kept
- [x] T108 `status` keeps the date after a simulated match
- [x] T109 `load` opens the save it was asked for
- [x] T110 A game update no longer breaks `fm start` (bundle signing)
- [x] T149 Since 27.0.1 the game would not start: Il2CppInterop's type hook fixed (`InteropHookFix`)

## 1. Held by text or order

- [x] T1 `tactic new`
- [x] T2 `vision`
- [x] T93 `vision plan`, `vision add`
- [x] T3 `talk` echoes in the game's language
- [x] T4 `tour`
- [x] T5 Calendar and training week venue
- [x] T6 `coaches`
- [x] T7 `players` search columns
- [x] T8 `tactic` pitch view
- [x] T86 Cup results by the game's numbers
- [x] T111 `stages` with no rounds
- [x] T112 `report` scorers
- [x] T89 `tactic copy`
- [x] T9 `--term` in any language

## 2. New in FM27

- [x] T10 Tactical tweaks (`fm tweak`)
- [x] T11 TransferRoom (`fm transferroom`): adverts, offers, requirements, agents. Waiting part: T154.
- [x] T12 Squad Planner (`fm planner`). Copy is broken in the game itself.
- [x] T13 Personalised roles, swap two players
- [x] T15 Training week card: copy, paste, clear
- [x] T16 Analysis pages (read only)
- [x] T17 Recruitment objectives and feedback

## 3. Existing commands, missing a part

- [x] T18 `captains --standins`
- [x] T19 `responsibilities delegate / take all`
- [x] T20 `coaches --clear`
- [x] T22 `players --filter`
- [x] T23 `shortlist` lists
- [x] T24 `inbox --read-all`
- [x] T25 `dugout` cards: apply puts a tweak or team instruction on, read back; a wrong number refused. TR 2/2, EN 2/2.
- [x] T157 `dugout` cards, what is left after T25 (live sightings under moments: T160, T161, T163):
  - [x] A card pressed and never taken: the panel is drawn twice and the hidden copy's button was pressed; now only
    the one the game shows (`Visible`). Round 67 (14 Nov): EN 2/2 (3 cards), TR 2/2 (3 cards), each taken at once
  - [x] The minute's wait: the timeline's marks stay listed with the panel away and the whole timeline last read is
    kept, so the list and "nothing to apply" come in 1–5 s (were 60–100 s); a card is done when its change reads back.
    Left as it is: a mark never read with the panel up, or an instruction or mentality card to press, waits for the panel
  - [x] A player card ("Tell Salifou: Shoot Less Often") changes a player instruction (PI_SHOOT_LESS), not a tweak:
    everyone's instructions on the pitch are read before and after (one batch, ~1.2 s) and the moved one said,
    "Player · Instruction: was → now" in the game's words. Measured with a changed instruction in a match, TR + EN, negative case too
- [x] T162 `tactic pi` in a TR game took only the plugin's English ("Şut" refused) and listed choices in English:
  the game's words are now asked for and shown, and matched (as `tactic instructions` since T155). TR + EN, 4 changes each, wrong name refused
- [x] T155 Team instruction names in a Turkish game came out in English: shown through `GameWords` (TR and EN checked)
- [x] T26 `pause` / `resume`
- [x] T27 `holiday --jobs`
- [x] T28 `youth candidates`
- [x] T29 A goal with a change waiting: the game's question (`cancelChanges`), answered Yes. TR 2/2, EN 2/2.
  - Asked only for a team instruction change (taken at the next dead ball); a mentality change is taken at once.
    A penalty goal never asks (the kick is a dead ball). Needs slow speed.
- [x] T97 The match bar's Tactics button
- [x] T113 `talk --json` order
- [x] T114 Deadline day date
- [x] T115 Inbox keeps older messages
- [x] T116 `status` with the vision meeting open
- [x] T117 Message attachments: taken once the game's text binders are ready and two reads agree (`round65` EN + TR 3/3)
- [x] T118 `tour` waiting on the screen
- [x] T119 `news <no>`
- [x] T120 `table` after a match
- [x] T122 `reply` "answered" when nothing happened
- [x] T123 The manager's own contract
- [x] T124 Status flags in the game's words
- [x] T125 `squad` reads every row
- [x] T126 `inbox --all` order
- [x] T127 Collective bonuses
- [x] T128 Inbox Tasks filter left on
- [x] T30 Active tactic's name
- [x] T31 `scout player --cancel`
- [x] T32 `training --trait-suggest` (not in FM27)
- [x] T151 `squad` on match day: reads (cold, twice, after `fixtures`, after `tactic`; TR and EN). In the pre-match
  talk the game has no menus: it now says the match's commands and `talk`, not `next` (3/3 TR, 3/3 EN)

## 4. Pages no command read

- [x] T33 Club history
- [x] T34 Club profile
- [x] T35 Career
- [x] T36 Competition pages
- [x] T37 Transfer and scouting pages
- [x] T38 Squad and medical pages

## 5. Robustness

- [x] T39 `fm crawl` closes a popup that holds the queue
- [x] T40 `fm stop`: the game sometimes hung on its way out (process stuck, not killable). Cause: T92's deaths.
  - [x] Since T92's fix (c5c8b63) 65 stops and loads in the round logs, none hung (longest 45 s): the hangs were T92's.
  - [x] Crash reports: the GC Finalizer deaths (T92) stopped with the fix; a SIGABRT on the game's `Timer-Scheduler`
    thread is still written now and then as the game quits (4 since the fix, each 10–20 s after a stop). Saves are
    written before the quit; 14 Nov 2 more stops: no report, no window on the screen (`/shot` of the desktop).
- [x] T41 `settings language`
- [x] T42 `status` with a held queue
- [x] T43 Squad numbers and list at a season's start
- [x] T95 `next` past FM27's look-only pages
- [x] T96 Names typed as several words
- [x] T91 `fm start` with a dead game on the port
- [x] T92 The game died now and then (one stack: GameAssembly 0x7547c8 → 0x519950 → 0x1964b70 → UnityPlayer 0x29840).
  - Cause: BepInEx's tick fires on any thread's runtime_invoke; the plugin touched Unity and AppKit there (loading
    thread: start hangs; GC Finalizer: deaths). Fixed in c5c8b63: ticks do nothing off the main thread.
  - Since: 13 Nov evening (5 h, 9 matches) and 14 Nov night round62 (the old death moments, TR + EN): kick-off
    submission 5/5 + 5/5, U19 instant result 5/5 + 5/5, TransferRoom delete --loan 3/3 + 3/3. No death, no hang.
- [x] T150 27.0.1 Continue: `match` passes match day, `next` reads unread messages
- [x] T148 A save is never put at risk by the tool.
  - [x] The game's fatal-error dialog is told (written, exit 70). Seeing it on a real one: T158 (moments)
  - [x] T11's delete crash: gone
  - [x] Each death's cause found (T92: ticks off the main thread, c5c8b63)
  - [x] T92 closed; T40 closed or its cause written here
- [x] T88 `tactic clear`
- [x] T94 `tactic slot`
- [x] T87 Playing by hand and by the tool on one install:
  - [x] A game opened from Steam by hand stays visible and normal: Steam's bundle is never touched (no
    `LSUIElement`), BepInEx lives in `~/fm27_bep`, and the player's display settings come back however our
    game ends: `fm stop`, `kill -9` (the launch shell puts them back, 3/3), a reboot (next `fm` command)
  - [x] After a Steam update the tool says so and puts BepInEx back: staged by a changed interop hash, `start`
    said "updated, preparing", regenerated, one game, save loaded. The ready sign is BepInEx's copy written in
    this launch (an old marker had started two games)
  - [x] A game left on a dialog or another screen: every command gets past it. `round70.toml`, EN + TR 3/3 each:
    left on a club page, a competition page, a finance page, an open message, a talk half way. The reads give
    what they gave before, `next` moves the game on, and the talk is still there after them
  - [x] Left on the next opponent's report: `squad` gives our side, not theirs. The report is another club's page,
    and the squad panel stays built for that club: by its GUID alone the page opens for the club on screen. Asked
    again it is our page, so the table is read by the team's name in the data. `round70.toml` ("left on the next
    opponent's report"), EN + TR 3/3.
  - [x] A page asked for while the game is still taking the last press is never sent: the tab it was asked for does
    not open, and the queue keeps that event in Sending for good (measured 30 s and more, then nothing opens).
    A press now waits for the queue to be empty, and a queue that stays held is said (start the game again).
  - [x] The tool changes for good only what the player asked: its own files are its caches; the game's display
    settings are put back however it ends; game settings only through `settings`; read-only tools change
    nothing (`round68.toml`). `next` reads unread messages because FM27 does on Continue, as by hand
- [x] T153 AI safety (from `AI.md`; rules in `AI.md` Safety):
  - [x] Every tool marked read-only, reversible or high-impact, next to its definition
  - [x] CLI, MCP and sentences follow the same rules
  - [x] A bench: nothing that changes the game runs without approval (`SafetyTests.cs`; `round68.toml`: read-only tools change nothing, EN + TR)
  - [x] A run that fails half way says what was done (sentence chains, `opposition`, contract terms; seen in EN + TR)
  - [x] `round68.toml` passes EN + TR (3/3 each half-way test)
- [x] T159 `opposition` right after a load: the club's name over the card took the first row's lists ("A01").
  Each list now goes with its own row; a half-drawn card reads empty. Checked cold, card open, other page open (TR); round68 EN + TR.

## 6. Plugin API changes in FM27

- [x] T44 `TableRowNavigatable` gone
- [x] T45 `m_textField` moved
- [x] T46 `GetDescendantCount` gone
- [x] T47 `IdentifierInfo.DisplayName` gone
- [x] T48 `EventFmxMatchFocusSelect*` gone

## 7. Platform (macOS only today)

- [ ] T49 `fm start` / `stop` on Windows (today: shell script, app bundle, PlistBuddy)
- [ ] T50 Window hiding on Windows (today: osascript)
- [x] T51 `doctor` FM27 paths
- [ ] T52 Publishing for other targets (today: `osx-arm64` only)
- [ ] T53 Plugin on Windows x64 (untried)
- [ ] T54 Save and user folders on Windows (untried)

## 8. Moments still to meet (catch them when they come: `fm crawl out.json --here`)

- [ ] T160 `dugout` mentality card ("Set team mentality"): read back from the match tactic's `Mentality`; see one live.
- [x] T161 `dugout` pressing card in a TR match: the read-back says "from → to" (was "Pres Şiddeti: Çok Şiddetli").
  Closed by review (Faruk, 14 Nov): from and to are the game's ids read before and after; TR words come in only to show
  them (`GameWords.Of`). Seen live EN 14 Nov: "Trigger Press: Much More Often → …".
- [x] T163 `dugout` player card met live: the read-back names the player and the instruction (`PlayerInstructionsChanged`).
  Hunt 14–15 Nov: EN 2/2 ("Ahmet Koşar · Shooting: Standard → Shoot Less Often"), TR 2/2 ("Mirko Sušak · Şut: Standart → Daha Az Şut Çek").
  - Tried 14 Nov: the game's own `EventFmxMatchDugoutGenerateDebugEvent` (InteropEventIDs 961956663), sent by
    `/pluginevent` to a dugout control's target (`DugoutEvent<n>`, after `dugout` resolved it) during a match: taken,
    no card made (likely shut in the release build). Not tried: a payload or another target; `…ClearDebugEvent` beside it.
- [ ] T158 The game's own fatal-error window, met for real: the tool says so (exit 70) and `fm stop` ends it.
  The sign is the main thread in `-[NSAlert runModal]`; the game's `game_plugin` holds NSAlert, runModal, the
  "A serious error was encountered…" text and `show_and_wait_native_error_dialog`. Its only known trigger (T11's
  delete) went with T92's fix.
- [x] T90 Tactical tweaks in extra time. Passed live EN (round 57); TR closed by review (Faruk, 14 Nov): the tweak is put
  on and off by the game's id, read back from TacticActiveTweaks / TacticPendingTweakOps, the period is the game's
  MatchPeriod; names only shown and matched against what was typed.
- [x] T156 An injured man on the pitch: `sub --json` gave `Injured: []` while the game asked `injuredplayer`.
  Found 14 Nov TR (Hrelja, 43'): MatchInjuryType 0, shirt not flagged; his `PlayerStatus` is a list (flag 126), read
  as one value it read nothing. Read as a list: `Injured` named him 3/3, `sub` took him off, `Injured: []` after, the
  match went on. EN met by the hunt 15 Nov (`hunt2.out`, 37').
- [ ] T14 `tactic advice`: the game always says "no advice" (4 saves, both phases). Needs a moment with a critique.
- [x] T55 Season's end: vision meeting, awards, contracts ending, retirements
  - Met 15 Nov TR + EN (scn season-end, 7 May 2029): the season-end team meeting played through by `reply` + `talk` (11 lines,
    moods read back, the talk ends); pre-season dates chosen by `reply … 1 2`, the inbox reads it answered. Vision,
    contracts, retirements and award winners: earlier rounds (round9, round10, round14).
  - This season's club awards (scn hunt-t55-awards-after, 30 May 2033): `reply` reads the message whole (season
    results, club awards, best XI) TR + EN. `inbox --open` right after opening gave "(kind )": now waits for the kind.
- [x] T56 Transfer window and deadline day
- [x] T57 Cup draws (`fm draw`)
- [x] T58 International break
- [x] T59 Youth intake
- [ ] T60 Board meeting and requests; job offers while employed; being sacked
  - Met 15 Nov TR + EN (scn hunt-t60-future-2032): `next` stops at the board's message, `reply … 2` attends, `talk` plays it
    through (moods read back, a wrong number refused), the talk ends and the day goes on. Requests (round16), sacked
    and interviews (round19) were done before.
  - The target accepted there is on the Promises page: `read promises` (scn caught-2029-11-30): TR none before the
    meeting, "5 maçta 7 puan toplanacak · 5 maç kaldı" after; EN "Collect 7 points from 5 matches · 5 games left".
  - Left: a job offer while employed (not met in FM27).
- [ ] T61 Contract renewals: a player asking, an unhappy player's demand
- [x] T62 Pre-season
- [ ] T63 Match: substitution screen, shouts, set pieces edited in a match
  - Done before: shouts, injury window + `sub`, set pieces in a match, extra time, shootout score (EN).
  - `dialog` presses by the game's handler: since 12 Nov every press in the hunts and rounds closed its window
    (35/35, EN + TR: tweak changes, Confirm Tweak, injured player, cancel changes); the one left open was before it.
  - At an injury stop with our hurt man still on, `match` no longer offers "carry on without one" (the game will not).
  - Left: a shootout in a TR match (its board's score).
- [x] T64 Press conferences
- [ ] T98 `talk`: a journalist pressing with the same answers. Not met again yet.
- [ ] T130 `jobs <club> apply` said "sent" when the club turned it down at once
  - No longer told by time ("unchanged within 5 s"): the game's answer is waited for, the post's status moving or a
    new message in the manager's record (`Human.HumanPortalNewestNewMessage`, no page opened). Kind 238 "job
    application received" or a moved status is sent; another kind with the post as it was is a refusal; no answer
    is said as none. 16 Nov EN: Hoffenheim, Saint-Étienne, PEC Zwolle, Willem II sent (238, 2–5 s); `round45.toml`
    EN + TR 3/3. Left: a real turn-down seen.
- [ ] T131 A new club's first days: Continue stuck on "Messages" (check with T150's fix)
- [x] T132 Contract card in Turkish
- [x] T133 `reply "<subject>"` with several of that subject
- [x] T134 Pitch size message
- [x] T136 An open negotiation closed by `player`
- [x] T137 `reply` found no message while another page was over the inbox. Not met again 15 Nov (scn bug-reply-notfound, TR):
  12/12 (3 fresh; after finances, squad, table, training, a player card, data hub, club history, opponent, a news item).
- [x] T138 `match --instant` silent while the game's queue was held. Not met again: 11 Nov, and 15 Nov TR 3/3 (scn kickoff:
  10 lines out, rc 0, `report` has the result).
- [x] T139 The walk past "contracts set to expire"
- [x] T140 That message's table: a decision per player
- [x] T141 `player` opened the wrong player
- [x] T142 Contract card marks a term the game will not take
- [x] T143 `targets` cover count
- [x] T144 `targets --free` goalkeepers
- [x] T145 `offer` on a free agent
- [x] T146 `contract --sign` with a red term
- [x] T147 A new signing's press introduction
- [x] T135 A youth team's match to pick for
- [x] T129 Live ratings after a match
- [ ] T154 TransferRoom `responses` and `activity --loan` tables: need real responses to our adverts

## 9. Depth the crawl has not reached

- [x] T65 Player interaction (`actions <player>`)
- [x] T66 `fm meeting`
- [x] T67 Every list's submenus (tactic presets, Manage Tactics …): `crawl --deep` reads them by value or plugin
  event, EN + TR (`reference/fm27/crawl-deep-*.json.gz`)
- [x] T68 Every row's right-click menu, per kind of row: `crawl --deep`, 209 pages, the same 63 actions EN + TR.
  Person menus: `actions <person>`. A club's menu: `actions <club>`, only where the game draws one (the next
  opponent's card); our own club's requests are `board request`. EN + TR: menu 3/3+, team report asked and read
  back (its entry turns off), a club with no menu and an unknown name refused
  - [ ] A club's id at an on-screen path answers only once copied to a row of ours (`IdsOf`); why, not known
  - [ ] Two clubs of one name and nation (Arsenal, ENG twice) cannot be told apart by `( )`
  - [ ] A requested team report's entry shows `- string#2` (a game string not filled)
  - [ ] `crawl --deep` leaves popups it opened by a button up (seen: Load Item, scouting options, player interest)
- [ ] T77 Menu actions the code never names (46): each has a command, was run through `actions` and read back, or
  needs none. 17 Nov, EN + TR (list in the round note). Open from it:
  - [x] An entry that opens a window (a child of the game's #ModalDialog) is not said "done": its words are shown
    and its buttons, finished with `press`. Add as Transfer Target: press adds it (the DoF's target count 0→1),
    Cancel adds nothing; Set Nickname. EN + TR 3/3; a question and a page link as before
  - [ ] Hire An Intermediary: its answer card comes after `dialog Yes`, which does not read it
  - [ ] Set Nickname: no command types into its box
  - [x] `actions <staff|manager>` on that person's own page: "Nothing can be done". His page was opened again and
    the old page's menu, still listed, opened empty; a menu of his already showing is now taken. EN + TR 3/3
    (staff and manager), and from another page; shortlist add/remove read back
  - [x] Bursts of "not found" after a load: the game had found them (its window said so) but drawn the rows, or
    their names, late. The plugin now says a row without its name (Pending), and a search waits for rows while
    the game's own list (game.Search.Results) holds any; "none" only when it holds none (no 3 s wait). TR watch
    after a language-change load: 96/96 (was 1–3 empty per 5 min). `FM_SEARCHTRACE=1` prints each poll
    - [x] Same watch in EN: 93/93 (TR→EN load, 5.5 min)
  - [x] `players` after Find Similar Players: "did not show the lists expected". Every `players` failed so (since
    93ec8a0): FM27's player database is itself a card, and the view list was skipped inside any card; that is now
    for the squad page only. A squad page opened before also lent its rows to a search: a search now reads only
    rows of a list no team is bound to (`people?teamless=1`; staff search too). EN + TR: 5+/5 across two players,
    from the squad page and from the search page, `--clear`, a quick filter, staff search with the squad bound
  - [x] `planner --position AMC`: "the tile #AMC has nothing to press". The game keeps the pitches of planner pages
    opened before (same tile names, nothing shown, nothing to press); the first by name could be one of them. Tiles
    now say whether they can be pressed, and the one chosen is pressed by its pointer. EN + TR 9/9 across pages
    (3 pitches up), next season; a dead tile's pointer, a wrong pointer, a position not in the tactic refused
  - [ ] No read-back found: Available For U19 Squad, Keep History After Retirement; Trigger Contract Extension
    Clause leaves the end date as it was (the entry goes)
  - [x] `scout staff` "assignments panel not found" (EN too): it pressed a tile's button on the focus page, and
    those tiles can be hidden (the crawl hid them) and now open Scout Priorities. It opens Scouting › Assignments
    by its GUID under its parent. EN 5/5 (tiles shown and hidden, from other pages), TR 4/4 (cold start); one
    scout, as `staff` lists
  - [x] A search took the words before's list for the answer when the game was slow to start (after a load):
    "not found". Now only the game's sign counts (the search started, or the list is another). EN 8/8, TR 6/6;
    one "not found" seen in ~60 since, not reproduced
- [ ] T69 Settings pages; the main menu
- [ ] T70 Pages of a club that is not ours; a nation's and a competition's

## 10. Quality

- [x] T71 Every command has its MCP tool and help line
- [x] T72 Every command reachable by sentence
- [ ] T73 `MANUAL.md` images refreshed for every changed output
- [x] T74 Full sweep passes (`tools/sweep.sh`, `tools/mcp-check.py`): 15 Nov, scn-calm EN 63/63, TR 63/63; MCP 50 called, 0 failed
- [x] T75 `AI-CHECKLIST.md` sorted: safety is T153, the rest is v2 in `AI.md`
- [x] T76 `BACKLOG.md` sorted: tasks made (T151, T152), the rest v2 below or dropped
- [ ] T77 `fm crawl` TR + EN at the end: no event or page without a command (or a reason); menu actions: see section 9
- [x] T100 `:` on the live panel opens a prompt
- [x] T101 Keys on the live panel
- [x] T102 Long commands show progress on one line
- [x] T103 Ctrl-C ends the command, the game goes on
- [ ] T152 Another theme and interface scale: control names, tree depth, row counts still hold

## 11. Acceptance tests (each in one go, tool only, no bridge)

- [ ] T78 Two full seasons through the CLI, Turkish game
- [ ] T79 Two full seasons through MCP, English game
- [ ] T80 A third language: one month through the CLI, sweep passes
- [ ] T81 Windows (Steam): install from the README, one month, sweep, stop
- [ ] T82 Epic Games Store (macOS and Windows): install, one month, stop
- [ ] T83 A clean machine, README only
- [ ] T84 A new career (another country and league), one month, sweep

## 12. Final check (the last box)

- [ ] T85 Audit `cli/fm` and the plugin: nothing held by text, order, position or a fixed wait
  - 15 Nov full scan: no fixed waits (16 paces, all inside sign-held loops); dates come from data as ISO; pixels only
    say "on screen"; rows are built from data paths; English compares go through the game's translation ids.
  - [x] T85a `opponent`: the report opened under Tactics (it was asked for under Home, so FM27 never opened it: no
    tiles, no meetings). The analysts' tiles come from their data (polygonobservation.DataPointList: the game's
    name, short name and true value; the screen caps a value at its axis, 1.13 shown 0,5). A meeting's date is
    taken only when it is a date by shape. TR + EN read; the scope ("1. Lig, This Season") is not in data, dropped.
  - [x] T85b `propose`: FM27 lists the offer in his action menu (PersonOfferToClubs); read back by the menu. TR + EN: sent,
    menu no longer offers it, again refused, another club's player refused. The header's value and wage open their
    panels by GUID (`Screen.PlayerTransferStatus`, `PlayerContract`). EN pass in the language round.
  - [ ] `contract --agent`: the contract panel opens (by GUID), but the game offered no ask for Abifade (24 Jun 2028);
    not met in FM27 yet
  - [x] T85c A message row is pressed by the binding it is drawn from (`MessageRow`: its button's Source is the
    message's path one level up), the list scrolled by its own element. TR + EN: 3 messages each opened, read back as read.
  - [x] T85d `dugout` finds a card again by minute + text. The game gives a card no id: its event index is its place
    and moves when a card goes (15 Nov, TR: 4 became 3). Kept minute (data) + sentence (the card's own, read in this
    language), now both, and the remembered timeline keeps the card's own minute. Live with T166, TR + EN.
  - [x] T85e `OfferScreen.AddPath`: no new row told, no row guessed (it took the last); by review
  - [x] T85f An attribute rule's row is the one whose comparison control was not there before. TR + EN: pace>=12 +
    pace>=16 finds as pace>=16 alone; pace>=15 + stamina>=15 works.
  - [x] T166 The match screen's small Dugout box draws no timeline (its marks are there, not shown, and only the
    first two presses selected anything), so `dugout` walks it with its own arrows (EventFmxMatchDugoutPrevious/
    NextEvent, each closes at its end) and reads `dugoutcard` each step; a walk the box hid in is waited out and done
    again, never given as part of the list. EN: `match --stop-at-advice` stopped at both 35' cards (the old code
    passed a 71' one), list 3/3 the same; a tweak card and an instruction card (by panel) applied, read back. TR: list
    3/3, a tweak and an instruction card ("Pas Karşılama: Dengeli → Koşu Yoluna Pas Yap") applied, read back. The
    box keeps only the latest cards: an old one goes and cannot be applied.
  - [x] T165 `players --attr "x<=n"`: the "Is At Most" list writes nothing when chosen from (its choices are bound
    to 0), so the maximum starts at 20 and is stepped down; the steps stopped at a fixed 6 (20 → 14). Now up to the
    scale's length, each waited for. EN + TR: stamina<=8 finds a player with 8, pace 12..14 works, pace>=17 with
    pace<=14 finds none.
  - [x] T85g The calendar grid is held by its day cells' path; a stage's words by the stage list itself
    (`texts?anchorptr=`, plugin). The label "League Table" had found a news item's text. TR + EN: cup round,
    undrawn final's draw date, calendar month.
  - [x] T85h `register`: rule titles shown as the game writes them (they came Turkish in a Turkish game, so the
    English switch never matched); "Min.", "Replacements" and the flags by the game's own word for them
    (GameWords); positions and statuses in the game's words. Age/position/status still told apart by shape.
    TR + EN read.
  - [x] T85i Small ones, by identity now (`round69.toml` EN + TR 3/3 each):
    - The fee window in a talk and its Confirm, and the assistant's closing button: the one the game shows
      (`Visible`), none guessed when two are (was the last laid out).
    - `mentoring new` had never been ported: FM27 draws the editor inside the Mentoring page, held now by its own
      Cancel / Confirm events; the group it made is the one no group before matches (was the last); Cancel leaves
      it, so it is deleted, aimed by the list's own selection. FM26's "no new group the day one is broken up" is
      gone (FM27: 3/3 not mixed up).
    - `Texts.TopBody`: the game's named page layers (MenuBody, Menu, ReportBody, Report, CardBody, Card, Overlay,
      ModalDialog), the last shown one with something shown in it (was the screen's centre). `read here` TR + EN
      the same as before on a page, a report, a talk; a search window on top found as the top.
    - `?any=1` row lists: hidden told by the same `Shown` every read uses (was a width of 0).
    - `actions` left the game's search window up when it stopped (a wrong entry, a list, a refusal): shut by its
      own ClosePanel, read back by its box going. EN 5/5.
  - [x] T85j `opponent --json`: the summary's keys are stable (Formation, TopScorer…), not our labels

## 13. Later (v2)

- [ ] T21 Opposition scouting priorities (`ScoutTeam`: `actions <next opponent> "Get Team Report›…"` asks for it; the priorities screen not found)
- [ ] Pages only under a "More" menu
- [ ] Read one value without opening its page
- [ ] Swap two starters; a player from outside the squad
- [ ] Youth training
- [ ] Season comparison and trends
- [ ] `fm watch`: report when a player or value changes
- [ ] Condition trend sparkline
- [ ] A web dashboard (the plugin already speaks HTTP)
- [ ] AI v2: see `AI.md`
