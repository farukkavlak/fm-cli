# Natural-language test set

Sentences a player might type, in Turkish and English, each with the MCP call it should become.
Written the way people type: typos, no Turkish letters, slang, half sentences.

## Files

| Path | What |
|---|---|
| `00-none.jsonl` … `13-multi.jsonl` | the test set, by command group (see the map below) |
| `train/*.jsonl` | training-only sentences; never scored, never a copy of a test sentence |
| `expanded/` | noisy variants, generated, not committed |

## Tools

| Command | Does |
|---|---|
| `tools/nl-check.py` | checks every case against the tools in `cli/fm/Core/Mcp.Tools.cs`; reports a training sentence that repeats a test one |
| `tools/nl-expand.py [--train]` | writes noisy variants: Turkish letters folded, a letter dropped, doubled, swapped or mistyped, filler words, odd capitals, another player's name |
| `tools/nl-score.py` | scores a router; `--router CMD` for another one, `--given-tool` for the argument readers alone, `--fails N` to list misses |
| `tools/nl-train.py` | fine-tunes the embedding model, exports int8 ONNX to `~/.cache/fm-cli/nl/<name>` |
| `tools/nl-embed.py` | the same router in Python, for experiments |

Score the current router:

```bash
dotnet build cli/fm
python3 tools/nl-score.py --router "cli/fm/bin/Debug/net10.0/fm __route --model ~/.cache/fm-cli/nl/model"
```

Train a model (Python venv with torch and numpy<2, about 35 minutes on the CPU):

```bash
python3 tools/nl-expand.py --train
~/.cache/fm-cli/nl-venv/bin/python tools/nl-train.py --epochs 2 --name NAME --int8
```

## Dev and held

- Each test sentence is in **dev** (~70%) or **held** (~30%), by a hash of its text.
- A noisy variant goes with its sentence.
- Rules and training sentences are tuned on dev only.
- Only held numbers say how the router does on new wording.

## Metrics

- **tool**: the right tool (top 1).
- **tool+args**: the right tool and the right arguments.
- **ask**: the router asks for what the sentence leaves out instead of guessing.
- **multi**: several asks in one sentence, all right.
- **none**: a sentence no tool answers is answered "nothing does this".
- **false none**: a real ask answered "nothing does this".

## Current numbers

Clean model `mpnet-t-q`, lower-cased input, held:

| set | tool | tool+args | ask | multi | none | false none |
|---|---|---|---|---|---|---|
| base | 75.8% | 62.8% | 47.8% | 25.0% | 82.5% | 2.7% |
| noisy | 66.9% | 52.0% | 39.9% | 20.8% | 71.1% | 3.9% |

Other measurements, held, base set:

- Argument readers alone (the right tool given): 76.7%.
- Right tool among the top 3 candidates: 87.8%. For several asks in one sentence, all tools in
  the top 5: 37.5% (only 8 sentences).
- With choices offered below 80% confidence, the right tool is among them 87% of the time
  (noisy: 78%).

## What we learned

What worked, biggest gain first:

- An embedding model picks the tool, rules fill the arguments. Rules alone got 33% held tool.
- Fine-tuning a multilingual model on our sentences (MiniLM 62%, mpnet-base 65%).
- Training-only sentences: other wordings, chattier sentences, "nothing" sentences.
- Better argument readers. A word is not a name if it is a known word with an ending, a question
  word, a verb form, a number, or a common word when a rarer one is there.
- Int8 weights: same scores, 275 MB instead of 1 GB.
- Lower-casing the input. Odd capitals were the top cause of false "nothing".

What did not work:

- A similarity threshold or margin for "nothing": it trades real asks for it, no net gain.
- Letting the rules win when they score high.
- multilingual-e5-small: no better than MiniLM.
- Voting choice arguments with the nearest sentences.
- Squeezing doubled letters ("kimm" → "kim").
- Offering the rule router's pick when "nothing" wins.

Traps:

- The int8 model scales a batch as a whole, so a sentence gives a slightly different vector alone
  and in a batch. Always embed one sentence at a time.
- ONNX Runtime 1.22.0 crashes on exit on macOS. Use 1.23.2.
- Run `nl-check.py` before training. On 28 Oct, 27 training sentences repeated test sentences and
  inflated held "none" to 92.8%.

The full history of every experiment is in the git log of this file.

## Case format

One JSON object per line. The text is under its language: `"tr"` or `"en"`.

```json
{"tr": "kadroda yorgun kim var", "call": "fm_squad", "args": {"view": "fitness"}}
{"en": "bid 5m for yildiz", "call": "fm_offer", "args": {"name": "yildiz", "fee": "5M"}}
{"tr": "maça çık", "call": "fm_match", "ask": "mode"}
{"tr": "sıradaki maç ne zaman ve kimle", "call": "fm_fixtures", "or": [{"call": "fm_opponent"}]}
{"tr": "kadroyu göster sonra taktiği", "call": ["fm_squad", "fm_tactic"]}
{"tr": "hava nasıl yarın", "call": null}
```

- `call` + `args`: the expected call. `args` holds only what the sentence says.
- `or`: other calls that answer just as well.
- `ask`: the right tool, but something must be asked (a match's mode, a missing player).
- `call` as a list: several asks in order; `args` is then a list too.
- `call: null`: no tool does this.

Judging:

- `yes`, `confirm` and offer `send` are never expected. The CLI asks for those itself.
- Names match without case or Turkish letters, and by prefix (`rinal` matches `Rinaldi`).
  Money matches as an amount (`5m`, `5 milyon`, `5.000.000`).
- Free-text values in the game's words (a role, a setting) match loosely.

## Command map

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

`00-none.jsonl` holds sentences no tool answers; `13-multi.jsonl` holds several asks in one sentence.
