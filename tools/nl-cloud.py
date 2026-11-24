#!/usr/bin/env python3
"""Measures smart mode (T207): the cloud model asked where the router is not sure.

On the held sentences (base, and every third noisy form), for the sentences the router is not
sure of: how many now run without a list (the cloud model agreed with the router's pick) and
how many of those are right, and how often the cloud's own pick put first in the list is right.
The provider comes from FM_AI_URL, FM_AI_KEY, FM_AI_MODEL and FM_AI_HEADERS (see `fm ai`).

  FM_AI_URL=https://opencode.ai/zen/go FM_AI_MODEL=claude-haiku-5-5 FM_AI_KEY=... \\
  FM_AI_HEADERS="x-opencode-session: {session}" tools/nl-cloud.py
"""
import importlib.util
import os
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("nl_score", HERE / "nl-score.py")
score = importlib.util.module_from_spec(spec)
spec.loader.exec_module(score)

ROUTER = f"cli/fm/bin/Debug/net10.0/fm __route --model {os.path.expanduser('~/.cache/fm-cli/nl/model')}"


def main():
	if not os.environ.get("FM_AI_URL") or not os.environ.get("FM_AI_MODEL"):
		sys.exit("set FM_AI_URL, FM_AI_MODEL (and FM_AI_KEY)")
	router = sys.argv[1] if len(sys.argv) > 1 else ROUTER
	sets = score.load("all")
	print(f"model {os.environ['FM_AI_MODEL']}")
	for which in ["base", "expanded"]:
		rows = [c for c in sets[which] if c["split"] == "held"]
		if which == "expanded":
			rows = rows[::3]
		out, _ = score.run_router(router + " --cloud", [c["text"] for c in rows])
		unsure, ran, ran_right, first, first_right, seconds = 0, 0, 0, 0, 0, []
		for c, o in zip(rows, out):
			if "cloud_s" not in o:
				continue
			unsure += 1
			seconds.append(o["cloud_s"])
			if o.get("cloud"):
				ran += 1
				ran_right += score.judge(c, o)[0]
			if "cloud_first" in o:
				first += 1
				first_right += c.get("call") == o["cloud_first"]
		seconds.sort()
		print(f"  {which:9} n={len(rows)} unsure={unsure} | runs without a list {ran} ({ran_right} right) | cloud pick first in the list {first} ({first_right} right)"
			f" | median {seconds[len(seconds) // 2]:.1f} s, p90 {seconds[int(len(seconds) * .9)]:.1f} s")


if __name__ == "__main__":
	main()
