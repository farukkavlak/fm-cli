#!/usr/bin/env python3
"""A local LLM as the planner of a sentence: a router for tools/nl-score.py (docs/LLM-EXPERIMENT.md).

fm __route --model gives each ask its candidate tools; the LLM picks the steps and their arguments
from those candidates only. The answer is held to a JSON schema built from the candidates' MCP
schemas, so tool and argument names are always real. As in the command router, our code and not the
model decides what is asked: a required argument the plan leaves out is asked (Rules.cs tables).

Reads {"text": ...} per line, writes {"call", "args", "ask", "votes", "asks"} per line. A line with
"tool" (nl-score.py --given-tool) gets only that tool as candidate, answered as {"forced": ...}.

  tools/nl-llm.py --gguf ~/.cache/fm-cli/llm/qwen3.5-0.8b-q8_0.gguf     # starts llama-server itself
  tools/nl-llm.py --url http://127.0.0.1:8088                            # one already running
  tools/nl-score.py --router "tools/nl-llm.py --gguf FILE" --set base --only held --save NAME
"""
import argparse
import json
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FM = ROOT / "cli/fm/bin/Debug/net10.0/fm"
TOOLS_CS = ROOT / "cli/fm/Core/Mcp.Tools.cs"
RULES_CS = ROOT / "cli/fm/Nl/Rules.cs"
CANDIDATES_MODEL = "~/.cache/fm-cli/nl/mpnet-f-q"  # the measurement model: same candidates as tests/nl/results
CONFIRM = {"yes", "confirm"}  # added by the layer after a yes, never by the planner

SYSTEM = """You turn what a Football Manager player types (Turkish or English, often with typos and no Turkish letters) into tool calls.
- Use only the tools listed. One step per request, in the order the player said them.
- Arguments: only what the sentence says. Copy names as written (a Turkish ending like 'yi', 'nin', 'a' is not part of the name). Leave out what it does not say; never guess a value.
- If none of the tools does what the player asks, or it is just chat, the one step is `none`.
- Answer with one line of JSON, no spaces or line breaks."""


def load_tools():
	src = TOOLS_CS.read_text()
	tools = {}
	for m in re.finditer(r'new\("(fm_\w+)", "((?:[^"\\]|\\.)*)",\s*(None|"""(.*?)""")', src, re.S):
		name, desc, _, schema = m.groups()
		tools[name] = {"desc": desc.replace('\\"', '"'), "schema": json.loads(schema) if schema else {"properties": {}}}
	return tools


def load_needs():
	"""Rules.Required (tool → arguments it cannot run without) and Rules.Needs (with the first given and
	the second not, the third is asked)."""
	src = RULES_CS.read_text()
	block = re.search(r"Dictionary<string, string\[\]> Required = new\(\)\s*\{(.*?)\};", src, re.S).group(1)
	required = {t: re.findall(r'"(\w+)"', args) for t, args in re.findall(r'\["(fm_\w+)"\] = \[([^\]]*)\]', block)}
	block = re.search(r"\)\[\] Needs =\s*\[(.*?)\];", src, re.S).group(1)
	needs = [(t, w, None if u == "null" else u.strip('"'), n) for t, w, u, n in
		re.findall(r'\("(fm_\w+)", "(\w+)", (null|"\w+"), "(\w+)"\)', block)]
	return required, needs


def short(desc, limit):
	"""The description up to the end of the sentence that passes `limit` characters."""
	if len(desc) <= limit:
		return desc
	cut = desc.find(". ", limit)
	return desc if cut < 0 else desc[:cut + 1]


def load_examples():
	"""Per tool, from the training-only sentences (never scored): the one with the most arguments and
	one with none, to show how arguments are written and that unsaid ones are left out."""
	per = {}
	for f in sorted((ROOT / "tests/nl/train").glob("*.jsonl")):
		for l in open(f):
			c = json.loads(l)
			if isinstance(c.get("call"), str) and not c.get("ask"):
				per.setdefault(c["call"], []).append((c.get("tr") or c.get("en"), c.get("args") or {}))
	out = {}
	for tool, cs in per.items():
		most = max(cs, key=lambda c: len(c[1]))
		bare = next((c for c in cs if not c[1]), None)
		out[tool] = [c for c in (most, bare) if c] if most[1] else [bare]
	return out


