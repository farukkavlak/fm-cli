#!/usr/bin/env python3
"""Conversation, memory and cost benches for a local LLM (docs/LLM-EXPERIMENT.md, tests/llm/README.md).

  tools/llm-bench.py --gguf ~/.cache/fm-cli/llm/FILE.gguf --snapshot ~/.cache/fm-cli/llm/snapshot-2029-08-04 \
      [--plan-log FILE] --save NAME

- conversation: tests/llm/conversation.jsonl; Mahmut answers each question from the snapshot files it
  reads (saved `fm` outputs). Capitalised words that are not in those files are flagged as possibly
  invented names, to be checked by hand with the blind scores.
- memory: tests/llm/memory.jsonl; earlier turns go in as the plans they became, the last turn is
  planned from its candidate tools (tools/nl-llm.py) and scored like tools/nl-score.py tool+args.
- cost: model load time, llama-server memory, swap growth, `fm status` time before and during a
  generation; with --plan-log (tools/nl-llm.py --log) also planning time per sentence.
"""
import argparse
import hashlib
import importlib.util
import json
import re
import statistics
import subprocess
import sys
import threading
import time
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BENCH = ROOT / "tests/llm"


def module(name, file):
	spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / file)
	m = importlib.util.module_from_spec(spec)
	spec.loader.exec_module(m)
	return m


llm = module("nl_llm", "nl-llm.py")
score = module("nl_score", "nl-score.py")

MAHMUT = """Sen Mahmut'sun: kullanıcının Football Manager kariyerindeki yardımcı antrenör.
- Kullanıcının dilinde cevap ver (Türkçe soruya Türkçe, İngilizce soruya İngilizce).
- Yalnızca aşağıdaki oyun verisini kullan. Veride olmayan oyuncu, kulüp, sayı veya olay uydurma; bilmiyorsan söyle.
- Kısa ve net ol: en fazla 5-6 cümle. Samimi ama ciddi, gerektiğinde fikrini söyle.
- Neye baktığını bir cümleyle belirt (ör. "Sağlık raporuna baktım")."""


def lines(path):
	return [json.loads(l) for l in open(path) if l.strip()]


def run(cmd, **kw):
	return subprocess.run(cmd, capture_output=True, text=True, **kw)


def swap_used_mb():
	m = re.search(r"used = ([\d.]+)M", run(["sysctl", "-n", "vm.swapusage"]).stdout)
	return float(m.group(1)) if m else None


def footprint_mb(pid):
	"""The process's own memory, Metal buffers included; the weights are a mapped file and not in it."""
	m = re.search(r"Footprint: ([\d.]+) (MB|GB)", run(["footprint", "-p", str(pid)]).stdout)
	return round(float(m.group(1)) * (1024 if m.group(2) == "GB" else 1)) if m else None


def fm_status_s(times=3):
	took = []
	for _ in range(times):
		t0 = time.perf_counter()
		run(["fm", "status"])
		took.append(time.perf_counter() - t0)
	return round(statistics.median(took), 2)


def chat(server, messages, max_tokens, schema=None):
	"""Plain text, or JSON held to `schema`; returns (text, seconds)."""
	if schema:
		t0 = time.perf_counter()
		out = server.chat(messages, schema, max_tokens)
		return out, time.perf_counter() - t0
	import urllib.request
	body = {"messages": messages, "temperature": 0, "seed": 1, "max_tokens": max_tokens,
		"chat_template_kwargs": {"enable_thinking": False}}
	req = urllib.request.Request(server.url + "/v1/chat/completions", json.dumps(body).encode(), {"Content-Type": "application/json"})
	t0 = time.perf_counter()
	with urllib.request.urlopen(req, timeout=600) as r:
		out = json.load(r)["choices"][0]["message"]["content"]
	return out, time.perf_counter() - t0


NAME = re.compile(r"(?<![\w'’])([A-ZÇĞİÖŞÜ][\wçğıöşüÇĞİÖŞÜ]+)")
SENTENCE_START = re.compile(r"(^|[.!?:\n]\s*|[-*•]\s+)$")


