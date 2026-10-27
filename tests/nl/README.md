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

`tools/nl-score.py` runs a router over the set and scores it: the right tool, the right tool with
the right arguments, asks and "nothing does this" on their own. Any command that reads
`{"text": ...}` lines and writes call lines can be scored (`--router`); the default is
`fm __route`, the rule router in `cli/fm/Nl/`. Each case falls in "dev" (rules may be tuned on
it) or "held" (never looked at while tuning) by a hash of its sentence, about 70/30; a noisy
variant follows its case. Only the held numbers say how a router does on sentences it has not
seen.

First numbers, rule router (27 Oct): base set dev 89% tool / 85% with arguments, held 33% / 22%;
expanded set dev 81% / 74%, held 29% / 17%; about 2 ms a sentence. Cues written from the dev
sentences barely carry over to new wording: the tool choice needs something that generalises
(embeddings), the argument readers are reusable either way.

Embedding candidate, `tools/nl-embed.py` (ONNX; dev sentences are the index, the nearest
neighbours vote the tool). Off the shelf, held tool: multilingual MiniLM 43% (44% with each tool's
own description indexed too), multilingual-e5-small 36%; football and our tools are not in them
("kadroda kimler var" → search). `tools/nl-train.py` fine-tunes MiniLM on the dev sentences and
their noisy variants (same tool pulled together, the rest of the batch pushed apart), word table
frozen so it trains in ~800 MB on the CPU, about 2.5 minutes an epoch on an M2. One epoch: held
tool 59%, expanded held 43%, about 5 ms a sentence. Training needs torch with numpy<2 (a venv,
see the script); scoring needs only onnxruntime and tokenizers.

More epochs: two give held tool 62%, three the same; training the word table too (`--unfreeze`)
gives 60% at three times the time and twice the memory. Hybrid (`--hybrid`): the fine-tuned
model picks the tool, the rule router's reader for that tool fills the arguments (`fm __route`
fills a "tool" given on the line as "forced"), a sentence the rules split stays split. Two
epochs, held: base 62% tool / 40% with arguments (rules alone 33% / 22%), expanded 47% / 29%
(rules 29% / 17%); a false "nothing does this" 0.2% (rules 10%). Letting the rules win when they
score high only lowers it, at every threshold tried. Weak spots: "nothing does this" sentences
(29% held: the model always finds a nearest tool) and asks (22%).

"Nothing does this" (27 Oct, later): a threshold on the nearest neighbour's similarity (`--none-below`)
does not separate them: at 0.7 held "nothing" goes 24% → 59% but 8% of real asks fall to
"nothing". What works is more of them to learn from: 00-none grew from 50 to about 290
sentences (chat, the real world, other apps, cheats and editor edits, undo, running the other
side, gibberish). Retrained two epochs, held base: tool 60% / with arguments 41%, "nothing" 65%
(was 24%), a false "nothing" 4.1% (was 0.9% with the old model on the new set 2.5%); expanded
held "nothing" 59%, false 9%. `--none-margin M` takes "nothing" only when it outvotes the next
tool by M: 1.5 gives held 47% "nothing" at 1.3% false; every margin trades one for the other,
none raises the total, so the default is 0. A dev sentence no longer finds itself in the index,
so dev numbers are now leave-one-out (base dev tool 91% → 82%).

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
