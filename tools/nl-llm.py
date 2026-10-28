#!/usr/bin/env python3
"""An LLM as the planner of a sentence: a router for tools/nl-score.py (docs/LLM-EXPERIMENT.md).

fm __route --model gives each ask its candidate tools; the LLM picks the steps and their arguments
from those candidates only. The answer is held to a JSON schema built from the candidates' MCP
schemas, so tool and argument names are always real. As in the command router, our code and not the
model decides what is asked: a required argument the plan leaves out is asked (Rules.cs tables).

Reads {"text": ...} per line, writes {"call", "args", "ask", "votes", "asks"} per line. A line with
"tool" (nl-score.py --given-tool) gets only that tool as candidate, answered as {"forced": ...}.

  tools/nl-llm.py --gguf ~/.cache/fm-cli/llm/qwen3.5-0.8b-q8_0.gguf     # starts llama-server itself
  tools/nl-llm.py --url http://127.0.0.1:8088                            # one already running
  tools/nl-llm.py --remote claude-haiku-5-5 --workers 4                  # OpenCode Go (OPENCODE_API_KEY, from .env)
  tools/nl-score.py --router "tools/nl-llm.py --gguf FILE" --set base --only held --save NAME
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
import uuid
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FM = ROOT / "cli/fm/bin/Debug/net10.0/fm"
TOOLS_CS = ROOT / "cli/fm/Core/Mcp.Tools.cs"
RULES_CS = ROOT / "cli/fm/Nl/Rules.cs"
CANDIDATES_MODEL = "~/.cache/fm-cli/nl/mpnet-f-q"  # the measurement model: same candidates as tests/nl/results
CONFIRM = {"yes", "confirm"}  # added by the layer after a yes, never by the planner
REMOTE_URL = "https://opencode.ai/zen/go"  # OpenCode Go, OpenAI-style chat completions

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


def plan_schema(names, tools, max_steps, none=True, per_ask=None, flat=False):
	"""At least one step: an empty plan is the easy way out for a small model, so "nothing" is a choice
	of its own. With per_ask (the router split the sentence), one step per ask from its own candidates."""
	if per_ask and not flat:
		steps = {"type": "array", "minItems": len(per_ask), "maxItems": len(per_ask),
			"prefixItems": [{"anyOf": [step_schema(n, tools[n]) for n in ask] + [NONE_STEP]} for ask in per_ask]}
	elif per_ask:  # remote providers do not all pass prefixItems on: one list, the count fixed
		items = [step_schema(n, tools[n]) for n in dict.fromkeys(t for ask in per_ask for t in ask)] + [NONE_STEP]
		steps = {"type": "array", "minItems": len(per_ask), "maxItems": len(per_ask), "items": {"anyOf": items}}
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
	def __init__(self, url, gguf, ctx, threads, remote=None):
		self.url, self.proc, self.remote = url, None, remote
		self.headers = {"Content-Type": "application/json"}
		if remote:
			self.url = REMOTE_URL
			key = os.environ.get("OPENCODE_API_KEY") or env_file().get("OPENCODE_API_KEY") or sys.exit("OPENCODE_API_KEY not set (.env)")
			# one session per run, as OpenCode asks; named honestly
			self.headers.update({"Authorization": f"Bearer {key}", "x-opencode-session": str(uuid.uuid4()), "User-Agent": "fm-cli-bench/1.0"})
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
		"""The answer's text, held to `schema` when given; (text, timings)."""
		body = {"messages": messages, "temperature": 0, "max_tokens": max_tokens}
		if schema:
			body["response_format"] = {"type": "json_schema", "json_schema": {"name": "plan", "schema": schema}}
		if self.remote:
			body["model"] = self.remote
		else:
			body.update({"seed": 1, "chat_template_kwargs": {"enable_thinking": False}})
		req = urllib.request.Request(self.url + "/v1/chat/completions", json.dumps(body).encode(), self.headers)
		dropped = False
		for attempt in range(5):
			try:
				with urllib.request.urlopen(req, timeout=300) as r:
					out = json.load(r)
				break
			except urllib.error.HTTPError as e:
				# a limit or an outage stops the run: counting it as "none" would score the network, not the model
				if e.code in (408, 429, 500, 502, 503, 504) and attempt < 4:
					time.sleep(10 * (attempt + 1))
					continue
				detail = e.read()[:300]
				if e.code == 400 and "response_format" in body:
					# a provider may refuse a big schema (Anthropic: at most 24 optional fields); then the
					# prompt alone asks for JSON and parse_steps checks it
					body.pop("response_format")
					req = urllib.request.Request(self.url + "/v1/chat/completions", json.dumps(body).encode(), self.headers)
					dropped = True
					continue
				if e.code == 400:
					print(f"nl-llm: HTTP 400: {detail!r}", file=sys.stderr)
					raise
				sys.exit(f"nl-llm: HTTP {e.code}: {detail!r}")
			except (urllib.error.URLError, TimeoutError) as e:
				if attempt == 4:
					sys.exit(f"nl-llm: {e}")
				time.sleep(10 * (attempt + 1))
		timings = out.get("timings", {})
		if self.remote:
			timings = {"finish": out["choices"][0].get("finish_reason"), "schema_dropped": dropped, **{k: v for k, v in (out.get("usage") or {}).items() if isinstance(v, int)}}
		return out["choices"][0]["message"]["content"] or "", timings

	def close(self):
		if self.proc:
			self.proc.terminate()
			self.proc.wait()