def tool_card(name, tool, limit, examples=()):
	props = tool["schema"].get("properties", {})
	args = []
	for k, p in props.items():
		if k in CONFIRM:
			continue
		kind = p.get("type", "string")
		if "enum" in p:
			kind = "|".join(p["enum"])
		args.append(f"{k}: {kind}")
	eg = "".join(f"\n    e.g. \"{t}\" → {json.dumps(a, ensure_ascii=False)}" for t, a in examples)
	return f"- {name}({', '.join(args)}): {short(tool['desc'], limit)}{eg}"


def step_schema(name, tool):
	props = {k: ({**v, "minLength": 1} if v.get("type") == "string" else v) for k, v in tool["schema"].get("properties", {}).items() if k not in CONFIRM}
	return {"type": "object", "properties": {
		"tool": {"const": name},
		"args": {"type": "object", "properties": props, "additionalProperties": False},
	}, "required": ["tool", "args"], "additionalProperties": False}


NONE = "none"
NONE_CARD = f"- {NONE}(): none of these tools does it; chat, real life, or out of the game."


NONE_STEP = {"type": "object", "properties": {"tool": {"const": NONE}, "args": {"type": "object", "maxProperties": 0}},
	"required": ["tool", "args"], "additionalProperties": False}


def plan_schema(names, tools, max_steps, none=True, per_ask=None):
	"""At least one step: an empty plan is the easy way out for a small model, so "nothing" is a choice
	of its own. With per_ask (the router split the sentence), one step per ask from its own candidates."""
	if per_ask:
		steps = {"type": "array", "minItems": len(per_ask), "maxItems": len(per_ask),
			"prefixItems": [{"anyOf": [step_schema(n, tools[n]) for n in ask] + [NONE_STEP]} for ask in per_ask]}
	else:
		items = [step_schema(n, tools[n]) for n in names] + ([NONE_STEP] if none else [])
		steps = {"type": "array", "minItems": 1, "maxItems": max_steps, "items": {"anyOf": items}}
	return {"type": "object", "properties": {
		"steps": steps,
	}, "required": ["steps"], "additionalProperties": False}


def flatten(calls):
	out = []
	for c in calls:
		for t in (c if isinstance(c, list) else [c]):
			if t and t not in out:
				out.append(t)
	return out


def asks_of(step, required, needs):
	args = step["args"]
	ask = [a for a in required.get(step["tool"], []) if a not in args]
	for t, when, unless, need in needs:
		if t == step["tool"] and when in args and (unless is None or unless not in args) and need not in args:
			ask.append(need)
	return ask


