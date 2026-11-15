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
- [ ] T87 Playing by hand and by the tool on one install:
  - [ ] A game opened from Steam by hand stays visible and normal
  - [ ] After a Steam update the tool says so and puts BepInEx back (or says how)
  - [ ] A game left on a dialog or another screen: every command gets past it
  - [ ] The tool changes for good only what the player asked
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
- [ ] T156 An injured man on the pitch: `sub --json` gave `Injured: []` while the game asked `injuredplayer`.
  Found 14 Nov TR (Hrelja, 43'): MatchInjuryType 0, shirt not flagged; his `PlayerStatus` is a list (flag 126), read
  as one value it read nothing. Read as a list: `Injured` named him 3/3, `sub` took him off, `Injured: []` after, the
  match went on. Left: the same met once in EN by the hunt (tests/live/hunt1.toml).
- [ ] T14 `tactic advice`: the game always says "no advice" (4 saves, both phases). Needs a moment with a critique.
- [ ] T55 Season's end: vision meeting, awards, contracts ending, retirements
  - Met 15 Nov TR + EN (scn season-end, 7 May 2029): the season-end team meeting played through by `reply` + `talk` (11 lines,
    moods read back, the talk ends); pre-season dates chosen by `reply … 1 2`, the inbox reads it answered. Left: vision, awards, retirements.
- [x] T56 Transfer window and deadline day
- [x] T57 Cup draws (`fm draw`)
- [x] T58 International break
- [x] T59 Youth intake
- [ ] T60 Board meeting and requests; job offers while employed; being sacked
  - Met 15 Nov TR + EN (scn hunt-t60-future-2032): `next` stops at the board's message, `reply … 2` attends, `talk` plays it
    through (moods read back, a wrong number refused), the talk ends and the day goes on. Left: requests, offers, sacked.
- [ ] T61 Contract renewals: a player asking, an unhappy player's demand
- [x] T62 Pre-season
- [ ] T63 Match: substitution screen, shouts, set pieces edited in a match
- [x] T64 Press conferences
- [ ] T98 `talk`: a journalist pressing with the same answers. Not met again yet.
- [ ] T130 `jobs <club> apply` said "sent" when the club turned it down at once
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
- [ ] T67 Every list's submenus (tactic presets, Manage Tactics …)
- [ ] T68 Every row's right-click menu, per kind of row
- [ ] T69 Settings pages; the main menu
- [ ] T70 Pages of a club that is not ours; a nation's and a competition's

## 10. Quality

- [x] T71 Every command has its MCP tool and help line
- [x] T72 Every command reachable by sentence
- [ ] T73 `MANUAL.md` images refreshed for every changed output
- [x] T74 Full sweep passes (`tools/sweep.sh`, `tools/mcp-check.py`): 15 Nov, scn-calm EN 63/63, TR 63/63; MCP 50 called, 0 failed
- [x] T75 `AI-CHECKLIST.md` sorted: safety is T153, the rest is v2 in `AI.md`
- [x] T76 `BACKLOG.md` sorted: tasks made (T151, T152), the rest v2 below or dropped
- [ ] T77 `fm crawl` TR + EN at the end: no event or page without a command (or a reason)
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
  - [ ] T85a `Opponent.cs` analyst tiles: numbers by draw order, labels matched by text ("Gl/Gm")
  - [x] T85b `propose`: FM27 lists the offer in his action menu (PersonOfferToClubs); read back by the menu. TR: sent,
    menu no longer offers it, again refused, another club's player refused. The header's value and wage open their
    panels by GUID (`Screen.PlayerTransferStatus`, `PlayerContract`). EN pass in the language round.
  - [ ] `contract --agent`: the contract panel opens (by GUID), but the game offered no ask for Abifade (24 Jun 2028);
    not met in FM27 yet
  - [ ] T85c A message row pressed and scrolled to by its title (`Clock.cs`, `Program.Reply.cs`, `Program.Inbox.cs`):
    two messages with one title
  - [ ] T85d `dugout` finds a card again by minute + text. The game gives a card no id: its event index is its place
    and moves when a card goes (15 Nov, TR: 4 became 3). Kept minute (data) + sentence (the card's own, read in this
    language), now both, and the remembered timeline keeps the card's own minute. Live check in the next hunt.
  - [ ] T85e `OfferScreen.AddPath` falls back to the last row
  - [ ] T85f `StaffSearch.Rules.cs`: the row just added found by the attribute's text, last of many
  - [ ] T85g `Calendar.cs`, `Stages.Lists.cs`: the page held by the month's or stage's shown name
  - [ ] T85h `Registration.cs`: rules told by English title, one label Turkish in every language ("en az"), flags by
    shown letters ("HGC", "Inj"), age/position/status told apart by shape
  - [ ] T85i Small: "the last of many" in the fee window (`Guided.cs`), new mentoring group, `Acknowledge.cs`,
    `?any=1`; `Texts.TopBody` picks the screen's centre. Done: `targets` reads the budgets from data, not our label.
  - [ ] T85j `opponent --json`: the summary's keys are our translated labels ("Diziliş"), not stable keys

## 13. Later (v2)

- [ ] T21 Opposition scouting priorities (`ScoutTeam` action exists; no screen found)
- [ ] Pages only under a "More" menu
- [ ] Read one value without opening its page
- [ ] Swap two starters; a player from outside the squad
- [ ] Youth training
- [ ] Season comparison and trends
- [ ] `fm watch`: report when a player or value changes
- [ ] Condition trend sparkline
- [ ] A web dashboard (the plugin already speaks HTTP)
- [ ] AI v2: see `AI.md`
