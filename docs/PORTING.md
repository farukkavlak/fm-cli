# Porting

How the bridge works, how FM27 was set up, and the traps. Per-command history: git.

## How it works

```
fm (C# CLI, MCP server)  ──HTTP──▶  127.0.0.1:7777  ◀──  FmCliProbe (BepInEx plugin in the game)
                                                         └─ SI.Bindable tree, pages, events
```

- The plugin reads the game's bound data tree and sends the game's own UI events.
- No screen capture, no mouse. The game stays hidden in the background.

## Rules

Rules are in `CLAUDE.md`. Attach order, strongest first: data path + number/enum → data path + `…String` → element name. Never screen text.

## FM27 on Apple Silicon

- `~/fm27_bep/game/`: BepInEx 6 be.755, `run_bepinex_arm64.sh` (copy in `tools/fm27-bep/`), `libdoorstop.dylib`,
  `dotnet_arm64/`, `steam_appid.txt` (4530370). Steam's folder is untouched.
- `~/fm27_bep/fm.app`: shadow bundle (LSUIElement, JIT entitlements, ad-hoc signed). `fm start` launches it.
- What it took:
  1. UnityDoorstop v4.6.0 (the FM26 one read UnityPlayer's chained fixups as zeros).
  2. `dotnet_arm64/`: .NET 10 runtime + Microsoft.Extensions.* 6.0.0.
  3. Interop is generated from a thin x86_64 GameAssembly; the game then runs the real one (the launcher swaps).
  4. `ScanMethodRefs = false` in `BepInEx.cfg`.
  5. Il2CppInterop hooks the wrong function on arm64 since 27.0.1: `InteropHookFix` moves it (T149).
- Game log: `~/Library/Logs/Sports Interactive/Football Manager 27/Player.log`.
- After a game update: `tools/metadump` → `tools/identity-check.py check` lists what the code uses that is gone.

## The bridge in the frame

- Requests run on each UI panel's scheduler, once a frame (`visualTree.schedule.Execute().Every(0)`).
  Running from BepInEx's tick broke pages mid-draw. `health` says `runs from: ui|tick`.
- One run a frame. A press while the UI queue drains is retried next frame (5 s cap).
- Never schedule twice on one panel (the game slowed to a halt).
- Many reads in one frame: `Bridge.Batch`.

## Ways that work in FM27

- Rows: the table's `m_boundElements` + `GetItemFromView` (view place ≠ source place).
- Menus and actions: by `ActionID` (`/actions?action=&who=&data=`), never by the row's words.
- Lazy menus fill on a later frame: `/menukinds?load=1`, then `close=1`.
- Windows: `/dialog` gives `Type` (`QuitGameDialog`, `Confirm<action id>`) or `Panel`; answer until gone.
- Pages moved into submenus: `/pageparent?guid=` names the parent; `Screen.OnBarOf` for bar-only pages.
- A record set beside a node (`/pathset`) to read data that wants `human`, `team` or `Judge` beside it.
- Shown words: `/pull?shown=<props>` keeps the game's words; numbers never go through the translation table.
- Money: data is in pounds; `MoneyFormat.Rate` from `/money`.
- Continue: `/continueflags` (flow, `IsProcessing`); `next` waits on it. The button `continue-button` passes match day.
- Diagnosis: `/tree`, `/props`, `/choicedump` (opens lists), `/attachment?state=1`, `/whatis` (crash frames → methods).

## Traps

Game:

- Success reported, nothing done. Read back.
- An unbound field reads `{}`: "no path" and "not ready" look the same.
- A click on a window while it draws is lost.
- Clicking a disabled button, editing pooled `TypedValue`s, or polling the whole tree (~40k nodes) can lock it.
- Opening a page from the post-match `Results` screen breaks Continue.
- After a language switch the first reads can be in the old language. Save data keeps the language it was made in.
- `IsInjured` is not "cannot play". An injured man on the bench stops a match from starting.
- `PlayerStatus` is a list (injured on the pitch: flag 126). Read as one value it reads nothing.
- A page asked for while the queue is draining never opens, and the event stays in Sending. Wait for an empty queue.
- Panels are kept or drawn twice: hidden copies stay listed (dugout card, planner pitches). Take the one `Visible`.
- A report is another club's page, and the squad panel stays built for that club. Check the team's name in the data.
- Search rows and their names come late. "None" only when `game.Search.Results` holds none.
- The "Is At Most" list writes nothing when chosen from. Step the maximum down from 20.
- A dugout card has no id, and its event index moves when a card goes. The small Dugout box keeps only the latest cards.
- A goal asks about a waiting change only for a team instruction. A mentality change is taken at once; a penalty never asks.
- The game's `Timer-Scheduler` thread sometimes aborts as the game quits (10–20 s after `stop`). Saves are already written.

Ours:

- The plugin loads only at game start. Never copy the DLL while the game runs.
- `127.0.0.1`, not `localhost`.
- `pkill` flags before the pattern.
- Publish `fm` to `dist/fm.next` and rename; writing over a running `fm` breaks it.
- Plugin ticks fire on any thread's runtime_invoke. Touch Unity and AppKit only on the main thread.

## Live panel and radar

- One `batch` request per redraw (1 s). The panel draws over itself (no flicker).
- Radar from `Match2DSimulation.m_data`: normalized positions, kit colours; sides by kit.
- 24-bit colour for pixel and 3D styles; ASCII, coloured ASCII and braille anywhere.
- Key moments: the game's own class of each commentary event (`/moments`).
- No live possession or shots during a match; `report` has them after.
