# fm-cli manual

The main workflows, with screenshots. Every command: `fm help`.

- The game stays open in the background; `fm` reads its data and sends its events.
- Screenshots are mostly from a Turkish game: labels follow the game's language.

## 1. Start and status

```bash
fm start          # launch the game, load the last save, enter a session
fm status         # date, next match, what is waiting
fm next           # continue; stops at a real blocker
fm next --until match    # on to match day (or the first thing that wants an answer)
fm holiday week|month|match   # the assistant runs the club until then
fm continue       # load the last save in the open game
fm stop [--save|--no-save]    # close the game; asks about saving first
```

![fm status](manual/img/start/status.png)

- `status` names injured players when the match is today or tomorrow: the picked ones when the tactics screen is up, otherwise the squad's.
- `next` stops for an unanswered message, a press conference, match day, or a question after a match.
- With the game's "Read Messages" setting, `next` first opens the unread messages, oldest first, as the game does.
- `inbox --read-all` marks every message read at once.

### Asking in a sentence

- Turkish or English: `önümüzdeki maç ne zaman`, `rinaldiyi sat`, `who is injured`.
- The command it became is shown, then runs.
- Not sure: it offers 3–5 commands; a number picks one, Enter none.
- Missing details are asked (`maça çık` asks how).
- Anything that spends, signs, sells or cannot be undone runs only on a yes.
- One word, a command's name first, or `--options`: it stays a command.
- The model (~290 MB) comes with `fm doctor --install`. The first sentence takes ~30 s.

![a sentence the model is not sure of](manual/img/ask/options.png)

### Language

The CLI follows the game's language. To use another language for the CLI alone:

```bash
fm lang english    # CLI text in English; the game's data keeps its own language
fm lang turkish
fm lang auto       # follow the game
FM_LANG=english fm status
```

Kept in `~/.config/fm-cli/language-choice`.

## 2. Inbox, replies and talks

```bash
fm inbox              # the newest 30 messages, and any older one awaiting an answer
fm inbox --all        # every message
fm inbox --tasks      # offers and registration messages on the Tasks tab
fm reply 4            # read message 4 and list its answers
fm reply 4 2          # answer it with choice 2
fm talk               # the open conversation: what was said and what can be said
```

![fm inbox](manual/img/inbox/inbox.png)

![fm inbox --tasks](manual/img/inbox/inbox-tasks.png)

- A message that needs an answer stops `next`.
- A message with no answer is read and closed.

![fm reply](manual/img/inbox/reply.png)

![fm reply, answered](manual/img/inbox/reply-done.png)

## 3. Squad

```bash
fm squad                 # position, age, status, wage, value
fm squad --fitness       # condition and match sharpness, most tired first
fm squad --morale        # morale, hierarchy and social groups
fm squad --stats         # apps, minutes, goals, assists, average rating, cards
fm squad --u19           # the youth sides
fm squad --views         # the table's own views
```

![fm squad](manual/img/squad/squad.png)

![fm squad --fitness](manual/img/squad/squad-fitness.png)

![fm squad --morale](manual/img/squad/squad-morale.png)

![fm squad --stats](manual/img/squad/squad-stats.png)

### Contracts, health and depth

```bash
fm contracts             # whose contract ends when, soonest first
fm contracts "A Player"
fm contracts --expiring  # the "contracts set to expire" message: each player's recommended action
fm contracts --expiring "A Player" 2   # that row's decision by its number (2: trigger the extension)
fm health                # injured, tired, low sharpness, highest injury risk
fm doctor                # what is wrong, with the command for each finding
fm depth                 # who can play each position, age profile
fm happiness             # morale and the reasons
fm progress              # who is improving or declining
```

![fm contracts](manual/img/squad/contracts.png)

![fm health](manual/img/squad/health.png)

![fm doctor](manual/img/squad/doctor.png)

![fm progress](manual/img/squad/progress.png)

## 4. Players

```bash
fm player "A Player"          # page: identity, contract, traits, attributes, positions
fm player "A Player" --report # scout report
fm player "A Player" --career # season by season
fm player "A Player" --matches
fm search "text"              # the game's search
fm compare "A Player" "B Player"
fm query "age<23 and potential>140" [--sort field] [--asc]
```