def unknown_names(answer, data):
	"""Capitalised words not at a sentence start that the snapshot does not contain."""
	low = data.lower()
	out = []
	for m in NAME.finditer(answer):
		w = m.group(1)
		if SENTENCE_START.search(answer[:m.start()]) or w in ("Mahmut", "Football", "Manager", "FM"):
			continue
		if w.lower() not in low and w.lower().replace("i̇", "i") not in low and w not in out:
			out.append(w)
	return out


def p90(xs):
	xs = sorted(xs)
	return round(xs[min(len(xs) - 1, int(0.9 * len(xs)))], 2) if xs else None


def conversation(server, snapshot):
	rows = []
	for c in lines(BENCH / "conversation.jsonl"):
		data = "\n\n".join(f"### fm {r}\n" + (snapshot / f"{r}.txt").read_text() for r in c["reads"])
		messages = [{"role": "system", "content": MAHMUT + "\n\nOyun verisi:\n" + data}, {"role": "user", "content": c["q"]}]
		answer, took = chat(server, messages, 400)
		rows.append({"id": c["id"], "q": c["q"], "answer": answer.strip(), "s": round(took, 2), "unknown_names": unknown_names(answer, data)})
		print(f"  {c['id']} {took:5.1f}s {rows[-1]['unknown_names'] or ''}", file=sys.stderr)
	return rows


def memory(server, tools):
	cases = lines(BENCH / "memory.jsonl")
	feed = "".join(json.dumps({"text": c["turns"][-1]["user"]}, ensure_ascii=False) + "\n" for c in cases)
	p = run([str(llm.FM), "__route", "--model", str(Path(llm.CANDIDATES_MODEL).expanduser()), "--cases", str(ROOT / "tests/nl")], input=feed, cwd=ROOT)
	routed = [json.loads(l) for l in p.stdout.splitlines() if l.strip()]
	rows = []
	for c, r in zip(cases, routed):
		*before, last = c["turns"]
		asks = r.get("asks") or [llm.flatten(v["call"] for v in r.get("votes", []))]
		names = [n for n in llm.flatten(asks) if n in tools]
		# the earlier turns' tools are candidates too: "onu" may mean doing more of the same
		names += [t["call"] for t in before if t["call"] not in names]
		cards = "\n".join([llm.tool_card(n, tools[n], 200) for n in names] + [llm.NONE_CARD])
		messages = [{"role": "system", "content": llm.SYSTEM + "\n- Words like 'him', 'onu', 'o' refer to the player of the earlier turns."}]
		for t in before:
			messages += [{"role": "user", "content": f"Player: {t['user']}"},
				{"role": "assistant", "content": json.dumps({"steps": [{"tool": t["call"], "args": t["args"]}]}, ensure_ascii=False)}]
		messages.append({"role": "user", "content": f"Tools:\n{cards}\n\nPlayer: {last['user']}"})
		raw, took = chat(server, messages, 256, llm.plan_schema(names, tools, 2))
		try:
			steps = [st for st in json.loads(raw)["steps"] if st["tool"] != llm.NONE]
		except (json.JSONDecodeError, KeyError, TypeError):
			steps = []
		for st in steps:  # a flag set to false is the same as one left out (tools/nl-llm.py)
			st["args"] = {k: v for k, v in st["args"].items() if v is not False}
		ok = len(steps) == 1 and steps[0]["tool"] == last["call"] and score.same_args(last["args"], steps[0]["args"])
		rows.append({"id": c["id"], "ok": ok, "want": {"call": last["call"], "args": last["args"]}, "got": steps, "s": round(took, 2)})
		print(f"  {c['id']} {'ok ' if ok else 'BAD'} {took:4.1f}s {json.dumps(steps, ensure_ascii=False)}", file=sys.stderr)
	return rows


def sha(path, block=1 << 20):
	h = hashlib.sha256()
	with open(path, "rb") as f:
		for chunk in iter(lambda: f.read(block), b""):
			h.update(chunk)
	return h.hexdigest()


