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
| `tools/nl-cloud.py` | smart mode: the cloud model asked where the router is not sure (`fm __route --cloud`); what runs, what is offered first, how long (T207) |
| `tools/nl-learn.py` | teaches the held misses as if the user had picked them, then asks them and their noisy forms again (T206) |
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

- Each case has a side in `"split"`: dev 63%, held 23%, final 14%. Near-copies stay on one side.
- Tune on dev only. Held says how new wording does. Final only confirms a decision; never tune on it.

## Metrics

Use these names everywhere: code, docs, commits. Out of all sentences unless noted.

- **top-1**: the router's pick is the right tool (or one of its `or` tools).
- **recall@k**: the right tool is among the k best candidate tools. Candidates come from the 50
  nearest examples. Out of sentences that expect a tool. Several asks: when the rules split the
  sentence, each ask has its own list and each expected tool must be in its ask's k best, in
  order; when they do not, all expected tools must be in the whole sentence's k best.
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
| base | held | 73.0% | 87.3% | 89.1% | 67.0% | 56.2% | 52.6% | 89.2% | 2.6% |
| base | final | 68.7% | 84.8% | 89.5% | 61.8% | 30.0% | 46.7% | 90.7% | 4.0% |
| noisy | held | 63.2% | 78.9% | 82.9% | 55.8% | 43.2% | 42.5% | 84.2% | 5.2% |
| noisy | final | 58.9% | 76.4% | 81.8% | 50.7% | 22.5% | 36.7% | 85.1% | 7.0% |

- `13-multi` recall@5: held 97.4%, final 93.3%. Readers alone (right tool given): tool+args held 90.0%.
- The CLI ships `mpnet-l-q` (trained on an older split; not comparable).

## What we learned

- Worked: embeddings pick the tool, rules fill arguments; fine-tuning on our sentences; int8 (275 MB, same scores);
  lower-casing; 50 neighbours for candidates; one candidate list per ask.
- Did not work: a threshold for "nothing", rules overriding, e5-small, squeezing doubled letters.
- Traps: embed one sentence at a time (int8 batches differ); ONNX Runtime 1.23.2 (1.22.0 crashes on exit);
  run `nl-check.py` before training (copies of test sentences inflate scores).

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
