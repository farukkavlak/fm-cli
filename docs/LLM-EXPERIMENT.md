# Local LLM experiment plan

Which jobs can a small local model do, with FM open on this Mac? Status: **waiting for approval.**
Nothing is downloaded before the plan is approved.

## Machine (28 Oct, FM open)

- MacBook Air M2, 8 GB RAM.
- Swap already 6.0 of 7.2 GB used, 37% memory free.
- Disk: 5.5 GB free.
- So: one model on disk at a time, model file ≤ 3 GB, delete it after its run.

## Runner

- `llama.cpp` from Homebrew (`llama-server`, stable 0.7.0 today; record the exact build).
- Not Ollama: the installed 0.9.5 is old, and llama.cpp gives the exact GGUF file and its SHA.
- JSON output forced by a JSON schema (llama.cpp grammar), so a plan always parses.
- Context 4096, temperature 0, fixed seed, Metal on.

## Models

| Model | Quant | File | License |
|---|---|---|---|
| Qwen3.5 0.8B Instruct | Q8_0 | ~0.9 GB | Apache 2.0 |
| Qwen3.5 2B Instruct | Q8_0 | ~2.0 GB | Apache 2.0 |
| Qwen3.5 4B Instruct | Q4_K_M | ~2.7 GB | Apache 2.0 |
| Gemma 4 E2B | Q4_K_M | check | check before download |

- GGUF from the vendor's own Hugging Face repo, else a well-known converter; record repo, file and SHA-256.
- Out: anything over 3 GB (Qwen3.5 9B, Gemma 4 E4B), Phi-4 mini (mostly English), Llama (weak Turkish, custom license).
- Sizes and Gemma's license are checked on the model page before download.

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

## Results

- Planning and arguments: `tests/nl/results/llm-<model>.json` (same manifest as other results, plus GGUF SHA and runner build).
- Conversation, memory, cost: `tests/llm/results/<model>.json`.
- Snapshots and blind scores: `tests/llm/`.

## Needs approval

- [ ] The four models above (or fewer)
- [ ] `brew install llama.cpp`
- [ ] Up to ~3 GB download per model, deleted after its run
- [ ] Faruk scores 20 blind answers per model that reaches the conversation bench
