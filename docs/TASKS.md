# Tasks

One list. Pick by id ("T12"). Tick a box in the commit that does it.
Done = every box ticked; acceptance tests (section 11) last. New work is added here first.
Only open work is listed; what was done, and how it was measured, is in git. Port notes: `PORTING.md`. Crawl data: `reference/fm27/`.

## 7. Platform

Windows was tried on 24 Nov 2026 on an AWS g4dn (Windows Server 2022, Steam, English): install, start/stop, load/save,
the read sweep, two weeks of play (instant and live matches, talks, subs, a cup draw), tactics, a transfer from offer to signing.

- [ ] T198 The patched BepInEx core (`IL2CPPChainloader.MainThreadTick`, the plugin needs it) exists only as this Mac's built
  `BepInEx.Unity.IL2CPP.dll`; its source is gone. Write the patch again against be.755 (3fab71a) and build both platforms from it
- [ ] T199 A look at `fm` in a Windows terminal by eye: ✓ and Turkish letters, Tab, the arrows, ctrl-R (the README's
  Windows section and the release's Windows zip are in; the console is put on UTF-8, its bytes checked)

## 9. Ease of use

Measured 23 Nov (TR): fixtures 1.3 s, status 2, inbox 4, table 5.5, squad 1.2 (was 6.6), player 2.0 for one of ours (was 5.8; opened by his record, no search), targets 9, diff 13, advice 5.6 (was 13).

Speed

Session (`fm›`)


Help and finding things


Output


Playing



Start and first use


## 10. Quality

- [ ] T173 First start under a fresh macOS user: which permission prompts (Accessibility, Screen Recording) window hiding and `/shot` raise, and whether the tool says so. The clean install (T83, 22 Nov) ran on a machine that had granted them already


## 11. Acceptance tests (each in one go, tool only, no bridge)


## 12. Final check (the last box)

- [ ] T85 Audit `cli/fm` and the plugin: nothing held by text, order, position or a fixed wait
  - Swept clean 22 Nov, twice (the second after the youth pages, scout teams and TransferRoom fixes): no new text, order or fixed-wait dependency; run again after T81
  - 21 Nov: the instant result's "match before" check held by the fixture's day (`FixtureDate`), not the opponent's name; the old key read a `Date` the fixture does not have (empty, and a 1.5 s wait each read)
  - Weakest still held: game words asked of the game's own table (registration flags, offer locks, `Response Required`); they work in every language, but lean on the game's English keys

## 13. Later (v2)

- [ ] AI v2: see `AI.md`
