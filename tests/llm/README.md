# LLM benches

For the local LLM experiment ([docs/AI.md](../../docs/AI.md)).
Planning and arguments use the NL test set and `tools/nl-score.py`; these files are the rest.

| File | What |
|---|---|
| `conversation.jsonl` | 20 questions to the assistant; `reads` = the saved `fm` outputs it answers from |
| `memory.jsonl` | 10 short talks; the last turn refers back ("onu sat"); scored as tool+args |
| `results/` | one file per model: answers, memory plans, cost, manifest |

Run (the snapshot is a folder of `fm <command>` outputs, `<command>.txt`, not in git):

```bash
tools/nl-score.py --router "tools/nl-llm.py --gguf FILE --log /tmp/plan.log" --set base --only held --save llm-NAME
tools/llm-bench.py --gguf FILE --snapshot ~/.cache/fm-cli/llm/snapshot-2029-08-04 --plan-log /tmp/plan.log --save NAME
```

- Unknown names: capitalised words in an answer that are not in its snapshot files. Flagged by the
  script, confirmed by hand.
- RAM: llama-server's footprint (Metal buffers included) plus the GGUF size (the weights are a mapped file, outside the footprint).
- Memory: earlier turns go in as the plans they became; only the last turn is scored.