![fm player](manual/img/players/player.png)

![fm search](manual/img/players/search.png)

![fm player --career](manual/img/players/career.png)

## 5. Tactics and training

```bash
fm tactic                    # shape, style, mentality, roles, effectiveness
fm tactic pick               # let the game pick the eleven
fm tactic pick --youth       # youth players too, when the first team is short
fm tactic advice             # the assistant's evaluation of the tactic; also: xi, opposition
fm tactic role <position> [<role>] [--oop]
fm tactic player <position> <player>    # anyone; a starter named swaps places with the one there
fm tactic player <substitute> <player>  # swap a substitute (an injured one) for another
fm tactic player <position> --clear     # leave the position empty
fm tactic instructions ["<instruction>" [<value>]]
fm tactic pi <position|player> ["<instruction>" [<value>]]
fm opposition [<player> <instruction>=<value>]
```

![fm tactic](manual/img/tactics/tactic.png)

```bash
fm training [<player>] [--focus ...] [--position ...] [--role ...] [--control player|month|all]
fm training <player> --report [--coach <name>]
fm training <player> --trait ["<trait>"]
fm schedule                  # this week and next, three sessions a day
fm schedule --team "<team>"  # the same for the B team or the U19s
fm schedule copy | paste | clear [this|<weeks ahead>]   # the week card's own buttons
fm intensity                 # per-player intensity
fm coaches                   # coaching assignments
fm prep                      # match preparation sessions up to the next match
fm setpieces new             # set-piece routines
fm takers                    # set-piece takers
fm penalties                 # penalty order and rules
fm scout                     # scouting focuses; scout <#|name>: the players one found
fm scout teams               # team reports being prepared; top / cancel <#|team>
fm youth [candidates]        # academy and prospects; the year's intake
```

![fm schedule](manual/img/tactics/schedule.png)

![fm scout](manual/img/tactics/scout-focus.png)

![fm youth](manual/img/tactics/youth.png)

![fm youth candidates](manual/img/tactics/youth-candidates.png)

## 6. Match day

```bash
fm match             # asks: watch in the terminal or instant result
fm match --play      # watch: commentary, goals, talks
fm match --instant   # leave it to the assistants
fm match --instant --pick   # the U19s play the same day: take their staff's suggested team
fm sub               # during a match: on the pitch and the bench
fm sub <off> <on>
fm shout [<x>]
fm tweak [<x>] [--off]   # tactical tweaks: Take a Breather, Lock It Down…
fm tweak [<x>] --player <player>   # one player's tweaks
fm tweak --reset team|individual|all
fm pause             # during a match; fm resume goes on
fm skip              # skip the replay that is playing
fm dugout [apply | <no> <apply|dismiss>] [--yes]
fm report            # the last match: score, scorers, player of the match, stats
fm opponent          # the next opponent: form, formation, key players, news
```

- `tweak` lists the team's tweaks; `tweak <x>` puts one on, `--off` takes it off.
- The game applies it within seconds, or at the next break.
- Tweaks work after kick-off. `--player` and `--reset` need the match screen.
- `--player`: what the game offers that player; greyed ones cannot be put on.
- `--reset` also takes back changes still waiting.
- A goal with a change waiting: the game asks; `match` stops and says `dialog`.
- After `pause`, `match` carries on as `resume` does.
- `dugout apply` during play holds the match, takes the card, reads back what changed, then plays on.
- A card that would take a tweak off: the game asks first. Without `--yes` nothing is applied and the question is shown.

![Tactical tweaks of one player](manual/img/match/tweak.png)

`match --play` at a terminal draws a live panel: score, clock, radar, last events.
Scripts and MCP get plain lines.

- Keys: `s` sub, `h` shout, `t` tactic, `v` view, `a` radar on/off, `r` radar style, `+`/`-` speed, `:` any command.
- At an injury stop it names the player and asks who comes on.
- The radar draws the ball and every player from the game's own simulation.
- Seven styles: `pixel`, `iso`, `raycast`, `voxel` (24-bit colour), `braille`, `colour`, `ascii` (any terminal).
- `auto` (default): `pixel` with 24-bit colour, else `braille`.
- Too small a window: it says how much room it needs.
- No live possession or shots in a match; `report` has them after.

