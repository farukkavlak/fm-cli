# AI roadmap

Why we build it and what it should become. How it works: [AI-DESIGN.md](AI-DESIGN.md).
Tasks in order: [AI-CHECKLIST.md](AI-CHECKLIST.md).

## Goal

- Play the real Football Manager from a terminal, fast and readable.
- Talk to it in Turkish or English.
- Later: an assistant manager, **Mahmut**, who knows the real save.
- A strong engineering project people want to read and try.

## What makes it good

- **Playable.** Every feature works as a plain command, with no AI.
- **Trustworthy.** It never invents players, offers, injuries or results.
- **Safe.** Nothing that changes the game runs without a yes.
- **Local first.** No account, key or internet needed by default.
- **Measured.** Every claim has a number from a held-out test.
- **Fun.** A live match view and an assistant with some character.

## What Mahmut should feel like

Examples (made up, not real save data):

- "Mahmut, kadroda durumlar ne?" → he reads squad and health, then answers with real names.
- "Rinaldi'yi sat, yerine genç bir sol bek bul." → he checks offers, asks before selling, then lists left backs.
- "Geçen hafta konuştuğumuz pres planına dönelim." → he finds the saved plan and asks before applying it.

Mahmut is:

- a personality,
- plus real game data,
- plus memory,
- plus safe actions.

Not a system prompt on top of a chat model.

## Not in the first release

- Transfers or contracts without approval.
- Expert tactics from day one.
- Training our own LLM.
- A required GPU or online account.
- Any claim of official Sports Interactive support.

## Phases

- **0 — Clean baseline:** clean test data, fixed metrics, reproducible numbers.
- **1 — Candidates + local LLM test:** what a small local model can do, measured.
- **2 — One safety policy:** CLI, MCP and Mahmut follow the same rules.
- **3 — FM27 + names from the save:** port the bridge, match names to game ids.
- **4 — Mahmut, read-only:** squad, injuries, fixtures, offers, from real data.
- **5 — Plans, memory, advice:** multi-step plans, save memory, transfer and tactic advice.
- **6 — Polish:** local Mahmut if possible, personality, public release.

Each phase ends with something that works in a real save.

## For the README and portfolio

Show:

- **Game-aware AI:** answers read the real save.
- **Model-agnostic:** the same assistant runs on different models.
- **Safe, measured tool use:** commands are checked, approved and scored.
- The game bridge, the .NET CLI and MCP server.
- Fine-tuned multilingual embeddings, int8 ONNX, a C# tokenizer that matches Python.
- Held-out tests, noisy variants, honest numbers.
- The live match view, drawn from real match data.

Rules for showing it:

- A 10–20 second demo first: ask Mahmut, approve a change, see it done, watch a match.
- Mark each feature: done, experimental or planned.
- No hand-picked demo sentences as proof.
- No game assets in the repo; check the game's modding terms before release.

## Risks

| Risk | What we do |
|---|---|
| Local LLM too slow | small quantized model, hybrid routing, a time budget |
| Made-up facts | answers built only from what the game returned |
| Unsafe actions | one approval policy, checks before and after |
| Overfitting our own sentences | family-based split, a final test set, real user sentences later |
| Stale save data | cached data expires when the save changes |
| Too much complexity | small working slices, no framework without need |
| A game update breaks the bridge | identity checks, version notes (`docs/PORTING.md`) |
| Licenses | check models, packages and game terms before release |
| Persona gets in the way | adjustable tone, fast command mode stays |
| Privacy | data stays on the machine, easy to delete |