def main():
	ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	ap.add_argument("--gguf", required=True)
	ap.add_argument("--snapshot", required=True, help="folder of saved `fm` outputs (<command>.txt)")
	ap.add_argument("--url", default="http://127.0.0.1:8089")
	ap.add_argument("--plan-log", help="tools/nl-llm.py --log file of this model's planning run")
	ap.add_argument("--only", choices=["conversation", "memory"])
	ap.add_argument("--save", metavar="NAME")
	a = ap.parse_args()
	gguf = Path(a.gguf).expanduser().resolve()
	snapshot = Path(a.snapshot).expanduser()
	tools = llm.load_tools()

	cost = {"fm_status_s_before": fm_status_s(), "swap_mb_before": swap_used_mb()}
	t0 = time.perf_counter()
	server = llm.Server(a.url, str(gguf), 8192, 4)
	cost["load_s"] = round(time.perf_counter() - t0, 1)
	try:
		# fm status while the model is busy
		busy = threading.Thread(target=chat, args=(server, [{"role": "user", "content": "Bir futbol takımına uzun bir moral konuşması yaz."}], 600))
		busy.start()
		time.sleep(0.5)
		cost["fm_status_s_during"] = fm_status_s()
		busy.join()
		result = {}
		if a.only in (None, "memory"):
			print("memory", file=sys.stderr)
			result["memory"] = memory(server, tools)
		if a.only in (None, "conversation"):
			print("conversation", file=sys.stderr)
			result["conversation"] = conversation(server, snapshot)
		cost["footprint_mb"] = footprint_mb(server.proc.pid)
		cost["ram_mb"] = cost["footprint_mb"] + round(gguf.stat().st_size / 2**20) if cost["footprint_mb"] else None
		cost["swap_mb_after"] = swap_used_mb()
	finally:
		server.close()

	if "conversation" in result:
		ts = [r["s"] for r in result["conversation"]]
		cost["answer_s_median"], cost["answer_s_p90"] = round(statistics.median(ts), 2), p90(ts)
	if a.plan_log:
		ms = [json.loads(l)["ms"] / 1000 for l in open(a.plan_log) if l.strip()]
		cost["plan_s_median"], cost["plan_s_p90"], cost["plan_n"] = round(statistics.median(ms), 2), p90(ms), len(ms)
	if cost["swap_mb_before"] is not None and cost["swap_mb_after"] is not None:
		cost["swap_growth_mb"] = round(cost["swap_mb_after"] - cost["swap_mb_before"])
	summary = {"cost": cost}
	if "memory" in result:
		summary["memory"] = f"{sum(r['ok'] for r in result['memory'])}/{len(result['memory'])}"
	if "conversation" in result:
		summary["unknown_names"] = sum(len(r["unknown_names"]) for r in result["conversation"])
	print(json.dumps(summary, indent=1))

	if a.save:
		runner = run(["llama-server", "--version"])
		manifest = {
			"when": datetime.now().astimezone().isoformat(timespec="seconds"),
			"gguf": {"name": gguf.name, "sha256": sha(gguf)},
			"runner": (runner.stdout + runner.stderr).strip().splitlines()[:1],
			"snapshot": {"dir": snapshot.name, "sha256": score.sha256(sorted(snapshot.glob("*.txt")), named=False)},
			"bench": score.sha256(sorted(BENCH.glob("*.jsonl")), named=False),
			"commit": score.git("rev-parse", "--short", "HEAD"),
			"dirty": bool(score.git("status", "--porcelain", "--", "tools", "tests/llm")),
			"machine": {"cpu": run(["sysctl", "-n", "machdep.cpu.brand_string"]).stdout.strip(), "mem_gb": round(int(run(["sysctl", "-n", "hw.memsize"]).stdout) / 2**30)},
		}
		out = BENCH / "results" / f"{a.save}.json"
		out.parent.mkdir(exist_ok=True)
		out.write_text(json.dumps({"manifest": manifest, "summary": summary, **result}, ensure_ascii=False, indent=1) + "\n")
		print(f"saved {out.relative_to(ROOT)}", file=sys.stderr)


if __name__ == "__main__":
	main()
