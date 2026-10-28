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
| `tools/nl-check.py` | checks every case against the tools in `cli/fm/Core/Mcp.Tools.cs`; reports near-copies on two sides |
| `tools/nl-split.py` | gives new cases their side (dev, held, final); run it after adding cases |
| `tools/nl-expand.py [--train]` | writes noisy variants: Turkish letters folded, a letter dropped, doubled, swapped or mistyped, filler words, odd capitals, another player's name |
| `tools/nl-score.py` | scores a router; `--router CMD` for another one, `--given-tool` for the argument readers alone, `--fails N` to list misses, `--final` to score the final set too, `--save` to keep the result in `results/` |
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

## Dev, held, final

- Each test case says its side in `"split"`: **dev** (63%), **held** (23%) or **final** (14%).
- Near-copies form a family, and a family stays on one side. Near = letter-trigram cosine ≥ 0.8,
  or ≥ 0.65 with the same call (`tools/nl_family.py`).
- A case near a training sentence is dev: the model has seen it.
- A new family gets its side by a hash; a case that joins a family takes the family's side.
- A noisy variant goes with its sentence.
- Rules and training sentences are tuned on dev only.
- Held numbers say how the router does on new wording.
- Final is scored only to confirm a decision (`--final`). Never list its failures, never tune on it.
- The split changed on 28 Oct. Rules and readers were tuned on the old dev, which is now partly
  held and final, so their held numbers read high.

## Metrics

Use these names everywhere: code, docs, commits. Out of all sentences unless noted.

- **top-1**: the router's pick is the right tool (or one of its `or` tools).
- **recall@k**: the right tool is among the k best candidate tools; for several asks, all of them
  are. Candidates come from the 50 nearest examples. Out of sentences that expect a tool.
- **tool+args**: the right tool and the right arguments; an expected ask must be asked, not guessed.
- **ask**: tool+args, out of sentences where something must be asked.
- **multi**: tool+args, out of sentences with several asks.
- **none**: answered "nothing does this", out of sentences no tool answers.
- **false none**: answered "nothing does this", out of sentences that expect a tool.

Every result quoted here comes from a file in `results/`, written by `nl-score.py --save`.
The file has a manifest: data hash, split rule, model SHA-256, commit (and whether the tree was
clean), machine.

## Current numbers

Model `mpnet-f-q`, trained on the family-split dev only. From `results/mpnet-f-q.json`:

| set | split | top-1 | recall@3 | recall@5 | tool+args | ask | multi | none | false none |
|---|---|---|---|---|---|---|---|---|---|
| base | held | 73.0% | 86.4% | 88.6% | 67.0% | 56.2% | 52.6% | 89.2% | 2.6% |
| base | final | 68.7% | 82.2% | 88.4% | 61.8% | 30.0% | 46.7% | 90.7% | 4.0% |
| noisy | held | 63.2% | 78.2% | 82.8% | 55.8% | 43.2% | 42.5% | 84.2% | 5.2% |
| noisy | final | 58.9% | 73.9% | 80.7% | 50.7% | 22.5% | 36.7% | 85.1% | 7.0% |

- Base held has 33 ask and 38 multi sentences, final 20 and 30.
- Most ask and multi sentences were added on 28 Oct, after the rules were written, so those two
  columns are not tuned on any side yet (dev ask 63.4%, dev multi 44.8%).
- tool+args reads high for the older sentences: the readers were tuned on the old split (see above).
- Argument readers alone (the right tool given): tool+args held 90.0%, final 86.6%
  (`results/readers.json`). Same caveat.
- The CLI still ships `mpnet-l-q`. It was trained on the old dev, which overlaps today's held
  and final, so it cannot be scored on them.

## What we learned

What worked, biggest gain first:

- An embedding model picks the tool, rules fill the arguments. Rules alone got 33% held tool.
- Fine-tuning a multilingual model on our sentences (MiniLM 62%, mpnet-base 65%).
- Training-only sentences: other wordings, chattier sentences, "nothing" sentences.
- Better argument readers. A word is not a name if it is a known word with an ending, a question
  word, a verb form, a number, or a common word when a rarer one is there.
- Int8 weights: same scores, 275 MB instead of 1 GB.
- Lower-casing the input, in training too. Odd capitals were the top cause of false "nothing".
- Candidates from 50 neighbours instead of the 15 that vote: held recall@5 82.1% → 88.6%.
  Summing, max or best-two per tool: within half a point; 100 neighbours: +1 point recall@5,
  −3 points recall@3.

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
{"tr": "kadroda yorgun kim var", "call": "fm_squad", "args": {"view": "fitness"}, "split": "dev"}
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
- `split`: dev, held or final. Left out in the examples above; `tools/nl-split.py` adds it.

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
