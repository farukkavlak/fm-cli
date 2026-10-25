# fm-cli

Play the real **Football Manager 26** from a terminal. A BepInEx plugin exposes the game's data
tree and events; the CLI turns them into commands. The game stays open in the background — no
screen capture, no mouse, no vision.

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

Squad and player pages, tactics and team instructions, training, transfers and contracts,
scouting, match day, press conferences, staff, finances, club vision, settings, saves, and a Model
Context Protocol server so an AI client can drive the same commands.

The CLI follows the game's language; `fm lang english` switches it without changing the game's data.

Every command and option: `fm help`, or [docs/USAGE.md](docs/USAGE.md).

## Requirements

- macOS on **Apple Silicon**
- **Football Manager 26** from Steam
- For the ready package: nothing else — it bundles BepInEx 6 IL2CPP arm64, the plugin and `fm`
- For development: .NET SDK 10 (the published `dist/fm/fm` needs no SDK)

Intel, Epic, Game Pass and Windows are not supported.

## Install — ready package

Build it with `tools/package.sh`, or use `dist/fm-cli-osx-arm64.zip`. Unpack and run:

```bash
./install.sh
fm doctor --install
```

The installer copies BepInEx, the plugin and `fm`, creates the arm64 shadow app
(`~/fm26_bep/fm.app`) and links `fm` into `~/.local/bin`. If the game is elsewhere:

```bash
FM_HOME=/path/to/fm26 ./install.sh
FM_HOME=/path/to/fm26 fm start
```

`fm doctor --install` checks seven things: game, BepInEx core, launcher, shadow app, plugin, `fm`
command and save folder. Each missing step says what to do. `--json` prints the same for scripts.

## Install — development

With BepInEx and the shadow app already on the machine:

```bash
tools/install.sh
```

It builds the plugin, publishes `fm` as one self-contained file, links it into `~/.local/bin` and
runs the install check. By hand:

```bash
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll \
   "$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26/BepInEx/plugins/FmCliProbe/"

dotnet build cli/fm -c Release
ln -sf "$PWD/cli/fm/bin/Release/net10.0/fm" ~/.local/bin/fm
# or: tools/publish.sh → dist/fm/fm (osx-arm64, self-contained)
```

Start the game through the shadow app, or `fm start`, then:

```bash
fm status      # is the game up, is a save loaded, what is next
fm inbox       # what is waiting
fm next        # continue; stops when something needs an answer
fm match       # play the match day
fm lang english # CLI in English while the game stays Turkish (data keeps the game's words)
```

## Layout

```
plugin/FmCliProbe/     the plugin inside the game (C#, BepInEx 6 IL2CPP)
cli/fm/                the fm command (.NET; Core/Match/Squad/Transfer/Club)
tests/FmCli.Tests/     unit tests for pure helpers (no game needed)
docs/reference/        data model, event, action and enum dumps from the game
tools/                 install, package, publish, tests, dumps
```

## Docs

| File | What |
|---|---|
| [docs/USAGE.md](docs/USAGE.md) | every command and option, with examples |
| [docs/PORTING.md](docs/PORTING.md) | architecture, rules, FM27 checklist, known traps |
| [docs/CODE-LAYOUT.md](docs/CODE-LAYOUT.md) | which file holds what |
| [docs/PLAYTEST.md](docs/PLAYTEST.md) | short test checklist |
| [docs/reference/](docs/reference/) | generated game data: model, events, actions, enums, translations |

## Checks

```bash
tools/ci.sh        # CLI, tests, publish; no game needed
tools/smoke.sh     # build, plugin, tests, published CLI, live status
tools/sweep.sh     # run every read-only command against the running game
```
