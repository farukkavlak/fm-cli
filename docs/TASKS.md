# Tasks

One list. Pick by id ("T12"). Tick a box in the commit that does it.
Done = every box ticked; acceptance tests (section 11) last. New work is added here first.
A closed task keeps one line; how it was done and measured is in git. Port notes: `PORTING.md`. Crawl data: `reference/fm27/`.

## 0. Basics

- [x] T0 The game never shows itself
- [x] T104 Scenario saves (`tools/fm-round.py scenarios`)
- [x] T105 One test tool: `tools/fm-round.py`, lists in `tests/live/*.toml`
- [x] T106 `captains --captain / --vice`
- [x] T107 A line starting with a command is that command, every word kept
- [x] T108 `status` keeps the date after a simulated match
- [x] T109 `load` opens the save it was asked for
- [x] T110 A game update no longer breaks `fm start`
- [x] T149 The game starts again since 27.0.1 (`InteropHookFix`)

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
- [x] T11 TransferRoom (`fm transferroom`). Waiting part: T154
- [x] T12 Squad Planner (`fm planner`). Copy is broken in the game itself
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
- [x] T25 `dugout` cards: apply, read back
- [x] T157 `dugout`: hidden copy of a card, the minute's wait, player cards
- [x] T162 `tactic pi` in the game's words
- [x] T155 Team instruction names in the game's language
- [x] T26 `pause` / `resume`
- [x] T27 `holiday --jobs`
- [x] T28 `youth candidates`
- [x] T29 A goal with a change waiting: the game's question answered
- [x] T97 The match bar's Tactics button
- [x] T113 `talk --json` order
- [x] T114 Deadline day date
- [x] T115 Inbox keeps older messages
- [x] T116 `status` with the vision meeting open
- [x] T117 Message attachments
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
- [x] T151 `squad` on match day

## 4. Pages no command read

- [x] T33 Club history
- [x] T34 Club profile
- [x] T35 Career
- [x] T36 Competition pages
- [x] T37 Transfer and scouting pages
- [x] T38 Squad and medical pages

## 5. Robustness

- [x] T39 `fm crawl` closes a popup that holds the queue
- [x] T40 `fm stop` no longer hangs
- [x] T41 `settings language`
- [x] T42 `status` with a held queue
- [x] T43 Squad numbers and list at a season's start
- [x] T95 `next` past FM27's look-only pages
- [x] T96 Names typed as several words
- [x] T91 `fm start` with a dead game on the port
- [x] T92 The game's deaths: plugin ticks do nothing off the main thread
- [x] T150 27.0.1 Continue: `match` passes match day, `next` reads unread messages
- [x] T148 A save is never put at risk by the tool
- [x] T88 `tactic clear`
- [x] T94 `tactic slot`
- [x] T87 Playing by hand and by the tool on one install
- [x] T153 AI safety (rules in `AI.md`)
- [x] T159 `opposition` right after a load

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

- [ ] T160 `dugout` mentality card ("Set team mentality"): read back from the match tactic's `Mentality`; see one live
  - `EventFmxMatchDugoutGenerateDebugEvent` sent to a dugout control made no card. Not tried: a payload, another target
  - Hunt 19 Nov (9 matches, 37 cards): advice to push for the win or to play more directly came 3 times TR, 1 EN; `dugout apply` said Done false each time