def env_file():
	f = ROOT / ".env"
	return dict(l.strip().split("=", 1) for l in open(f) if "=" in l and not l.startswith("#")) if f.exists() else {}


def parse_steps(raw, names, tools):
	"""The plan's steps, checked by our code: a remote model may wrap the JSON or ignore the schema, so
	only listed tools and their own argument names count."""
	m = re.search(r"\{.*\}", raw, re.S)
	try:
		steps = json.loads(m.group(0))["steps"] if m else []
	except (json.JSONDecodeError, KeyError, TypeError):
		return []
	out = []
	for st in steps if isinstance(steps, list) else []:
		if not isinstance(st, dict) or st.get("tool") not in names or not isinstance(st.get("args", {}), dict):
			continue
		props = tools[st["tool"]]["schema"].get("properties", {})
		out.append({"tool": st["tool"], "args": {k: v for k, v in st.get("args", {}).items() if k in props and k not in CONFIRM}})
	return out


def main():
	ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	ap.add_argument("--gguf", help="start llama-server with this model file (stopped at the end)")
	ap.add_argument("--url", default="http://127.0.0.1:8088")
	ap.add_argument("--ctx", type=int, default=4096)
	ap.add_argument("--threads", type=int, default=4)
	ap.add_argument("--desc", type=int, default=200, help="tool description length shown to the model (characters, to the end of a sentence)")
	ap.add_argument("--candidates", default=CANDIDATES_MODEL, help="fm __route --model for the candidate tools")
	ap.add_argument("--log", help="append each prompt, answer and time here (JSON lines)")
	ap.add_argument("--remote", metavar="MODEL", help="an OpenCode Go model instead of a local one")
	ap.add_argument("--workers", type=int, default=4, help="requests at a time to a remote model")
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

	server = Server(a.url, a.gguf, a.ctx, a.threads, a.remote)

	def plan(line, r):
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
		timings = {}
		if not names:  # the router has no tool for it: nothing to plan
			raw = '{"steps": []}'
		else:
			try:
				raw, timings = server.chat(messages, plan_schema(names, tools, 1 if given else 2, none=not given, per_ask=per_ask, flat=bool(a.remote)), 2048 if a.remote else 256)
			except urllib.error.HTTPError as e:
				raw = f"error {e.code}: {e.read()[:300]!r}"
				print(f"nl-llm: {line['text']!r}: {raw}", file=sys.stderr)
		took = time.perf_counter() - t0
		steps = parse_steps(raw, names, tools)
		if given and not steps:
			steps = [{"tool": given, "args": {}}]
		calls = [st["tool"] for st in steps]
		# a flag set to false is the same as one left out
		argss = [{k: v for k, v in st["args"].items() if v is not False} for st in steps]
		ask = [x for st, args in zip(steps, argss) for x in asks_of({"tool": st["tool"], "args": args}, required, needs)]
		if not calls:
			out = {"call": None}
		elif len(calls) == 1:
			out = {"call": calls[0], "args": argss[0], "ask": ask}
		else:
			out = {"call": calls, "args": argss, "ask": ask}
		rec = {"text": line["text"], "tools": names, "raw": raw, "ms": round(1000 * took), **({"remote": timings} if a.remote else {}),
			"prompt_n": timings.get("prompt_n"), "prompt_ms": round(timings.get("prompt_ms", 0)), "out_n": timings.get("predicted_n"), "out_ms": round(timings.get("predicted_ms", 0))}
		if given:
			out = {"forced": out}
		else:
			out.update({k: r[k] for k in ("votes", "asks") if k in r})
		return out, rec

	log = open(a.log, "a") if a.log else None
	try:
		# answers come back in order; a local server takes one request at a time
		with ThreadPoolExecutor(a.workers if a.remote else 1) as pool:
			for out, rec in pool.map(plan, lines, routed):
				if log:
					log.write(json.dumps(rec, ensure_ascii=False) + "\n")
					log.flush()
				print(json.dumps(out, ensure_ascii=False), flush=True)
	finally:
		server.close()
		if log:
			log.close()


if __name__ == "__main__":
	main()
