# Tasks

One list. Pick by id ("T12"). Tick a box in the commit that does it.
Done = every box ticked; acceptance tests (section 11) last. New work is added here first.
Only open work is listed; what was done, and how it was measured, is in git. Port notes: `PORTING.md`. Crawl data: `reference/fm27/`.

## 7. Platform (macOS only today)

- [ ] T49 `fm start` / `stop` on Windows
  - 22 Nov, written ahead (cli/fm/Core/Launcher.Windows.cs): game found in the Steam libraries (registry + libraryfolders.vdf), exe by Unity's layout, SteamAppId from the app manifest, BepInEx's doorstop in the game folder; stop/alive by process. Untested
- [ ] T50 Window hiding on Windows
  - 22 Nov, written ahead: the plugin's WindowHider makes the process's windows layered, alpha 0, click-through and off the taskbar; the second-copy notice is a message box. Untested
- [ ] T52 Publishing for other targets
  - 22 Nov: `tools/package-windows.sh` builds fm.exe (win-x64), the plugin and BepInEx be.755 win-x64 into a zip with `install.ps1`; built here, not yet installed on Windows
- [ ] T53 Plugin on Windows x64 (untried; its main-thread check now has a Windows way: the thread Load runs on)
- [ ] T54 Save and user folders on Windows (the game says where; AppData/LocalLow before it has; `fm doctor` has Windows checks)

## 9. Ease of use

Measured 23 Nov (TR, scn-calm): fixtures 1.3 s, status 2, inbox 4, table 5.5, squad 5.5–7, player 7.7, targets 9, diff 13, advice 15.

Speed

- [ ] T176 A read asked again with the clock unmoved answers from the last one
  - [ ] Key: the game's date and hour (`state`), the save, the language and the command with its words
  - [ ] Forgotten after any command that changes the game (sell, train, tactic, reply …): the list kept beside `CommandNames`
  - [ ] Which commands may be kept: reads only; MCP `fm_*` reads too
  - [ ] `--fresh` reads again
  - [ ] Measure: squad twice in a row; a change (tactic, sub) then squad shows it
- [ ] T177 Where `squad`, `player` and `advice` spend their time
  - [ ] Time each step: page open, drawn, rows bound, read, waits
  - [ ] Drop waits that are not the game's own sign; keep the old if the new is not faster on the same tests
- [ ] T178 The session reads ahead: the squad's names for tab, the state for the prompt, while the line is being typed

Session (`fm›`)

- [ ] T179 The prompt says where things stand: date, days to the next match, answers waiting
  - [ ] From the data tree only (`state`, `Human.HumanPortalUnreadMessages`), no page opened
  - [ ] Read after each command that moved the clock, not on a timer
- [ ] T181 A number alone picks from the last list shown
  - [ ] `inbox` then `3`: opens message 3 (`reply 3`)
  - [ ] `players` / `search` then `3`: that player's card
  - [ ] Said which list it took it from; nothing when no list was shown
- [ ] T182 Tab completes more
  - [ ] Sub-commands (`scout new`, `tactic role`, `schedule set`)
  - [ ] Competition names for `table`, `stages`, `league` (the game's own list)
  - [ ] Staff names for `staff`, `training --coach`, `scout --by`
  - [ ] Message numbers from the last `inbox`

Help and finding things

- [ ] T184 `help` in groups
  - [ ] `help`: about 15 lines, one per group, and the five commands used most
  - [ ] `help <group>`: start, squad, match, transfers, training, club, settings
  - [ ] Every command in one group; a test fails when one is in none
  - [ ] MANUAL and README follow the groups
- [ ] T186 `todo`: what wants doing now, each line with its command
  - [ ] Waiting answers, offers, contract talks, injuries before a match, board concerns
  - [ ] Reads only what `status` and `doctor` read

Output

- [ ] T187 `inbox` easier to read
  - [ ] A day heading over each day's messages, from the message's own date field
  - [ ] Unread as one mark (●), not a word on every row
  - [ ] Awaiting an answer stands out (colour, and first under its day)
  - [ ] Numbers stay what `reply` takes (`inbox-numbers.json`)

Playing

- [ ] T190 One short command to go on to match day, with the news on the way (T174's lines)
- [ ] T191 `prep` as one pre-match screen: opponent report, picked side, injuries, tactic familiarity
- [ ] T192 A reminder in the prompt when no save has been made for N game days (says it, never saves)

Start and first use

- [ ] T193 `start` / `load` show the game's own stages while waiting: process up, plugin ready, menu built, save loading
- [ ] T194 First session: four or five lines on what to type, tab and the arrows; once only

## 10. Quality

- [ ] T173 First start under a fresh macOS user: which permission prompts (Accessibility, Screen Recording) window hiding and `/shot` raise, and whether the tool says so. The clean install (T83, 22 Nov) ran on a machine that had granted them already


## 11. Acceptance tests (each in one go, tool only, no bridge)

- [ ] T81 Windows (Steam): install from the README, one month, sweep, stop

## 12. Final check (the last box)

- [ ] T85 Audit `cli/fm` and the plugin: nothing held by text, order, position or a fixed wait
  - Swept clean 22 Nov, twice (the second after the youth pages, scout teams and TransferRoom fixes): no new text, order or fixed-wait dependency; run again after T81
  - 21 Nov: the instant result's "match before" check held by the fixture's day (`FixtureDate`), not the opponent's name; the old key read a `Date` the fixture does not have (empty, and a 1.5 s wait each read)
  - Weakest still held: game words asked of the game's own table (registration flags, offer locks, `Response Required`); they work in every language, but lean on the game's English keys

## 13. Later (v2)

- [ ] AI v2: see `AI.md`
