# fm-cli

Play the real **Football Manager 27** from a terminal.

- A BepInEx plugin inside the game shares its data and events over local HTTP.
- The `fm` CLI turns them into commands. An MCP server gives AI clients the same commands.
- The game stays open in the background. No screen capture, no mouse.

```
$ fm status
FM is open, save loaded. 12 August 2028 09:00
Next match: League · home · Saturday 15:00 (7 days)
Ready to continue: next
```

## What it does

- Squad, players, tactics, training, transfers, contracts, scouting, staff, club, saves.
- Match day with a live panel and radar; press conferences and talks.
- Output in the game's language. `fm lang english|turkish|auto` changes only the CLI.
- Sentences instead of commands (Turkish or English). Anything that changes the game asks first.

## Requirements

- macOS on Apple Silicon, Football Manager 27 from Steam.
- Development: .NET SDK 10. The packaged `fm` needs nothing else.

## Install

Ready package (`tools/package.sh` → `dist/fm-cli-osx-arm64.zip`):

```bash
./install.sh            # BepInEx, plugin and fm into ~/fm27_bep/game; fm into ~/.local/bin
fm doctor --install     # language model (~290 MB) and an 8-step check
```

Development: `tools/install.sh` (builds the plugin, publishes `fm`, runs the check).
Game in another folder: `FM_HOME=/path/to/fm27`.

## First steps

```bash
fm start          # open the game hidden, load the last save
fm status         # date, next match, what is waiting
fm inbox          # messages, numbered; fm reply 3 to open one
fm next           # continue; stops at anything that needs an answer
fm match          # play match day (--instant for a quick result)
fm help           # every command
```

During a match: `s` sub, `h` shout, `t` tactic, `v` view, `a` radar, `r` radar style, `+/-` speed, `:` any command.
Scripts: `--json` on reads, `--dry` on writes, `--yes` where a command would ask.

## Layout

```
plugin/FmCliProbe/   the plugin inside the game (C#, BepInEx 6 IL2CPP)
cli/fm/              the fm command (.NET)
tests/               unit tests, sentence tests, live test lists
tools/               install, package, tests, dumps
docs/reference/      data dumped from the game
```

## Docs

- [MANUAL.md](docs/MANUAL.md): illustrated manual
- [TASKS.md](docs/TASKS.md): open work
- [PORTING.md](docs/PORTING.md): how the bridge works, FM27 notes
- [CODE-LAYOUT.md](docs/CODE-LAYOUT.md): which file holds what
- [AI.md](docs/AI.md): sentences and the assistant

## Checks

```bash
tools/ci.sh                  # build and tests, no game needed
tools/sweep.sh               # every read command against the running game
python3 tools/mcp-check.py   # a real MCP client, read only
```
