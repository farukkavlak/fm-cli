# Code layout

## CLI (`cli/fm/`)

- `Program.cs`: entry point and routing only. Every command is a file of the `Program` partial.
- `Core/`: bridge (`Bridge.cs`), clock (`next`, `status`), labels and game words, launch, saves, settings, MCP.
- `Match/`: match day, live panel, radar, report, fixtures, table, calendar, tactics planner, talks.
- `Squad/`: squad, health, training, tactics files, set pieces, registration, captains, mentoring.
- `Transfer/`: offers, contract talks, scouting, shortlist, targets, TransferRoom, planner.
- `Club/`: club, board, finances, staff and their advice, vision, responsibilities, youth, jobs.
- `Nl/`: sentences instead of commands (router, embedder, rules).
- `Program/`: inbox, reply, messages, help, buttons and pages.

Help text: `Core/HelpEnglish.cs`. It is one entry in `labels-turkish.tsv`: change its Turkish value with it.
Every user-facing word: `L($"…")` plus its line in `labels-turkish.tsv`.

## Plugin (`plugin/FmCliProbe/src/`)

- `Bridge/`: HTTP surface (`FmApi`), frame scheduling (`HttpBridge`), game state, radar, interop fixes.
- `Controls/`: buttons, dropdowns, menus, dialogs, table rows, text boxes, pitch tiles.
- `Reads/`: texts, pages, panels, player card, talks, tweaks, attachments, crash-frame names (`WhatIs`).

## Tools (`tools/`)

- Install and publish: `install.sh`, `package.sh`, `publish.sh`, `ci.sh`.
- Tests: `fm-round.py` (live lists in `tests/live/`), `sweep.sh`, `mcp-check.py`, `smoke.sh`.
- Game data: `sigdump/` (types), `metadump`, `identity-check.py`.
- Sentences: `nl-*.py`, `nl-release.sh`. Radar and panel looks: `radar-*.py`, `live-*.py`, `catalog/`.

## Rules for new code

- A new command goes in its own file, not in `Program.cs`.
- Keep one command's reads and writes together.
- Reuse `Table` / `Ui` before writing a formatter.
