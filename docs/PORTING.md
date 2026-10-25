# Porting to FM27

What carries over from the FM26 work, what must be rebuilt, and how to check the first day.
The code and `docs/reference/` hold the details. Open work and ideas: `docs/BACKLOG.md`.

## Overview

`fm` is a terminal client for the real Football Manager. It does not reimplement the game: a
BepInEx plugin inside the game exposes the game's data tree and events over HTTP, and the CLI
reads and drives that.

```
fm (C# CLI)  ──HTTP──▶  127.0.0.1:7777  ◀──  FmCliProbe (BepInEx plugin, in the game)
MCP server (same CLI)                        └─ SI.Bindable tree + navigation + events
```

The game stays open in the background. No screen capture, no mouse, no vision.

## Rules

- **Bind by identity, not by text.** Data path, event name, game id, page GUID. Screen words are
  the last resort.
- **Wait for the game's signal.** A value changed, a row was drawn, `Processing` ended, a page
  bound. A timeout is only an upper bound.
- **Do not replace one dependency with another.** Swapping a fixed delay for a label or a style is
  still a dependency. Remove all of them together.
- **Verify results.** FM often says it did something and does nothing. Read the result back.
- **Keep questions and actions apart.** A path that asks must not change the game.
- **Measure before coding.** Three states (page first open, already open, another page open) and
  two languages (Turkish and English).
- **Readable output first.** A working command is not enough; the terminal output must be clear and quick.

Attach order, strongest first:

1. Data path + number/enum field, event name, game id, page GUID.
2. Data path + a `…String` field whose value comes in the game's language.
3. Control/element name, `up=N`, row order.
4. Screen text or sentence parsing.

New code starts at 1. If it must go lower, say why in a comment.

## What carries over

Most of the plumbing is independent of the game's content:

| Part | Where |
|---|---|
| HTTP bridge (plugin side) | `plugin/FmCliProbe/src/Bridge/` |
| IL2CPP helpers, spans, key paths | `plugin/FmCliProbe/src/Bridge/`, `Reads/` |
| Game translation table layer | `plugin/FmCliProbe/src/Reads/Text.cs` |
| Bridge client, `Until` (wait for a signal) | `cli/fm/Core/Bridge.cs` |
| CLI language layer (English source, Turkish catalog, `fm lang` / `FM_LANG`) | `cli/fm/Core/Labels.cs`, `Lang.cs`, `cli/fm/labels-turkish.tsv` |
| Game words, money and number format | `cli/fm/Core/GameWords.cs`, `MoneyFormat.cs`, `EngineTexts.cs` |
| Game folders and launch | `cli/fm/Core/GameFolders.cs`, `Launcher.cs` |
| MCP server | `cli/fm/Core/Mcp.cs`, `Mcp.Tools.cs` |
| Bulk dumps and scans | `tools/sigdump/`, `tools/sweep.sh`, `tools/buttons-catalog.py` |

Must be rebuilt against the new game: page GUIDs (`Match/Screens.cs`), data paths, event names,
roles, and any feature the game changed.

## Repo plan for FM27

Stay in this repository. Move the FM26 tree to `archive/fm26/` with `git mv` (history is kept), then
start the FM27 code clean at the root. The archive is the reference for "how did this screen work,
which event did it send". Review every copied part against the rules above; do not copy text-,
timing- or order-based code blindly.

Apple Silicon: the official BepInEx does not work. FM26 needed the patched package from
DadMych/fm26-player-export-macos. If there is no FM27 package, patch it ourselves; the FM26 patch
is the starting point.

## First day with FM27

Work in this order; each answer changes the size of the job.

1. Unity version, IL2CPP, and whether `global-metadata.dat` is encrypted (FM26: Unity 6000.0.52f1,
   not encrypted).
2. Does BepInEx start on arm64? Is there a patched FM27 package?
3. Run Il2CppDumper. Do `FM.UI`, `FMGame`, `SI.Bindable`, `SI.UI` still exist?
4. Does `SI.Bindable.Bindings` + `BindingPath.Create(string)` still exist? If yes, most of the
   project ports.
5. Did the private fields the plugin uses change (`m_choices`, `m_key`, `m_selectionIndex`,
   `m_boundElements`… about 25)? They are in `plugin/FmCliProbe/src`.
