# Code layout

## Layers

| Layer | Where | Job |
|---|---|---|
| CLI entry | `cli/fm/Program.cs` | `main`, command routing, session, version check |
| CLI commands | `cli/fm/{Core,Match,Squad,Transfer,Club}/` | one command or read; all are part of the `Program` partial |
| Game bridge | `cli/fm/Core/Bridge.cs` | HTTP, port, errors, timeouts |
| Game reads | `cli/fm/Core/Reads*.cs`, `Core/Clock*.cs`, `Match/Guided*.cs`, `Match/MatchDay*.cs` | data paths, pages, match flow |
| Formatting | `cli/fm/Core/Table.cs`, `Ui.cs`, `MoneyFormat.cs`, `JsonOut.cs`, `InboxView.cs` | tables, terminal, money and numbers |
| Text | `cli/fm/Core/Labels.cs`, `Lang.cs`, `GameWords.cs`, `EngineTexts.cs` | English source, Turkish catalog, language choice, the game's words |
| Infrastructure | `cli/fm/Core/Launcher.cs`, `Session.cs`, `Screens.cs`, `GameFolders.cs`, `InstallDoctor.cs` | launch, folders, install checks |
| Plugin | `plugin/FmCliProbe/src/{Bridge,Controls,Reads}/` | IL2CPP side: HTTP, controls, texts, pages |
| Tools | `tools/` | install, package, publish, test, dumps |
| Docs | `docs/` | usage, porting notes, layout, test checklist |

## Folders

- `Core/` — bridge, reads, clock, formatting, labels, launch, session, MCP, settings, saves.
- `Match/` — match day, narration, calendar, fixtures, table, opponent report, stages.
- `Squad/` — squad, fitness, morale, contracts, tactics, training, set pieces, registration.
- `Transfer/` — offers, contract talks, scouting, shortlist, targets.
- `Club/` — club, board, finances, staff, facilities, career, jobs, resign.
- `Program/` — large `Program` partials that do not belong to one folder.

## `Program` partials

`Program.cs` holds only the entry point and routing. Large blocks are split by responsibility:

- `Program/Program.Squad.cs` — squad reads and views.
- `Program/Program.Inbox.cs` — inbox and message choice; `Program.Reply.cs` — reply command.
- `Program/Program.Messages.cs` — attachment body and unknown attachments;
  `Messages.Attachments.cs` — shared helpers; `Messages.Cards.cs` — captaincy, match report,
  season review, opponent, training, data hub, scout, injury and player cards;
  `Messages.FrontOffice.cs` — budgets, vision, responsibilities, win bonuses;
  `Messages.SquadCards.cs` — internationals, expiring contracts, recommendations, match prep,
  bid card; `Messages.Inbox.cs` — message reading and number memory.
- `Program/Program.Ui.cs` — button/page commands; `Program.Ui.Helpers.cs` — press/select helpers;
  `Program.Help.cs` + `Core/HelpEnglish.cs` — help text. The help is one entry in
  `labels-turkish.tsv`; update its Turkish value when the English text changes.

## Other splits

- `Match/Guided.cs` — tour and talks; `Guided.SetPieces.cs` — set-piece wizard;
  `Guided.Tactic*.cs` — tactic planner and settings.
- `Core/Reads.cs` — search and player; `Reads.Squad.cs` — squad views; `Reads.League.cs` — league,
  fixtures, progress; `Reads.Player.cs` — player card and history.
- `Match/MatchDay.cs` — main loop; `MatchLive.cs` — score/clock/event panel at a terminal;
  `MatchRadar.cs` — reading and parsing the `/radar` frame; `RadarStyles.cs` — the four radar
  looks (pixel, braille, coloured and plain ASCII) fitted to the room left; `RadarCells.cs` — colour
  (24-bit or 256), the character grid, braille and half blocks; `RadarSample.cs` — a real frame for
  the settings preview;
  `PanelConfig.cs` + `PanelSettings.cs` — the panel file and `fm settings panel`;
  `MatchDay.Instant.cs` — instant result; `MatchDay.Team.cs` — lineups; `MatchDay.Substitute.cs` —
  substitutions; `MatchDay.Dialog.cs` — confirmations.
