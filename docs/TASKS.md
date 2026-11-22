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

## 11. Acceptance tests (each in one go, tool only, no bridge)

- [ ] T81 Windows (Steam): install from the README, one month, sweep, stop

## 12. Final check (the last box)

- [ ] T85 Audit `cli/fm` and the plugin: nothing held by text, order, position or a fixed wait
  - Swept clean 22 Nov, twice (the second after the youth pages, scout teams and TransferRoom fixes): no new text, order or fixed-wait dependency; run again after T81
  - 21 Nov: the instant result's "match before" check held by the fixture's day (`FixtureDate`), not the opponent's name; the old key read a `Date` the fixture does not have (empty, and a 1.5 s wait each read)
  - Weakest still held: game words asked of the game's own table (registration flags, offer locks, `Response Required`); they work in every language, but lean on the game's English keys

## 13. Later (v2)

- [ ] `tactic advice` with a real critique (T14): the game said "no advice" in every save tried
- [ ] `dugout` mentality card ("Set team mentality") seen live and read back (T160); not met in 15+ matches
- [ ] A player asking for a new contract himself (T61); not met yet
- [ ] A penalty shootout seen in a TR match (its board's score); not caught so far
- [ ] Change a youth team's schedule (`schedule set/style/copy --team`); reading it works (`schedule --team`, 22 Nov)
- [ ] `fm watch`: report when a player or value changes
- [ ] A web dashboard (the plugin already speaks HTTP)
- [ ] AI v2: see `AI.md`