6. Are the page GUIDs (`cli/fm/Match/Screens.cs`, 36) and event names (`OpenSubtabEvent`,
   `SpeakToAction`, `EventFmxClearChanges`…) the same? Compare from the dump.
7. Diff the data paths (`Human.Team…`, `Club.Teams`, `PlayerTraining`…) against the FM26 schema.
8. Is the translation table / `languages.fmf` the same format?
9. Run `tools/sweep.sh`, then the same commands in both languages, and list what broke.

Approach: diff the static dumps first, rebuild only what broke. Identity-based commands should need
little work; text- and position-based ones must be rewritten.

## FM26 traps that may return

- The game reports success and does nothing. Read the result back.
- An unbound field returns `{}`; "no path" and "not ready yet" look the same.
- Scrolling fills table rows in several steps; wait for two equal reads in a row.
- Do not mutate the game's `TypedValue`s in place (they come from a pool; the main thread locks).
  Do not click a disabled button; the game locks. Do not walk the whole tree in a polling loop
  (~40,000 nodes; the game crashes).
- Copying the plugin DLL while the game is open does nothing: it loads only at start (about 1.5 min).
- The game's auto-save overwrites the main save; "quit without saving" loses progress.
- Opening a page from the post-match `Results` screen breaks Continue; Match Day › Overview stops
  drawing.
- Deleting and recreating a mentoring group on the same game day mixes the groups up; the game
  refreshes its own copies when the day advances.
- Mods that force a display resolution (FM26DisplayFix) draw pages empty.
- Use `127.0.0.1`, not `localhost` (it answers "Not Found"). On macOS, put `pkill` flags before the
  pattern; it once killed the wrong processes.
- Some actions (mutual termination) apply without asking. Re-read a list before answering by number.

## Fragility review (FM26)

What can still break when the language, theme, terminal, machine or game version changes:

- **English labels in the match flow.** `Confirm Changes` (match stop), `Continue` (tour),
  `Confirm` (set-piece wizard) and the mentality choice `Very Attacking` are matched by English
  text. The plugin translates the game's UI to English, so they work while that table is complete;
  a new game version or a missing translation breaks them. Prefer event ids
  (`EventFmxConfirmChanges`) and element names when porting.
- **Live panel.** It uses ANSI clear/colours and `Console.KeyAvailable`; this is a POSIX terminal
  and macOS. The width adapts to the terminal (56–86 columns), but below that the panel wraps.
  Keys are off when input is redirected, so scripts and MCP keep plain output.
- **Live stats.** During a match the bindings only hold `LastFixture`/`NextFixture` and
  `IsLiveMatch` is false; no live possession/shots. The post-match report has them.
- **Match speed.** The match screen has no speed dropdown; `+`/`-` looks for one with `1x…`
  choices and otherwise tells the user to use settings.
- **Language reverse lookup.** `Labels.Source` builds an English reverse table from the Turkish
  catalog; duplicate Turkish values can map to the wrong English key. Settings names are unique;
  keep new translated labels unique or pass the English key explicitly.
- **MCP.** The tools run the CLI, so their text follows the CLI/game language. Set
  `FM_LANG=english` (or `fm lang english`) when a model needs English. Read tools are tested with
  the Python SDK; write tools are not.
- **Another machine.** Install and launch are macOS arm64 + Steam only; `fm start` window
  minimising needs Accessibility; Intel, Epic, Game Pass and Windows are out of scope.
- **Another game.** Page GUIDs, data paths and event names are FM26-specific; start from
  `docs/PORTING.md` and the dumps in `docs/reference/`.

## State at the FM26 stop

- Working: 100+ CLI commands and MCP tools — squad, tactics, training, transfers, contracts,
  scouting, matches, press, staff, finances, settings. One full season was played end to end.
- Portability work was unfinished: parts of match narration and a few screens still depended on
  text, timing or order. `tools/sweep.sh` shows what breaks in a new save or version.
- Known limits: only macOS arm64 + Steam was tested. Intel, Epic, Game Pass and Windows were not.

## FM27 plan

1. Identity-based from day one.
2. Test every command in Turkish and English from day one.
3. Keep a second save from day one: another country, another league, a new career.
4. No fixed waits. Only `Until(signal, upperBound)`.
5. Think about install and release early: one place for platform paths, a version guard (the plugin
   knows the game version and refuses unsupported ones), `fm doctor` checks the install.
6. Readable, quick output before more commands.
7. A local natural-language layer comes later, once the base is solid.