![fm match --play](manual/img/match/live-match.png)

The seven radar styles (`fm settings panel radar <style>` previews one):

| | |
|---|---|
| `pixel` ![pixel](manual/img/match/radar-pixel.png) | `iso` ![iso](manual/img/match/radar-iso.png) |
| `raycast` ![raycast](manual/img/match/radar-raycast.png) | `voxel` ![voxel](manual/img/match/radar-voxel.png) |
| `braille` ![braille](manual/img/match/radar-braille.png) | `colour` ![colour](manual/img/match/radar-colour.png) |
| `ascii` ![ascii](manual/img/match/radar-ascii.png) | |

Panel settings (`~/.config/fm-cli/panel.json`):

```bash
fm settings panel                              # show them
fm settings panel view key                     # full, key or score
fm settings panel pitch off events 5 colour off radar on
fm settings panel radar braille                # pixel, iso, raycast, voxel, braille, colour, ascii or auto; shows a preview
fm settings panel reset
```

![fm report](manual/img/match/report.png)

![fm opponent](manual/img/match/opponent.png)

## 7. League, fixtures and cups

```bash
fm table [competition]
fm fixtures [--all]
fm calendar [month] [--u19]
fm league [competition] [--stat <name>] [--teams] [--top <n>]
fm stages [competition] [stage] [--all] [--list]
```

![fm table](manual/img/league/table.png)

![fm fixtures](manual/img/league/fixtures.png)

![fm fixtures --all](manual/img/league/fixtures-all.png)

![fm calendar](manual/img/league/calendar.png)

![fm stages](manual/img/league/stages.png)

## 8. Transfers and contracts

```bash
fm targets [<position>] [--max-value <money>] [--age <range>]
fm players [--filter ...] [--view ...]
fm offer "<player>" [fee] [--term "Label=value"] [--loan] [--dry]
fm offer "<player>" --send --yes
fm offers                    # offers for our players
fm offer "<player> <club>" <fee> --yes [--negotiate]   # their offer back with our terms
fm propose "<player>" [fee] [--loan] [--yes]
fm release "<player>" [--free|--mutual] [--yes]
fm contract ["<player>"]     # talks: promises, then terms
fm contract "<player>" --next
fm contract "<player>" --sign --yes
fm contract "<player>" --agent
fm budget                    # transfer, wage and scouting budgets
```

![fm offer](manual/img/transfers/offer.png)

![fm offer, edited](manual/img/transfers/offer-edited.png)

![fm offer, locked terms](manual/img/transfers/offer-locked.png)

![fm contract, promises](manual/img/transfers/contract-promises.png)

![fm contract, terms](manual/img/transfers/contract-terms.png)

