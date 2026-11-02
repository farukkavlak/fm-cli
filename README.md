# fm-cli

Play the real **Football Manager 26** from a terminal.

- A BepInEx plugin inside the game exposes its data and events over local HTTP.
- The `fm` CLI turns them into commands.
- The game stays open in the background. No screen capture, no mouse, no vision.

```
$ fm status
FM is open, save loaded. 12 August 2028 09:00
Next match: League · home · Saturday 15:00 (7 days)
Transfers: window closed
Ready to continue: next

$ fm squad
PLAYER          POSITION          AGE  STATUS        WAGE        VALUE
A Player        AM (RL), ST (C)   24   Squad Player  £14.25K p/w £2M - £4.9M
B Player        GK                31   Star Player   £230K p/w   £56M - £62M

$ fm inbox
1  08:05  A Sender   A subject line                         unread
2  08:13  A Sender   A question that blocks Continue        unread, answerable
```

## What it can do

- Squad and player pages, tactics, team instructions, training.
- Transfers, contracts, scouting.
- Match day with a live panel and radar, press conferences.
- Staff, finances, club vision, settings, saves.
- An MCP server, so an AI client can use the same commands.
- Output in the game's language; `fm lang english` switches the CLI only.
- Sentences in place of commands, in Turkish or English:
  - a local model picks the command,
  - it offers choices when it is not sure,
  - anything that changes the game asks first.

```
$ fm önümüzdeki maç ne zaman
→ fixtures
$ fm rinaldiyi sat
`propose rinaldi` çalıştırılsın mı? [e/H]
```

Every command and option: `fm help` or [docs/USAGE.md](docs/USAGE.md).

## Requirements

- macOS on **Apple Silicon**.
- **Football Manager 26** from Steam.
- Ready package: nothing else (it bundles BepInEx 6 IL2CPP arm64, the plugin and `fm`).
- Development: .NET SDK 10. The published `dist/fm/fm` needs no SDK.
- Not supported: Intel, Epic, Game Pass, Windows.

## Install — ready package

Build it with `tools/package.sh`, or use `dist/fm-cli-osx-arm64.zip`. Unpack and run:

```bash
./install.sh
fm doctor --install
```

- The installer copies BepInEx, the plugin and `fm`.
- It creates the arm64 shadow app (`~/fm26_bep/fm.app`) and links `fm` into `~/.local/bin`.
- `fm doctor --install` downloads the language model once (~290 MB).
- It then checks eight things: game, BepInEx core, launcher, shadow app, plugin, `fm`, save
  folder, language model. Each missing step says what to do. `--json` for scripts.

Game in another folder:

```bash
FM_HOME=/path/to/fm26 ./install.sh
FM_HOME=/path/to/fm26 fm start
```

## Install — development

With BepInEx and the shadow app already on the machine:

```bash
tools/install.sh
```

- Builds the plugin and publishes `fm` as one file.
- Links it into `~/.local/bin` and runs the install check.

By hand:

```bash
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll \
   "$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26/BepInEx/plugins/FmCliProbe/"

dotnet build cli/fm -c Release
ln -sf "$PWD/cli/fm/bin/Release/net10.0/fm" ~/.local/bin/fm
# or: tools/publish.sh → dist/fm/fm (osx-arm64, self-contained)
```

Start the game with `fm start` (or the shadow app), then:

```bash
fm status        # is the game up, is a save loaded, what is next
fm inbox         # what is waiting
fm next          # continue; stops when something needs an answer
fm match         # play the match day
fm lang english  # CLI in English, the game stays as it is
```

## Layout

```
plugin/FmCliProbe/     the plugin inside the game (C#, BepInEx 6 IL2CPP)
cli/fm/                the fm command (.NET; Core/Match/Squad/Transfer/Club/Nl)
tests/FmCli.Tests/     unit tests, no game needed
tests/nl/              sentences and expected commands, to score the sentence router
docs/reference/        data model, events, actions and enums dumped from the game
tools/                 install, package, publish, tests, dumps, router training
```

## Docs

| File | What |
|---|---|
| [docs/MANUAL.md](docs/MANUAL.md) | illustrated manual |
| [docs/USAGE.md](docs/USAGE.md) | every command and option |
| [docs/PORTING.md](docs/PORTING.md) | architecture, rules, FM27 checklist, traps |
| [docs/TASKS.md](docs/TASKS.md) | open work, one checklist by id |
| [docs/BACKLOG.md](docs/BACKLOG.md) | ideas |
| [docs/CODE-LAYOUT.md](docs/CODE-LAYOUT.md) | which file holds what |
| [docs/PLAYTEST.md](docs/PLAYTEST.md) | test checklist |
| [docs/AI-ROADMAP.md](docs/AI-ROADMAP.md) | the AI goal: sentences now, Mahmut later |
| [docs/AI-DESIGN.md](docs/AI-DESIGN.md) | how the AI parts work |
| [docs/AI-CHECKLIST.md](docs/AI-CHECKLIST.md) | AI work, in order |
| [tests/nl/README.md](tests/nl/README.md) | sentence test set and numbers |
| [docs/reference/](docs/reference/) | generated game data |

## Checks

```bash
tools/ci.sh                  # CLI, tests, publish; no game needed
tools/smoke.sh               # build, plugin, tests, published CLI, live status
tools/sweep.sh               # every read-only command against the running game
python3 tools/mcp-check.py   # real MCP client, read-only (needs: pip install mcp)
```
