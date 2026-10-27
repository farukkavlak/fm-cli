# Natural-language test set

Sentences a player might type, in Turkish and English, each with the MCP call it should become.
Any router (rule matcher, local embeddings, a small classifier, a cloud model) is measured on
the same set. The sentences are written the way people type: typos, no Turkish letters, slang,
half sentences, several asks at once.

`tools/nl-check.py` checks every case against the tool list in `cli/fm/Core/Mcp.Tools.cs`
(tool names, argument names, enum values, required arguments) and prints counts per tool and
category. `tools/nl-expand.py` writes noisy variants of every case into `tests/nl/expanded/`
(not committed): Turkish letters folded, a letter dropped, doubled, swapped or hit on the key
next to it, filler words, odd capitals, and another name in place of the player's, with the
expected arguments changed to match. Argument values in the sentence are never misspelt.
Six per case by default, about 10 700 in all; same seed, same set.

## Case format

One JSON object per line. The text is under its language: `"tr"` or `"en"`.

```json
{"tr": "kadroda yorgun kim var", "call": "fm_squad", "args": {"view": "fitness"}}
{"en": "bid 5m for yildiz", "call": "fm_offer", "args": {"name": "yildiz", "fee": "5M"}}
{"tr": "rinaldiyi sat 3 milyon", "call": "fm_propose", "args": {"player": "rinaldi", "fee": "3M"}}
{"tr": "maça çık", "call": "fm_match", "ask": "mode"}
{"tr": "sıradaki maç ne zaman ve kimle", "call": "fm_fixtures", "or": [{"call": "fm_opponent"}]}
{"tr": "kadroyu göster sonra taktiği", "call": ["fm_squad", "fm_tactic"]}
{"tr": "hava nasıl yarın", "call": null}
```

- `call` + `args`: the expected call. `args` holds what the sentence says, nothing more.
- `or`: other calls that answer the sentence just as well; any of them counts as right.
- `ask`: the tool is right but a choice the sentence does not make must be asked
  (`fm_match` mode, `fm_stop` save, a missing player). The router should name the tool and ask,
  not guess.
- `call` as a list: several asks in one sentence, in order; `args` is then a list too.
- `call: null`: nothing in the tool set does this; the right answer is to say so.

Rules for judging:

- `yes`, `confirm` and offer `send` are never expected. Anything that changes the game is shown
  first and confirmed by the user; the layer adds those itself.
- Names are compared without case or Turkish letters and by prefix (`ghiglione` matches
  `Ghiglione`; `rinal` is fine when the sentence says so). Money is compared as an amount
  (`5m`, `5M`, `5 milyon`, `5.000.000` are the same).
- Free-text arguments in the game's own words (a role, a focus, a setting name) are compared
  loosely; the tool lists the choices when the value is off.

## Command map

Twelve groups. A sentence is first placed in a group, then on a tool.

| # | Group | Tools |
|---|---|---|
| 1 | Time and saves | status, next, diff, world, save, saves, load, continue, stop |
| 2 | Inbox and talks | inbox, reply, talk, tour, dialog, news |
| 3 | Match day | match, sub, shout, dugout, report, opponent, opposition, prep |
| 4 | Fixtures and competitions | fixtures, calendar, table, league, stages |
| 5 | Squad and players | squad, player, compare, query, depth, health, doctor, progress, happiness, dynamics, internationals, contracts, captains |
| 6 | Tactics | tactic, tactic_set, tactic_role, tactic_player, tactic_pick, tactic_instructions, tactic_pi, tactic_slot, tactic_copy, tactic_clear, tactic_file, tactic_new |
| 7 | Set pieces | takers, penalties, setpieces, setpieces_new |
| 8 | Training | training, intensity, coaches, schedule, mentoring, youth |
| 9 | Buying and scouting | search, players, targets, shortlist, shortlist_change, offer, approach, scout, scout_focus, scout_player, actions |
| 10 | Selling and contracts | offers, propose, release, contract, register, numbers |
| 11 | Club and board | club, board, board_request, vision, budget, finances, facilities, supporters, staff, staff_find, staff_hire, staff_fire, responsibilities, jobs, resign |
| 12 | Screens and settings | settings, pages, open, buttons, press, dropdowns, select |

The files are named after the groups (`01-time.jsonl` … `12-screens.jsonl`); `00-none.jsonl`
holds sentences no tool answers, `13-multi.jsonl` several asks in one sentence.
