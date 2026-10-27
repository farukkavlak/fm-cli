#!/usr/bin/env python3
"""Scores a natural-language router on the test set (tests/nl/*.jsonl, see tests/nl/README.md).

The router is any command that reads one JSON object per line on stdin ({"text": "..."}) and
writes one per line on stdout, in the same order:

  {"call": "fm_squad", "args": {"view": "fitness"}}
  {"call": "fm_match", "ask": ["mode"]}
  {"call": ["fm_squad", "fm_tactic"], "args": [{}, {}]}
  {"call": null}

A case is split into "dev" (rules may be tuned on it) or "held" (never looked at while tuning)
by a hash of its sentence; a noisy variant from tests/nl/expanded/ follows the case it came from.
Scores, per set and split: the right tool, the right tool with the right arguments, and asks
and "nothing does this" counted on their own.

  tools/nl-score.py                          # fm __route, base set and expanded set
  tools/nl-score.py --fails 30 --split dev   # what went wrong, to tune on
  tools/nl-score.py --router "python3 my_router.py" --set base
  tools/nl-score.py --given-tool              # the argument readers alone: each case's own tool handed to fm __route
  tools/nl-score.py --given-tool --set train  # the same on the training-only sentences of tests/nl/train/
"""
import argparse
import hashlib
import json
import re
import shlex
import subprocess
import sys
import time
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "tests/nl"
EXPANDED = CASES / "expanded/all.jsonl"
FM = ROOT / "cli/fm/bin/Debug/net10.0/fm"
CONFIRM = {"yes", "confirm", "send"}
HELD = 3  # of every 10 cases


def fold(s):
	s = str(s).lower().replace("ı", "i").replace("i̇", "i")
	s = "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")
	return re.sub(r"[^a-z0-9.]+", " ", s).strip()


def split_of(text):
	return "held" if int(hashlib.sha1(text.encode()).hexdigest(), 16) % 10 < HELD else "dev"


def load(which):
	base, by_ref = [], {}
	for f in sorted(CASES.glob("*.jsonl")):
		for n, line in enumerate(f.read_text().splitlines(), 1):
			if not line.strip():
				continue
			c = json.loads(line)
			c["lang"] = "tr" if "tr" in c else "en"
			c["text"] = c[c["lang"]]
			c["group"] = f.stem
			c["split"] = split_of(c["text"])
			by_ref[f"{f.name}:{n}"] = c
			base.append(c)
	sets = {}
	if which == "train":
		# training-only sentences (never in dev or held); the ones with arguments tune the readers
		rows = []
		for f in sorted((CASES / "train").glob("*.jsonl")):
			for line in f.read_text().splitlines():
				if line.strip():
					c = json.loads(line)
					c["lang"] = "tr" if "tr" in c else "en"
					c["text"], c["group"], c["split"] = c[c["lang"]], f.stem, "dev"
					rows.append(c)
		sets["train"] = rows
	if which in ("base", "all"):
		sets["base"] = base
	if which in ("expanded", "all"):
		if not EXPANDED.exists():
			sys.exit("no expanded set: run tools/nl-expand.py first")
		rows = []
		for line in EXPANDED.read_text().splitlines():
			c = json.loads(line)
			src = by_ref[c["from"]]
			c["lang"] = "tr" if "tr" in c else "en"
			c["text"] = c[c["lang"]]
			c["group"], c["split"] = src["group"], src["split"]
			rows.append(c)
		sets["expanded"] = rows
	return sets


def money(v):
	s = fold(v).replace(" ", "")
	m = re.fullmatch(r"([0-9]+(?:[.,][0-9]+)?)(k|bin|m|mn|milyon|million|b|milyar)?", s.replace(",", "."))
	if not m:
		digits = re.sub(r"[.,]", "", s)
		return float(digits) if digits.isdigit() else None
	num, unit = m.groups()
	if unit is None and num.count(".") >= 1 and len(num.split(".")[-1]) == 3:
		return float(num.replace(".", ""))
	mult = {"k": 1e3, "bin": 1e3, "m": 1e6, "mn": 1e6, "milyon": 1e6, "million": 1e6, "b": 1e9, "milyar": 1e9}.get(unit, 1)
	return float(num) * mult


