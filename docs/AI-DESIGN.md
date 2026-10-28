# AI design

How fm-cli understands sentences today, and where it is going. The task list is
[AI-CHECKLIST.md](AI-CHECKLIST.md). Test set and numbers: [tests/nl/README.md](../tests/nl/README.md).

## Goal

Play Football Manager by talking to it. First as commands in plain words ("maça kadar devam et"),
later as a conversation with an assistant manager, **Mahmut**, who reads the real save, remembers
decisions, explains what he saw, and acts only when allowed.

## Rules

- **A model proposes, the code decides.** Only application code checks, asks for approval and
  runs a command. No model output is run directly.
- **Facts come from the game.** Mahmut never invents a player, an injury, an offer or a result.
  He says what he checked.
- **Everything works without AI.** Every feature is a command first.
- **Local and free by default.** A cloud model is the user's choice, never required.
- **Measure before deciding.** Same test set, same machine, held numbers only.
- **Keep it simple.** No vector database, no agent framework, no big restructure until a
  measured need shows up.

## Three modes, one core

| Mode | Engine | For |
|---|---|---|
| Command | ONNX embeddings + kNN + rules (today) | fast, offline, free commands |
| Mahmut Cloud | Claude, OpenAI, … (user's own key) | conversation, analysis, planning |
| Mahmut Local | small local LLM | offline assistant, if it proves good enough |

Mahmut belongs to the app, not to a model: his memory, personality, game access and safety rules
live in fm-cli. The model only reads and writes language, so switching models keeps Mahmut.

The MCP server already lets any capable model drive the game through the same tools.

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

- Model: `paraphrase-multilingual-mpnet-base-v2`, fine-tuned on our sentences, int8 ONNX,
  275 MB, about 5 ms a sentence once loaded.
- Runs in .NET (`cli/fm/Nl/`), with a C# tokenizer that matches the Python one.
- Downloaded once at install (`fm doctor --install`).

## Target

```
input → split into asks → candidate tools per ask → planner → plan
      → resolve names to game ids → check rules → ask approval → run → verify → answer
```

- **Split first.** One candidate list for a whole sentence misses tools: for several asks in one
  sentence, all tools are in the top 5 only 37.5% of the time today.
- **Candidates, not 100 tools.** The current router picks the top tools for each ask; a planner
  model sees only those.
- **Planner.** Fills arguments, order and missing values as a typed plan. The argument readers
  reach only 76.7% held even when the tool is given, so this is where a model can help most.
- **Names from the save.** Players, clubs and offers come from the game with their ids. Fuzzy
  matching handles Turkish endings and typos; two good matches mean a question, not a guess.
- **One safety policy.** Every tool is marked read-only, reversible or high-impact, in one place.
  CLI, MCP and Mahmut all follow it. A multi-step plan stops and reports if a step fails.

## Plan format

Each step: tool, arguments, the words they came from, resolved ids, depends-on, risk, approval
state, result. A sale and a search in the same sentence are separate steps; only the sale needs
approval.

## Memory (Mahmut)

- **Turn:** the last few messages, pronouns ("him", "o oyuncu"), pending approvals.
- **Save:** decisions, plans, watch lists; tied to one save, never shared across saves.
- **User:** preferred style and tone; editable.

Each entry says whether it is a fact (read from the game), a preference, a guess, a plan or an
outcome. The user can list, fix and delete memory.

## Local LLM experiment

The question is not "local or cloud Mahmut" but "which jobs can a local model do". Measured
separately, with FM running, on an 8 GB Mac:

- **Understanding**: Turkish, slang, typos, half sentences.
- **Planning**: tools and arguments from the candidate list.
- **Conversation**: natural, consistent Turkish.
- Memory, latency, model size, license.

A model can be good at plans and poor at conversation; then it plans locally and a cloud model
talks.

## Evaluation

Built when the matching feature exists, not before:

| Bench | Checks |
|---|---|
| Intent / Tool | the right tool, today's test set |
| Safety | nothing that changes the game runs without approval (`next opponent` must not continue) |
| Entity | names resolve to the right player or club |
| Plan | several steps, right order, right arguments |
| Grounding | answers use only real game data (from saved snapshots of the game) |
| Dialogue | natural Turkish, consistent character |

Every result records the data hash, split, model, commit and machine.

## Not now

- Training an LLM.
- A required GPU or account.
- Running transfers or contracts without approval.
- Splitting the code into many projects. Interfaces (`IPlanner`, `IEntityResolver`, the
  approval policy) are added when a second implementation needs them.

## Open questions

- Which bridge reads give stable ids, and which only display text? Check again on FM27.
- Can the bridge tell when the save changed, so cached names expire?
- Which actions can be undone in the game?
- Should Mahmut work during a live match, or only between matches?
- How much personality by default?