![fm contract, agent's answer](manual/img/transfers/contract-answer.png)

![fm budget](manual/img/transfers/budget.png)

### Registration and numbers

```bash
fm register [--in <player>] [--out <player>] [--auto] [--send]
fm numbers [<player> <no>] [--auto] [--send]
```

![fm register](manual/img/squad/register.png)

![fm register, picking](manual/img/squad/register-pick.png)

![fm register, rules met](manual/img/squad/register-ready.png)

![fm register, sent](manual/img/squad/register-done.png)

## 9. Club

```bash
fm club           # board, vision, supporters, money, facilities, staff gaps
fm board          # the board's and supporters' view
fm finances [income|expenditure] [period]
fm supporters
fm staff
fm facilities
fm responsibilities [<duty>]
fm advice         # the staff's advice by area; advice <no>|<name> apply | seen; advice all seen
fm world          # loaded save, date, nations, leagues, players
```

![fm club](manual/img/club/club.png)

![fm board](manual/img/club/board.png)

![fm finances](manual/img/club/finances.png)

![fm finances expenditure](manual/img/club/finances-expenditure.png)

![fm supporters](manual/img/club/supporters.png)

![fm staff](manual/img/club/staff.png)

![fm facilities](manual/img/club/facilities.png)

![fm responsibilities](manual/img/club/responsibilities.png)

- `advice` lists what the staff advise: who it is about, what they say, the action they offer.
- `apply` carries one out (tactics advice: for the next match); `seen` marks it seen. Both are read back.
- Advice that opens a card of its own in the game is listed, not carried out.

![fm advice](manual/img/club/advice.png)

![fm world](manual/img/start/world.png)

## 10. Saves and settings

```bash
fm saves
fm save --yes
fm save --as "<name>" --yes
fm load "<name>" [--save|--no-save]
fm settings                    # the game's main preferences
fm settings all                # every preference, each with its key
fm settings "<setting>" [<value>]   # a name, the game's title or a key
fm settings "text scaling" Large    # interface sizes: text scaling, information display
```

`fm settings` changes the game. The CLI's own language: `fm lang`.

## 11. MCP and scripts

```bash
fm mcp                     # the commands as MCP tools over stdio
python3 tools/mcp-check.py # a real MCP client check (needs: pip install mcp)
tools/sweep.sh             # run every read-only command against the game
```

- Reads have `--json` (often `--csv` / `--md`).
- Writes that cannot be undone ask at a terminal; scripts pass `--yes`.

## 12. More commands

```bash
fm news [#]                 # the press: headlines, or one story in full
fm shortlist [add|remove <player> --for <length>]
fm transferroom [--loan]    # clubs' adverts for players you could offer
fm transferroom offer <no or club> [player]   # send one; activity lists those sent
fm transferroom wanted | responses            # your own requirements and the replies
fm transferroom new --position "ST (C)" [--role <r>] [--max-age <n>] [--time <t>] [--type <t>]   # --choices: what each list offers, by value
fm transferroom delete <no>
fm transferroom agents | discard <player> | discard --all    # players agents offered
fm planner [--season current|next|after] [--position <p>]    # the Squad Planner
fm planner add|remove <player> · move <player> <place>
fm planner suggest position|tactic|season|all
fm interest "<player>" --top|--transfer|--loan [--yes]
fm enquire "<player>"       # ask the club what it wants
fm trial "<player>" --for <1-4 weeks> [--yes]
fm diff [--keep|--reset]    # what changed since the last look
fm export <file.md|csv> [commands…]
fm internationals           # our players' national teams
fm mentoring [--auto|new|remove]
fm dynamics                 # dressing room: atmosphere, support, hierarchy
fm captains [--captain|--vice <player>]
fm actions "<person|club>" ["<option>"]   # the game's right-click menu
fm vision [confirm|remove|suggest|undo|reset|end|plan|add]   # plan: the 5 Year Plan
fm jobs [<club>] [apply|interest] [--yes] [--all]
fm resign [--yes]
fm read [<page>]            # the game's look-only pages, read as they stand
fm read [<page>] --of "<name>"   # a club not ours, a nation, a national team, a competition
fm meeting                  # a team meeting of your own; answer with talk
fm draw                     # a cup draw its message opened: runs it, shows the ties
fm friendly [<#|club> [--date yyyy-mm-dd] [--away] [--rules <no>] --yes]   # opponents, dates, rules
fm manager                  # your record: clubs, transfers, results
```

![fm read](manual/img/club/read.png)

- `fm read` lists the look-only pages; `fm read club-honours` reads one.
- `fm read --of "Galatasaray (Super League)"` also lists the pages on that club's bar.
- Two of one name are told apart in brackets (kind, league).

![fm read --of](manual/img/club/read-of.png)

## 13. Everything else

```bash
fm pages                    # the game's pages
fm open "<page>" [--tab <tab>]
fm buttons [text] [--all]
fm press "<label>" [--name <name>] [--dry]
fm dropdowns [text]
fm select <list> "<option>" [--dry]
fm type "<text>" [--dry]
fm dialog [answer]
fm tour [--skip]
fm completion zsh|bash      # tab completion
fm crawl <out.json> [--here] [--deep]   # for developers: a map of the game's pages
```

Not here: `fm help <command>`. A screen with no command: `pages`, `buttons`, `press`.
