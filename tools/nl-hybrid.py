#!/usr/bin/env python3
"""Router first, LLM only where the router is unsure: scored from saved answers, no new LLM calls.

Takes the router's answers (fm __route --model, run here) and an LLM's planning log
(tools/nl-llm.py --log) for the same held sentences, and scores rules that pick one of the two:

- router: the router alone (free, offline)
- llm: the LLM alone
- unsure: the LLM when the router would not run its pick (under Router.Sure, 0.8, of the 15 nearest
  examples' vote, as Router.Understand; or no tool), or the rules split the sentence (the LLM does
  several asks better); else the router.
- unsure+back: as unsure, but when the LLM says "none" and the router has a tool, the router's.

  tools/nl-hybrid.py --log /tmp/plan-claude-haiku-5-5.log
"""
import argparse
import importlib.util
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SURE = 0.8


def module(name, file):
	spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / file)
	m = importlib.util.module_from_spec(spec)
	spec.loader.exec_module(m)
	return m


score = module("nl_score", "nl-score.py")
llm = module("nl_llm", "nl-llm.py")


def llm_answer(rec, tools, required, needs):
	"""The answer tools/nl-llm.py gave, rebuilt from its log line."""
	steps = llm.parse_steps(rec["raw"], rec["tools"], tools)
	calls = [s["tool"] for s in steps]
	argss = [{k: v for k, v in s["args"].items() if v is not False} for s in steps]
	ask = [x for s, a in zip(steps, argss) for x in llm.asks_of({"tool": s["tool"], "args": a}, required, needs)]
	if not calls:
		return {"call": None}
	if len(calls) == 1:
		return {"call": calls[0], "args": argss[0], "ask": ask}
	return {"call": calls, "args": argss, "ask": ask}


def main():
	ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	ap.add_argument("--log", required=True, help="tools/nl-llm.py --log of a held planning run")
	ap.add_argument("--split", default="held")
	a = ap.parse_args()
	cases = [c for c in score.load("base")["base"] if c["split"] == a.split]
	logs = {}
	for l in open(a.log):
		r = json.loads(l)
		logs[r["text"]] = r
	cases = [c for c in cases if c["text"] in logs]
	feed = "".join(json.dumps({"text": c["text"]}, ensure_ascii=False) + "\n" for c in cases)
	p = subprocess.run([str(score.FM), "__route", "--model", str(Path(llm.CANDIDATES_MODEL).expanduser()), "--cases", str(ROOT / "tests/nl")],
		input=feed, capture_output=True, text=True, cwd=ROOT, check=True)
	routed = [json.loads(l) for l in p.stdout.splitlines() if l.strip()]
	tools = llm.load_tools()
	required, needs = llm.load_needs()

	def unsure(r):
		return bool(r.get("asks")) or r.get("call") is None or (r.get("share") or 0) < SURE

	rules = {
		"router": lambda r, m: r,
		"llm": lambda r, m: m,
		"unsure": lambda r, m: m if unsure(r) else r,
		"unsure+back": lambda r, m: (r if m.get("call") is None and r.get("call") is not None else m) if unsure(r) else r,
	}
	sent = sum(unsure(r) for r in routed)
	print(f"{len(cases)} {a.split} sentences; the router is unsure of {sent} ({100 * sent / len(cases):.1f}%): only these go to the LLM\n")
	metrics = ("top-1", "tool+args", "ask", "multi", "none", "false none")
	print(f"{'':13}" + "".join(f"{m:>12}" for m in metrics))
	for name, pick in rules.items():
		rows = [(c, pick(r, llm_answer(logs[c["text"]], tools, required, needs))) for c, r in zip(cases, routed)]
		judged = [(c, o, *score.judge(c, o)) for c, o in rows]
		kinds = {}
		for c, o, t, ok, kind, _ in judged:
			kinds.setdefault(kind, [0, 0])
			kinds[kind][0] += ok
			kinds[kind][1] += 1
		real = [(c, o) for c, o in rows if c.get("call") is not None]
		counts = {
			"top-1": (sum(j[2] for j in judged), len(judged)),
			"tool+args": (sum(j[3] for j in judged), len(judged)),
			"ask": tuple(kinds.get("ask", [0, 0])),
			"multi": tuple(kinds.get("multi", [0, 0])),
			"none": tuple(kinds.get("none", [0, 0])),
			"false none": (sum(1 for c, o in real if o.get("call") is None), len(real)),
		}
		print(f"{name:13}" + "".join(f"{score.pct(*counts[m]):>12}" for m in metrics))


if __name__ == "__main__":
	main()