def same_value(want, got):
	if isinstance(want, bool) or isinstance(got, bool):
		return want == got
	if isinstance(want, list):
		return isinstance(got, list) and len(want) == len(got) and all(same_value(a, b) for a, b in zip(want, got))
	if isinstance(want, int) or isinstance(got, int):
		try:
			return int(want) == int(got)
		except (TypeError, ValueError):
			return False
	w, g = fold(want), fold(got)
	if w == g:
		return True
	mw, mg = money(want), money(got)
	if mw is not None and mg is not None and re.search(r"\d", w) and re.search(r"\d", g):
		return abs(mw - mg) < 1e-6
	# names by prefix, free text loosely
	if w and g and (w.startswith(g) or g.startswith(w)):
		return True
	ww, gw = set(w.split()), set(g.split())
	return bool(ww) and len(ww & gw) * 2 >= max(len(ww), len(gw))


def same_args(want, got):
	# a confirmation the router adds is no mistake, unless the case asks for it ("send" submits a squad list)
	got = {k: v for k, v in (got or {}).items() if k not in CONFIRM or k in want}
	if set(want) != set(got):
		return False
	return all(same_value(want[k], got[k]) for k in want)


def as_list(x):
	return x if isinstance(x, list) else ([] if x is None else [x])


def judge(case, out):
	"""(tool right, all right, kind, why)"""
	want_call = case.get("call")
	got_call = out.get("call")
	asks = set(as_list(case.get("ask")))
	got_asks = set(as_list(out.get("ask")))
	if want_call is None:
		kind = "none"
		ok = got_call is None
		return ok, ok, kind, "" if ok else f"→ {got_call}"
	if isinstance(want_call, list):
		kind = "multi"
		wa = case.get("args") or [{}] * len(want_call)
		ga = out.get("args") if isinstance(out.get("args"), list) else [out.get("args") or {}]
		tool_ok = as_list(got_call) == want_call
		all_ok = tool_ok and len(ga) == len(wa) and all(same_args(a, b) for a, b in zip(wa, ga))
		return tool_ok, all_ok, kind, "" if all_ok else f"→ {got_call} {ga}"
	kind = "ask" if asks else "call"
	options = [{"call": want_call, "args": case.get("args", {})}] + case.get("or", [])
	got_args = out.get("args") if isinstance(out.get("args"), dict) else {}
	for o in options:
		if isinstance(o["call"], list):
			ga = out.get("args") if isinstance(out.get("args"), list) else []
			if as_list(got_call) == o["call"]:
				ok = len(ga) == len(o["call"]) and all(same_args(a, b) for a, b in zip(o.get("args") or [{}] * len(o["call"]), ga))
				return True, ok, kind, "" if ok else f"args {ga}"
			continue
		if got_call != o["call"]:
			continue
		if asks and o is options[0]:
			# the asked argument must be asked, not guessed
			ok = asks <= got_asks and not (asks & set(got_args)) and same_args(o.get("args", {}), {k: v for k, v in got_args.items() if k not in asks})
		else:
			ok = same_args(o.get("args", {}), got_args)
		if ok:
			return True, True, kind, ""
		return True, False, kind, f"args {got_args}" + (f" ask {sorted(got_asks)}" if asks else "")
	return False, False, kind, f"→ {got_call} {got_args}"


def run_router(cmd, texts, tools=None):
	t0 = time.perf_counter()
	feed = "".join(json.dumps({"text": t, **({"tool": tools[i]} if tools else {})}, ensure_ascii=False) + "\n" for i, t in enumerate(texts))
	p = subprocess.run(cmd, input=feed, capture_output=True, text=True, shell=isinstance(cmd, str))
	took = time.perf_counter() - t0
	if p.returncode != 0:
		sys.exit(f"router failed ({p.returncode}):\n{p.stderr[-2000:]}")
	lines = [l for l in p.stdout.splitlines() if l.strip()]
	if len(lines) != len(texts):
		sys.exit(f"router gave {len(lines)} answers for {len(texts)} sentences")
	outs = [json.loads(l) for l in lines]
	return ([o["forced"] for o in outs] if tools else outs), took


def pct(a, b):
	return f"{100 * a / b:5.1f}%" if b else "    -"


