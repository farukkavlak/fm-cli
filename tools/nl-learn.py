#!/usr/bin/env python3
"""Measures the learning router (T206): what teaching the user's choices does.

The held sentences the router gets wrong are taught as if the user had picked the right tool
from the list (fm __route --learned). Then are asked again:

- taught: the same sentences, as typed
- noisy: their noisy forms from tests/nl/expanded/ (typos, filler, folded letters)
- others: every other held sentence and noisy form, to see that teaching breaks nothing

For each: top-1 (the right tool), sure (runs without asking: share >= 0.8 and right) and
sure-wrong (runs the wrong tool without asking), before and after, for each weight given.

  tools/nl-learn.py --router "cli/fm/bin/Debug/net10.0/fm __route --model ~/.cache/fm-cli/nl/model" --weights 1 2 3
"""
import argparse
import importlib.util
import json
import pathlib
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("nl_score", HERE / "nl-score.py")
score = importlib.util.module_from_spec(spec)
spec.loader.exec_module(score)

SURE = 0.8


def right(case, out):
	return score.judge(case, out)[0]


def numbers(cases, outs):
	n = len(cases)
	top1 = sum(right(c, o) for c, o in zip(cases, outs))
	sure = sum(right(c, o) and o.get("share", 0) >= SURE for c, o in zip(cases, outs))
	wrong = sum(not right(c, o) and o.get("call") is not None and o.get("share", 0) >= SURE for c, o in zip(cases, outs))
	return f"top-1 {score.pct(top1, n):>6}  sure {score.pct(sure, n):>6}  sure-wrong {score.pct(wrong, n):>6}  (n={n})"


def main():
	ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	ap.add_argument("--router", required=True, help="fm __route --model DIR")
	ap.add_argument("--weights", nargs="+", default=["2"], help="how much a taught sentence counts")
	ap.add_argument("--near", nargs="+", default=["0.9"], help="from this similarity up the nearest taught sentence decides")
	ap.add_argument("--split", default="held", choices=["dev", "held"])
	args = ap.parse_args()

	sets = score.load("all")
	base = [c for c in sets["base"] if c["split"] == args.split]
	noisy = [c for c in sets["expanded"] if c["split"] == args.split]
	before, _ = score.run_router(args.router, [c["text"] for c in base])
	# a sentence the rules split into several asks offers no list, so the user never picks for it
	taught = [c for c, o in zip(base, before) if isinstance(c.get("call"), str) and not right(c, o) and not isinstance(o.get("call"), list)]
	# a noisy form names its case by file and line
	by_line = {}
	for f in sorted(score.CASES.glob("*.jsonl")):
		for n, line in enumerate(f.read_text().splitlines(), 1):
			if line.strip():
				by_line[f"{f.name}:{n}"] = json.loads(line)
	taught_texts = {c["text"] for c in taught}
	def of_taught(v):
		src = by_line.get(v.get("from", ""), {})
		return (src.get("tr") or src.get("en")) in taught_texts
	noisy_taught = [v for v in noisy if of_taught(v)]
	others = [c for c in base if c["text"] not in taught_texts] + [v for v in noisy if not of_taught(v)]
	print(f"{args.split}: {len(base)} sentences, {len(taught)} wrong and taught, {len(noisy_taught)} noisy forms of them")

	groups = [("taught", taught), ("noisy", noisy_taught), ("others", others)]
	texts = [c["text"] for _, g in groups for c in g]
	def report(label, outs):
		print(label)
		at = 0
		for name, g in groups:
			print(f"  {name:7} {numbers(g, outs[at:at + len(g)])}")
			at += len(g)

	outs, _ = score.run_router(args.router, texts)
	report("before", outs)
	with tempfile.TemporaryDirectory() as tmp:
		learned = pathlib.Path(tmp) / "learned.json"
		learned.write_text(json.dumps({"sentences": [{"text": c["text"], "typed": c["text"], "tool": c["call"], "shown": c["call"]} for c in taught]}, ensure_ascii=False))
		for w in args.weights:
			for near in args.near:
				outs, _ = score.run_router(f"{args.router} --learned {learned} --learned-weight {w} --learned-near {near}", texts)
				report(f"after, weight {w}, near {near}", outs)


if __name__ == "__main__":
	main()
