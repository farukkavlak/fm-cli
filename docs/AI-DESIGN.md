# AI design

How sentences work today and how the assistant will be built.

- Why and what for: [AI-ROADMAP.md](AI-ROADMAP.md)
- Tasks in order: [AI-CHECKLIST.md](AI-CHECKLIST.md)
- Test set and numbers: [tests/nl/README.md](../tests/nl/README.md)

## Rules

- A model proposes; only our code checks, asks and runs.
- Facts come from the game. Answers say what was checked.
- Every feature is a command first.
- Local and free by default; a cloud model is the user's choice.
- Decide on held-out numbers, same test set, same machine.
- No vector database, agent framework or big restructure without a measured need.

## Three modes, one core

| Mode | Engine | For |
|---|---|---|
| Command | ONNX embeddings + kNN + rules (today) | fast, offline commands |
| Mahmut Cloud | Claude, OpenAI, … with the user's key | conversation, analysis, plans |
| Mahmut Local | a small local LLM | offline assistant, if good enough |

- Mahmut belongs to the app, not to a model.
- His memory, personality, game access and safety rules live in fm-cli.
- The model only reads and writes language. Switching models keeps Mahmut.
- The MCP server already lets any capable model play through the same tools.

## Today: the command router

```
sentence ─┬─ rules split it into several asks? ── yes: rules decide
          └─ embedding model → 5 nearest examples vote → tool
             15 nearest examples' share of the vote → confidence
                           │
             reader for that tool → arguments, plus what is missing
                           │
   ≥80% run it (ask first if it changes the game) · 50–80% offer 3 · <50% offer 5
```

- Model: `paraphrase-multilingual-mpnet-base-v2`, fine-tuned on our sentences.
- Int8 ONNX, 275 MB, about 5 ms a sentence once loaded.
- Runs in .NET (`cli/fm/Nl/`); the C# tokenizer matches the Python one.
- Downloaded once at install (`fm doctor --install`).

## Target flow

```
input → split into asks → candidate tools per ask → planner → plan
      → names to game ids → check rules → approval → run → verify → answer
```

- **Split first.** For several asks in one sentence, all tools are in one top-5 list only 37.5% of the time.
- **Candidates, not 100 tools.** The router picks the top tools per ask. The planner sees only those.
- **Planner.** Fills arguments, order and missing values. Readers reach only 76.7% held tool+args even when handed the right tool.
- **Names from the save.** Players, clubs and offers come with game ids. Fuzzy match Turkish endings and typos.
- **Two good matches → ask.** Never guess a name.
- **One safety policy.** Each tool is read-only, reversible or high-impact, in one place.
- **Same rules everywhere.** CLI, MCP and Mahmut.
- **Stop on failure.** A multi-step plan reports what was done and what was not.

## Plan format

Each step has:

- tool and arguments,
- the words they came from,
- resolved game ids,
- what it depends on,
- risk and approval state,
- result.

A sale and a search in one sentence are two steps. Only the sale needs approval.

## Memory

- **Turn:** last messages, "him", "o oyuncu", pending approvals.
- **Save:** decisions, plans, watch lists. One save only.
- **User:** preferred style and tone. Editable.
- Each entry is marked: fact, preference, guess, plan or outcome.
- The user can list, fix and delete memory.

## Local LLM experiment

- Question: which jobs can a local model do? Not: local or cloud Mahmut.
- Run with FM open, on an 8 GB Mac.
- Measure separately:
  - understanding (Turkish, slang, typos, half sentences),
  - planning (tools and arguments from candidates),
  - conversation (natural, consistent Turkish),
  - memory, latency, size, license.
- A model may plan well and talk badly. Then it plans and a cloud model talks.

## Evaluation

Build each bench when its feature exists:

- [x] **Tool:** top-1, recall@k, tool+args, ask, multi, none, false none (`tests/nl/README.md`).
- [ ] **Safety:** nothing that changes the game runs without approval.
- [ ] **Entity:** names resolve to the right player or club.
- [ ] **Plan:** several steps, right order and arguments.
- [ ] **Grounding:** answers use only real game data (saved game snapshots).
- [ ] **Dialogue:** natural Turkish, consistent character.

Every result records data hash, split, model, commit and machine.

## Open questions

- Which bridge reads give stable ids? Check again on FM27.
- Can the bridge say when the save changed?
- Which game actions can be undone?
- Mahmut during a live match, or only between matches?
- How much personality by default?