class Server:
	def __init__(self, url, gguf, ctx, threads):
		self.url, self.proc = url, None
		if gguf:
			exe = shutil.which("llama-server") or sys.exit("llama-server not found (brew install llama.cpp)")
			port = re.search(r":(\d+)", url).group(1)
			self.proc = subprocess.Popen([exe, "-m", str(Path(gguf).expanduser()), "--port", port, "-c", str(ctx),
				"-ngl", "99", "--seed", "1", "-t", str(threads), "-np", "1", "--cache-ram", "0", "--reasoning-budget", "0", "--log-disable"],
				stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
			self.wait_ready()

	def wait_ready(self, limit=180):
		end = time.time() + limit
		while time.time() < end:
			if self.proc and self.proc.poll() is not None:
				sys.exit(f"llama-server stopped ({self.proc.returncode})")
			try:
				with urllib.request.urlopen(self.url + "/health", timeout=2) as r:
					if json.load(r).get("status") == "ok":
						return
			except Exception:
				pass
			time.sleep(0.5)
		sys.exit("llama-server did not get ready")

	def chat(self, messages, schema, max_tokens):
		body = {"messages": messages, "temperature": 0, "seed": 1, "max_tokens": max_tokens,
			"response_format": {"type": "json_schema", "json_schema": {"name": "plan", "schema": schema}},
			"chat_template_kwargs": {"enable_thinking": False}}
		req = urllib.request.Request(self.url + "/v1/chat/completions", json.dumps(body).encode(), {"Content-Type": "application/json"})
		with urllib.request.urlopen(req, timeout=300) as r:
			out = json.load(r)
		self.timings = out.get("timings", {})
		return out["choices"][0]["message"]["content"]

	def close(self):
		if self.proc:
			self.proc.terminate()
			self.proc.wait()


def main():
	ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	ap.add_argument("--gguf", help="start llama-server with this model file (stopped at the end)")
	ap.add_argument("--url", default="http://127.0.0.1:8088")
	ap.add_argument("--ctx", type=int, default=4096)
	ap.add_argument("--threads", type=int, default=4)
	ap.add_argument("--desc", type=int, default=200, help="tool description length shown to the model (characters, to the end of a sentence)")
	ap.add_argument("--candidates", default=CANDIDATES_MODEL, help="fm __route --model for the candidate tools")
	ap.add_argument("--log", help="append each prompt, answer and time here (JSON lines)")
	a = ap.parse_args()

	lines = [json.loads(l) for l in sys.stdin if l.strip()]
	tools = load_tools()
	required, needs = load_needs()
	examples = load_examples()
	feed = "".join(json.dumps({"text": l["text"]}, ensure_ascii=False) + "\n" for l in lines)
	p = subprocess.run([str(FM), "__route", "--model", str(Path(a.candidates).expanduser()), "--cases", str(ROOT / "tests/nl")],
		input=feed, capture_output=True, text=True, cwd=ROOT)
	if p.returncode != 0:
		sys.exit(f"fm __route failed:\n{p.stderr[-2000:]}")
	routed = [json.loads(l) for l in p.stdout.splitlines() if l.strip()]

	server = Server(a.url, a.gguf, a.ctx, a.threads)
	log = open(a.log, "a") if a.log else None
	try:
		for line, r in zip(lines, routed):
			given = line.get("tool")
			asks = r.get("asks") or [flatten(v["call"] for v in r.get("votes", []))]
			names = [given] if given else [n for n in flatten(asks) if n in tools]
			per_ask = None if given or not r.get("ask_texts") else [[n for n in ask if n in tools] for ask in asks]
			cards = "\n".join([tool_card(n, tools[n], a.desc, examples.get(n, ())) for n in names] + ([] if given else [NONE_CARD]))
			said = f"Player: {line['text']}"
			if per_ask:
				said += "\nRequests, one step each:" + "".join(f"\n{i + 1}. \"{t}\" ({', '.join(ask)})" for i, (t, ask) in enumerate(zip(r["ask_texts"], per_ask)))
			messages = [{"role": "system", "content": SYSTEM}, {"role": "user", "content": f"Tools:\n{cards}\n\n{said}"}]
			t0 = time.perf_counter()
			server.timings = {}
			if not names:  # the router has no tool for it: nothing to plan
				raw = '{"steps": []}'
			else:
				try:
					raw = server.chat(messages, plan_schema(names, tools, 1 if given else 2, none=not given, per_ask=per_ask), 256)
				except urllib.error.HTTPError as e:
					raw = f"error {e.code}: {e.read()[:300]!r}"
					print(f"nl-llm: {line['text']!r}: {raw}", file=sys.stderr)
			took = time.perf_counter() - t0
			try:
				steps = [s for s in json.loads(raw)["steps"] if s["tool"] != NONE]
			except (json.JSONDecodeError, KeyError, TypeError):
				steps = []
			if given and not steps:
				steps = [{"tool": given, "args": {}}]
			calls = [s["tool"] for s in steps]
			# a flag set to false is the same as one left out
			argss = [{k: v for k, v in s["args"].items() if v is not False} for s in steps]
			ask = [x for s, args in zip(steps, argss) for x in asks_of({"tool": s["tool"], "args": args}, required, needs)]
			if not calls:
				out = {"call": None}
			elif len(calls) == 1:
				out = {"call": calls[0], "args": argss[0], "ask": ask}
			else:
				out = {"call": calls, "args": argss, "ask": ask}
			if log:
				t = server.timings
				log.write(json.dumps({"text": line["text"], "tools": names, "raw": raw, "ms": round(1000 * took),
					"prompt_n": t.get("prompt_n"), "prompt_ms": round(t.get("prompt_ms", 0)), "out_n": t.get("predicted_n"), "out_ms": round(t.get("predicted_ms", 0))}, ensure_ascii=False) + "\n")
				log.flush()
			if given:
				out = {"forced": out}
			else:
				out.update({k: r[k] for k in ("votes", "asks") if k in r})
			print(json.dumps(out, ensure_ascii=False), flush=True)
	finally:
		server.close()
		if log:
			log.close()


if __name__ == "__main__":
	main()