- [ ] T171 `dugout apply` cards with nothing read back (hunt 19 Nov): an added team instruction Done false (TR); a removed team instruction and an opposition instruction Done true, no change listed
  - Cause of the Done false: cards taken while the match played (the hunt took them every 8 s), and cards whose tweak clashes with one on: the game asks "Change From Tactical Tweak?" first. `dugout apply` now holds the match, and answers that question only with `--yes`
  - Left: 1 of 3 EN: after a card was declined (75'), `dugout apply --yes` said "could not be opened in the panel; the list may have changed"
- [x] T161 `dugout` pressing card in a TR match
- [x] T163 `dugout` player card met live
- [ ] T158 The game's own fatal-error window: the tool says so (exit 70) and `fm stop` ends it
  - Sign: the main thread in `-[NSAlert runModal]`. Its only known trigger went with T92's fix
- [x] T90 Tactical tweaks in extra time
- [x] T156 An injured man on the pitch
- [ ] T14 `tactic advice`: the game always says "no advice" (4 saves, both phases). Needs a moment with a critique
- [x] T55 Season's end: vision meeting, awards, contracts ending, retirements
- [x] T56 Transfer window and deadline day
- [x] T57 Cup draws (`fm draw`)
- [x] T58 International break
- [x] T59 Youth intake
- [ ] T60 Board meeting and requests; job offers while employed; being sacked
  - Left: a job offer while employed (not met in FM27)
- [ ] T61 Contract renewals: a player asking, an unhappy player's demand
- [x] T62 Pre-season
- [ ] T63 Match: substitution screen, shouts, set pieces edited in a match
  - Left: a shootout in a TR match (its board's score)
- [x] T64 Press conferences
- [ ] T98 `talk`: a journalist pressing with the same answers. Not met again yet
- [x] T130 `jobs <club> apply` said "sent" when the club turned it down at once
  - The game's answer is now waited for (`round45.toml` EN + TR 3/3)
  - Real turn-downs seen (19 Nov, TR, 7 applications): the game always answers "application received" first; the refusal comes days later and `next` lists it as new. A refusal at the press itself was not met
- [x] T131 A new club's first days through the tool: briefings, the tactics tour, captains, then `next` moves on (round77 TR+EN 3/3)
- [x] T132 Contract card in Turkish
- [x] T133 `reply "<subject>"` with several of that subject
- [x] T134 Pitch size message
- [x] T136 An open negotiation closed by `player`
- [x] T137 `reply` with another page over the inbox
- [x] T138 `match --instant` with the queue held
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
- [x] T67 Every list's submenus (`crawl --deep`)
- [x] T68 Every row's right-click menu, per kind of row
  - [ ] A club's id at an on-screen path answers only once copied to a row of ours (`IdsOf`); why, not known
  - [ ] Two clubs of one name and nation (Arsenal, ENG twice) cannot be told apart by `( )`
  - [ ] A requested team report's entry shows `- string#2` (a game string not filled)
  - [x] `crawl --deep` closes the windows its buttons open
- [ ] T77 Menu actions the code never names (46): each has a command, was run through `actions` and read back, or needs none
  - [ ] By sentence: the rules route "kutuya X yaz" to `fm_type`, but the live model does not know the tool yet (retrain)
  - [ ] No read-back found: Available For U19 Squad, Keep History After Retirement; Trigger Contract Extension
    Clause leaves the end date as it was (the entry goes)
- [x] T69 Settings pages; the main menu
  - [ ] A list of bare numbers (3D, colour blind) gets its words from its open rows by their place in the list:
    no identity found; every change is read back
  - [ ] Blocks with no control on the list are not settings here: data language, save location, shortcuts, online,
    third party
- [x] T70 Pages of a club that is not ours; a nation's and a competition's (`read --of`)
  - [ ] Slow: 15–20 s a page, most of it `read`'s table scroll (as `read here`)

## 10. Quality

- [x] T71 Every command has its MCP tool and help line
- [x] T72 Every command reachable by sentence
- [ ] T73 `MANUAL.md` images refreshed for every changed output
- [x] T74 Full sweep passes (`tools/sweep.sh`, `tools/mcp-check.py`)
- [x] T75 `AI-CHECKLIST.md` sorted
- [x] T76 `BACKLOG.md` sorted
- [x] T77 `fm crawl` TR + EN at the end: 193 of 209 pages open in both; each event's reason in `reference/fm27/events-no-command.md`; menu actions: see section 9
- [x] T170 `advice`: the staff's advice by area, one applied or marked seen, read back by the game's flags (round76 TR+EN 3/3)
  - Advice that opens a card of its own (a talk, a contract offer, a staff signing) is listed, not carried out
- [x] T100 `:` on the live panel opens a prompt
- [x] T101 Keys on the live panel
- [x] T102 Long commands show progress on one line
- [x] T103 Ctrl-C ends the command, the game goes on
- [x] T152 Text scaling, information display, colour blind mode and resolution: answers do not follow the screen
- [ ] T167 Game froze once for 6+ minutes: main thread inside the .NET JIT, compiling plugin code; not reproduced
- [ ] T168 `read club-history` gave its table as loose lines in 1 of 10 sweeps; not reproduced
- [ ] T172 `opposition` after a match, on the menu-less messages screen: "no subtab bar … ask again" every time; passed after `next` (TR, 19 Nov)
  - Not reproduced: an instant match's end (right message), a press conference waiting or open, the advice panel up. Seen after a played match with its talk open
- [ ] T169 Each resolution change takes the game longer to recover (about 3 minutes by the 16th)

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
  - [x] T85a `opponent`
  - [x] T85b `propose`
  - [ ] `contract --agent`: the panel opens by GUID, but the game has offered no ask yet (not met in FM27)
  - [x] T85c A message row is pressed by its binding
  - [x] T85d `dugout` finds a card by minute + its own sentence (the game gives a card no id)
  - [x] T85e `OfferScreen.AddPath`
  - [x] T85f An attribute rule's row
  - [x] T166 `dugout` walks the small Dugout box by its own arrows
  - [x] T165 `players --attr "x<=n"`
  - [x] T85g Calendar grid and a stage's words
  - [x] T85h `register`
  - [x] T85i Fee window, `mentoring new`, `Texts.TopBody`, `?any=1` row lists, the search window `actions` left up
  - [x] T85j `opponent --json` keys
  - [x] T85k Mentality and style lists, wage total, value limit, last match, tactic panels
  - [x] T85l Last sweep (19 Nov): fixed waits are all a poll's pace; order only in the CLI's own lists or a row's shape; no position, colour or currency. Fixed: the squad table's injured mark looked for "Inj"

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
