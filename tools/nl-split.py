#!/usr/bin/env python3
"""Gives every new test case in tests/nl/*.jsonl its side: dev, held or final.

A case that joins a family with a side takes that side; a new family gets one by a hash, or dev
when a training sentence is near it (rules in tools/nl_family.py). Cases that have a side keep it,
so numbers stay comparable. Prints the counts per side.

  tools/nl-split.py
"""
import json
import sys
from collections import Counter

sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parent))
from nl_family import SPLITS, families, new_split, rows  # noqa: E402


def main():
	rs = rows()
	for fam in families(rs):
		have = {rs[i]["split"] for i in fam if rs[i]["test"] and rs[i]["split"]}
		side = next(iter(have)) if len(have) == 1 else new_split(rs, fam) if not have else None
		for i in fam:
			if rs[i]["test"] and not rs[i]["split"] and side:
				rs[i]["split"] = side
	changed = Counter()
	for path in sorted({r["path"] for r in rs if r["test"]}):
		lines = path.read_text().splitlines()
		for r in (r for r in rs if r["path"] == path and r["split"] and "split" not in r["case"]):
			line = lines[r["line"] - 1].rstrip()
			lines[r["line"] - 1] = line[:-1] + f', "split": {json.dumps(r["split"])}}}'
			changed[r["split"]] += 1
		path.write_text("\n".join(lines) + "\n")
	counts = Counter(r["split"] for r in rs if r["test"])
	total = sum(counts.values())
	print("new: " + (", ".join(f"{k} {v}" for k, v in changed.items()) or "none"))
	print(" · ".join(f"{s} {counts[s]} ({100 * counts[s] / total:.0f}%)" for s in SPLITS))
	if counts[None]:
		print(f"{counts[None]} cases in a family with two sides: see tools/nl-check.py")


if __name__ == "__main__":
	main()