def main():
	ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	ap.add_argument("--router", help="router command (default: fm __route)")
	ap.add_argument("--set", choices=["base", "expanded", "all", "train"], default="all")
	ap.add_argument("--split", choices=["dev", "held", "all"], default="all", help="which split --fails lists")
	ap.add_argument("--fails", type=int, default=0, help="list this many failures of the first set")
	ap.add_argument("--group", help="only cases of this group (file stem prefix, e.g. 05)")
	ap.add_argument("--by", choices=["group", "lang", "tool", "kind"], action="append", default=[], help="breakdown")
	ap.add_argument("--json", action="store_true", help="print the totals as JSON")
	ap.add_argument("--given-tool", action="store_true", help="hand each case's own tool to fm __route: the argument readers alone")
	a = ap.parse_args()
	cmd = a.router or [str(FM), "__route"]
	totals = {}
	for name, cases in load(a.set).items():
		if a.group:
			cases = [c for c in cases if c["group"].startswith(a.group)]
		if a.given_tool:
			cases = [c for c in cases if isinstance(c.get("call"), str) and (name != "train" or "args" in c or "ask" in c)]
			outs, took = run_router([str(FM), "__route"], [c["text"] for c in cases], [c["call"] for c in cases])
		else:
			outs, took = run_router(cmd, [c["text"] for c in cases])
		rows = [(c, o, *judge(c, o)) for c, o in zip(cases, outs)]
		print(f"\n== {name}: {len(cases)} sentences, {1000 * took / max(len(cases), 1):.2f} ms each (whole run {took:.1f} s)")
		print(f"{'':10}{'n':>6} {'tool':>7} {'tool+args':>10}   {'call':>7} {'ask':>7} {'multi':>7} {'none':>7} {'false none':>11}")
		totals[name] = {}
		for sp in ("dev", "held", "all"):
			rs = [r for r in rows if sp == "all" or r[0]["split"] == sp]
			by_kind = defaultdict(lambda: [0, 0])
			for c, o, t, ok, kind, _ in rs:
				by_kind[kind][0] += ok
				by_kind[kind][1] += 1
			false_none = sum(1 for c, o, *_ in rs if c.get("call") is not None and o.get("call") is None)
			tool = sum(r[2] for r in rs)
			full = sum(r[3] for r in rs)
			kinds = "".join(f" {pct(*by_kind[k]):>7}" for k in ("call", "ask", "multi", "none"))
			print(f"{sp:10}{len(rs):>6} {pct(tool, len(rs)):>7} {pct(full, len(rs)):>10}  {kinds} {pct(false_none, len(rs)):>11}")
			totals[name][sp] = {"n": len(rs), "tool": tool, "full": full}
		for by in a.by:
			agg = defaultdict(lambda: [0, 0, 0])
			for c, o, t, ok, kind, _ in rows:
				keys = {"group": [c["group"]], "lang": [c["lang"]], "kind": [kind],
					"tool": [str(x) for x in as_list(c.get("call"))] or ["(none)"]}[by]
				for k in keys:
					agg[k][0] += t
					agg[k][1] += ok
					agg[k][2] += 1
			print(f"\n  by {by}:")
			for k, (t, ok, n) in sorted(agg.items(), key=lambda kv: kv[1][1] / kv[1][2]):
				print(f"  {k:28}{n:>6} {pct(t, n):>7} {pct(ok, n):>10}")
		if a.fails and name == next(iter(load(a.set))):
			fails = [r for r in rows if not r[3] and (a.split == "all" or r[0]["split"] == a.split)]
			conf = Counter((str(r[0].get("call")), str(r[1].get("call"))) for r in fails if not r[2])
			print(f"\n  most confused (want → got):")
			for (w, g), n in conf.most_common(15):
				print(f"  {n:4}  {w} → {g}")
			print(f"\n  {len(fails)} failures, first {a.fails}:")
			for c, o, t, ok, kind, why in fails[: a.fails]:
				want = {k: c[k] for k in ("call", "args", "ask", "or") if k in c}
				print(f"  [{c['group'][:2]}] {c['text']!r}\n        want {json.dumps(want, ensure_ascii=False)}\n        got  {json.dumps(o, ensure_ascii=False)}")
	if a.json:
		print(json.dumps(totals))


if __name__ == "__main__":
	main()
