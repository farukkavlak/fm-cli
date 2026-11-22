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

## 10. Quality

- [ ] T173 First start under a fresh macOS user: which permission prompts (Accessibility, Screen Recording) window hiding and `/shot` raise, and whether the tool says so. The clean install (T83, 22 Nov) ran on a machine that had granted them already
- [ ] Contract card: a later season's playing-time promise row (future-playing-times-list) now read with its name (TR seen 22 Nov, Malec); EN still to see
- [ ] Contract card: a clause with two values (international caps wage rise: caps and wage) breaking a rule is shown red by the game's row colour only; the tool does not mark that row (no identity found yet), though it says a term breaks a rule
  - 22 Nov: not its list's greyed choices (they follow the value chosen: 5 greyed with 5, 10 with 10); the row's colour is the only sign found, and rows with a note (Appearance Fee…) are coloured too
- [ ] `tactic advice` names a position by the game's own short form in its critique ("MCR" / "OSMSğ") while `tactic` and `tactic role` take "MR (centre)" / "OM (sağ)": find the critique's position field and show it as `tactic` does (T14 seen 22 Nov: TR 3/3, EN, gone once the role was changed, back when changed back)
  - 22 Nov: the critique is one finished text (EvaluationCritiques is a list of strings) and its short form ("OSMSğ") is in neither the engine's position codes nor the translation file: no identity yet

## 11. Acceptance tests (each in one go, tool only, no bridge)

- [ ] T81 Windows (Steam): install from the README, one month, sweep, stop

## 12. Final check (the last box)

- [ ] T85 Audit `cli/fm` and the plugin: nothing held by text, order, position or a fixed wait
  - Swept clean 22 Nov, twice (the second after the youth pages, scout teams and TransferRoom fixes): no new text, order or fixed-wait dependency; run again after T81
  - 21 Nov: the instant result's "match before" check held by the fixture's day (`FixtureDate`), not the opponent's name; the old key read a `Date` the fixture does not have (empty, and a 1.5 s wait each read)
  - Weakest still held: game words asked of the game's own table (registration flags, offer locks, `Response Required`); they work in every language, but lean on the game's English keys

## 13. Later (v2)

- [ ] `dugout` mentality card ("Set team mentality") seen live and read back (T160); not met in 15+ matches
- [ ] A penalty shootout seen in a TR match (its board's score); not caught so far
- [ ] Change a youth team's schedule (`schedule set/style/copy --team`); reading it works (`schedule --team`, 22 Nov)
- [ ] `fm watch`: report when a player or value changes
- [ ] A web dashboard (the plugin already speaks HTTP)
- [ ] AI v2: see `AI.md`
