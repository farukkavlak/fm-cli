#!/usr/bin/env python3
"""The assistant (`fm ask`, T208) against the game that is open: questions and short talks.

    tools/ask-bench.py [--save NAME]

- tests/llm/conversation.jsonl: each question asked in a new talk. Counted: answered, read something,
  names flagged as unread (each to be checked by hand: a real slip or a word the check took for a name).
- tests/llm/ask-talks.jsonl: short talks whose last turn refers back. {p1} and {p2} are two of our
  own players, taken from the squad. The last turn either reads the player meant (`reads`) or, asked to
  act, names the command that would (`act`, in backticks) and runs nothing.

Uses the installed `fm` and the model set up with `fm ai set`. The talk is kept in a file of its own.
Results: tests/llm/results/ask-NAME.jsonl (answers in full, for reading).
"""
from __future__ import annotations

import argparse
import json
import os
import statistics
import subprocess
import tempfile
import unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LLM = ROOT / "tests/llm"


def fold(text: str) -> str:
    text = text.replace("ı", "i").replace("İ", "i")
    return "".join(c for c in unicodedata.normalize("NFKD", text.lower()) if not unicodedata.combining(c))


def ask(question: str, new: bool, env: dict) -> dict:
    words = ["fm", "ask", *(["--new"] if new else []), question, "--json"]
    done = subprocess.run(words, capture_output=True, text=True, env=env, stdin=subprocess.DEVNULL, timeout=600)
    try:
        return json.loads(done.stdout)
    except json.JSONDecodeError:
        return {"Answer": "", "Read": [], "Refused": [], "Unread": [], "Seconds": 0, "Error": (done.stderr or done.stdout).strip()[:300]}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--save", default="run")
    args = parser.parse_args()
    env = dict(os.environ, FM_ASK_FILE=str(Path(tempfile.mkdtemp()) / "ask.json"))

    squad = json.loads(subprocess.run(["fm", "squad", "--json"], capture_output=True, text=True, stdin=subprocess.DEVNULL).stdout)
    names = [p["Name"] for p in squad if p.get("Name")]
    p1, p2 = names[len(names) // 3], names[2 * len(names) // 3]
    print(f"players: {p1}, {p2}")

    out = []
    for line in (LLM / "conversation.jsonl").read_text().splitlines():
        q = json.loads(line)
        r = ask(q["q"], True, env)
        out.append({"id": q["id"], "q": q["q"], **r})
        print(f"{q['id']} {r['Seconds']:5.1f}s read={len(r['Read'])} unread={r['Unread']} {r['Error'] or ''}")

    talks = []
    for line in (LLM / "ask-talks.jsonl").read_text().splitlines():
        t = json.loads(line)
        rs = []
        for i, turn in enumerate(t["turns"]):
            q = turn["user"].replace("{p1}", p1).replace("{p2}", p2)
            r = ask(q, i == 0, env)
            rs.append({"q": q, **r})
        last, want = rs[-1], t["turns"][-1]
        if "reads" in want:
            meant = fold(want["reads"].replace("{p1}", p1).replace("{p2}", p2).split()[-1])
            ok = any(meant in fold(w) for w in last["Read"])
        else:
            ok = f"`{want['act']}" in last["Answer"] and not last["Refused"]
        talks.append(ok)
        out.append({"id": t["id"], "turns": rs, "ok": ok})
        print(f"{t['id']} {'ok ' if ok else 'NO '} {last['q']!r}: read={last['Read']} {last['Answer'][:120]!r}")

    answered = [o for o in out if "q" in o]
    seconds = [o["Seconds"] for o in answered if not o["Error"]]
    print(f"\nquestions: {sum(1 for o in answered if not o['Error'])}/{len(answered)} answered, "
          f"{sum(1 for o in answered if o['Read'])} read something, {sum(1 for o in answered if o['Unread'])} with names flagged, "
          f"median {statistics.median(seconds) if seconds else 0:.1f} s")
    print(f"talks: {sum(talks)}/{len(talks)} meant the one before")
    results = LLM / "results" / f"ask-{args.save}.jsonl"
    results.parent.mkdir(exist_ok=True)
    results.write_text("".join(json.dumps(o, ensure_ascii=False) + "\n" for o in out))
    print(f"-> {results.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
