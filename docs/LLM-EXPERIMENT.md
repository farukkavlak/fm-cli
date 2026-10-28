# Local LLM experiment plan

Which jobs can a small local model do, with FM open on this Mac? Status: **approved 28 Oct.** Starts once disk space is freed.

## Machine (28 Oct, FM open)

- MacBook Air M2, 8 GB RAM.
- Swap already 6.0 of 7.2 GB used, 37% memory free.
- Disk: 5.5 GB free.
- So: one model on disk at a time, model file ≤ 3 GB, delete it after its run.

## Runner

- `llama.cpp` from Homebrew (`llama-server`; the exact build goes in every result).
- Not Ollama: the installed 0.9.5 is old, and llama.cpp gives the exact GGUF file and its SHA.
- JSON output forced by a JSON schema (llama.cpp grammar), so a plan always parses.
- Context 4096 (8192 for conversation), temperature 0, fixed seed, Metal on.
- One slot (`-np 1`) and no prompt cache (`--cache-ram 0`): the defaults (4 slots, up to 8 GB cache) cost 2.6 GB even for 0.8B.

## Models

| Model | GGUF (Hugging Face) | Size | License |
|---|---|---|---|
| Qwen3.5 0.8B | `ggml-org/Qwen3.5-0.8B-GGUF` Q8_0 | 0.83 GB | Apache 2.0 |
| Qwen3.5 2B | `bartowski/Qwen_Qwen3.5-2B-GGUF` Q8_0 | 2.08 GB | Apache 2.0 |
| Qwen3.5 4B | `bartowski/Qwen_Qwen3.5-4B-GGUF` Q4_K_M | 3.01 GB | Apache 2.0 |
| Gemma 4 E2B | `ggml-org/gemma-4-E2B-it-GGUF` Q4_0 | 2.84 GB | Apache 2.0 |

- No official Qwen GGUF: ggml-org (the llama.cpp team) where it exists, else bartowski.
- Gemma: Google's own QAT file is 3.35 GB, over the limit; ggml-org Q4_0 instead.
- Each download is checked against the repo's SHA-256; the SHA goes in every result.
- Out: anything over 3 GB (Qwen3.5 9B, Gemma 4 E4B), Phi-4 mini (mostly English), Llama (weak Turkish, custom license).

## Jobs and benches

Each job is scored on its own. Prompts are tuned on dev; held decides; final only confirms.

### 1. Planning (tool + arguments)

- Input: the sentence, the router's top-5 candidates per ask (`fm __route` "asks"), each candidate's MCP schema.
- Output: `{"steps": [{"tool", "args"}], "missing": [...]}`, or none.
- New script `tools/nl-llm.py`: a router for `nl-score.py --router`, so the numbers are the same metrics.
- Sets: base held, noisy held, `13-multi` held.
- Metrics: top-1, tool+args, ask, multi, none, false none.
- Compared with: today's router (`mpnet-f-q` + readers) on the same split.

### 2. Arguments alone

- Same as 1, but the right tool is given (`nl-score.py --given-tool`).
- Compared with: readers alone, 90.0% held (reads high).

### 3. Conversation

- 20 fixed questions ("kadroda durumlar ne?", "sakat kim?", …) with a saved game snapshot: real `fm` outputs from the current save, saved as text.
- Model answers in Turkish as Mahmut, from the snapshot only.
- Scored by Faruk, blind (model names hidden), 1–5: natural Turkish, in character, useful.
- Counted by script: every player or club name in the answer is in the snapshot (invented names = 0 allowed).

### 4. Memory

- 10 short scripts of 2–4 turns: "Rinaldi nasıl?" → "onu sat" → "kaç para?".
- Metric: the reference resolves to the right player or plan.

### 5. Cost on this Mac

- Model load time, first token, full answer: median and p90.
- Runner RSS, swap growth during the run.
- FM still responds: `fm status` time before and during the run.

## Pass marks

A model passes a job only if all of its marks hold on held.

| Job | Pass |
|---|---|
| Planning | tool+args ≥ today's router; false none not higher; multi ≥ router |
| Arguments | ≥ readers alone |
| Conversation | average ≥ 4/5; 0 invented names in 20 |
| Memory | ≥ 9 of 10 |
| Cost | planning p90 ≤ 2 s; answer p90 ≤ 8 s; RSS ≤ 3 GB; swap growth ≤ 1 GB; `fm status` ≤ 2× slower |

- A model may pass planning and fail conversation. That is a result, not a failure.
- If nothing passes planning, the router stays and the LLM idea is only for the cloud.

## Order

1. Install `llama.cpp`; write `tools/nl-llm.py` and the conversation/memory benches with no model.
2. Qwen3.5 0.8B → 2B → 4B → Gemma 4 E2B, one at a time: download, all benches, save results, delete.
3. Stop early if a smaller model already passes everything, or a bigger one breaks the cost marks.
4. Final set for the best model per job.
5. Decision note (last box of Phase 1).

## Notes so far

- Qwen3.5 0.8B: out. Measured with the first planner (empty plans allowed): arguments 38.7% (readers 90.0%), memory 0/10, invents numbers and names. Not measured again with the fixed planner (Faruk, 29 Oct).
- Planner fix after 0.8B: `none` is a choice, at least one step, one step per split ask, training examples per tool. Held is scored again for every other model.

## Results

- Planning and arguments: `tests/nl/results/llm-<model>.json` (same manifest as other results, plus GGUF SHA and runner build).
- Conversation, memory, cost: `tests/llm/results/<model>.json` (`tools/llm-bench.py`).
- Game snapshot: `~/.cache/fm-cli/llm/snapshot-2029-08-04/`, saved `fm` outputs; not in git (game data), its SHA is in each result.
- Blind scores: `tests/llm/`.

## Approved (28 Oct)

- [x] The four models above
- [x] `brew install llama.cpp`
- [x] Up to ~3 GB download per model, deleted after its run
- [x] Blind scoring of the 20 answers, done together