- `Transfer/ContractTalks.cs` — command and terms; `ContractTalks.Terms.cs` — terminal loop and
  agent answers; `ContractTalks.Screen.cs` — screen reads, exit, renewal.
- `Transfer/ScoutFocus.cs` + `.Team.cs` + `.Form.cs` — focus list, staff, new-focus form.
- `Squad/TrainingSchedule.cs` + `.Catalogue.cs` + `.Calendar.cs` — schedule changes, catalog, grid.
- `Squad/Mentoring.cs` + `Mentoring.New.cs` — groups and new-group flow.
- `Club/StaffSearch.cs` + `.Catalog.cs` + `.Rules.cs` — command, names, rule sending.
- `Club/Responsibilities.cs` + `Responsibilities.Delegate.cs` — duty list and delegation.
- `Core/Saves.cs` + `.SaveAs.cs` + `.Files.cs` — list, save as, delete/load.
- `Core/Clock.cs` + `.Status.cs` + `.Fixtures.cs` — `next`, status, fixtures.
- `Core/Mcp.cs` + `Mcp.Tools.cs` — protocol and tool catalog.
- `Core/Settings.cs` + `.Catalog.cs` + `.Preferences.cs` + `.Draw.cs` — command, catalog, navigation,
  matching and drawing.
- `tools/radar-capture.py` + `radar-styles.py` + `radar-gif.py` — capture `/radar` frames and render
  them in the pixel, braille and isometric styles (see `docs/experiments/radar-styles`).
- `tools/radar-catalog.py` + `tools/catalog/` + `tools/mock_match.py` — the visualization catalog from
  mock data, as PNGs, terminal previews and real ANSI output (see `docs/experiments/catalog-draft`).

Add a command in a new file of the same `Program` partial; do not grow `Program.cs`. Keep reads and
writes of one command together. Reuse `Table`/`Ui` before adding a formatter.

## Plugin

- `Bridge/` — `FmApi` (HTTP surface) + `FmApi.System.cs`, `HttpBridge`, `Plugin`, `Game`, `State`,
  `Send`, `Schema`, interop; `Radar.cs` — the live `MatchSimulationData` read.
- `Controls/` — `Buttons` + `Buttons.Controls.cs` (catalog/click/queue) + `Buttons.Runtime.cs`
  (render, shape, pointer), forms, radios, rows, submenus, dialogs.
- `Reads/` — pages, panels, text/translation, player card, offer, tactics, saves.

A new read: take the data path in `KeyPaths`/`Path`; never key on screen text or row number.
A new write: send the game's event; wait for the queue to drain.

## Nullable

- Flow analysis is on. Dereference, value and null bugs were fixed.
- Five categories remain at the dynamic JSON boundary (`CS8600`, `CS8603`, `CS8604`, `CS8619`,
  `CS8620`); reads go through `JsonRead`/`Field` and runtime checks. They are suppressed at project
  level in `cli/fm/fm.csproj`. Replace with typed DTOs before removing the list.

## Rules

- **Language and screen independent:** data path, event name, GUID, id. No `press "Confirm"`, no
  `up=N`, no row numbers.
- **No fixed waits:** wait for the game's signal; time is only an upper bound.
- **One screen at a time:** a command opens the screen it needs; do not assume another screen's data.
- **No silent failures:** return `rc` and a short reason; an empty table is not "no data".
- **Small changes:** fix one behaviour; do not refactor unrelated code in the same file.

## Build and check

```bash
dotnet build cli/fm/fm.csproj -c Release
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release
tools/install.sh          # plugin + fm + doctor
fm doctor --install
fm status                 # bridge and save
tools/smoke.sh            # build + plugin + tests + published CLI
tools/ci.sh               # without the game (same as CI)
```

Test checklist: `docs/PLAYTEST.md`. Porting notes: `docs/PORTING.md`.

```bash
dotnet test tests/FmCli.Tests/FmCli.Tests.csproj -c Release
```
