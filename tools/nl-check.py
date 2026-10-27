#!/usr/bin/env python3
"""Checks the natural-language test set (tests/nl/*.jsonl) against the MCP tool list.

Every case must name real tools with real arguments: names from the tool's schema, enum values
from its list, required arguments present (unless the case says the router must ask for them).
Prints counts per file, language and tool, and the tools with no case. Exit 1 on any error.

  tools/nl-check.py            # check and summarise
  tools/nl-check.py --quiet    # errors only
"""
import json
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TOOLS_CS = ROOT / "cli/fm/Core/Mcp.Tools.cs"
CASES = ROOT / "tests/nl"
# Never expected in a case: the layer adds them after the user confirms (see tests/nl/README.md).
CONFIRM = {"yes", "confirm"}


def load_tools():
	src = TOOLS_CS.read_text()
	tools = {}
	for m in re.finditer(r'new\("(fm_\w+)", "(?:[^"\\]|\\.)*",\s*(None|"""(.*?)""")', src, re.S):
		name, _, schema = m.groups()
		tools[name] = json.loads(schema) if schema else {"properties": {}}
	return tools


def check_call(tools, tool, args, ask, where, errors):
	if tool not in tools:
		errors.append(f"{where}: unknown tool {tool}")
		return
	schema = tools[tool]
	props = schema.get("properties", {})
	if not isinstance(args, dict):
		errors.append(f"{where}: args of {tool} must be an object")
		return
	for k, v in args.items():
		if k not in props:
			errors.append(f"{where}: {tool} has no argument {k!r} (has {', '.join(props) or 'none'})")
			continue
		if k in CONFIRM or (tool == "fm_offer" and k == "send"):
			errors.append(f"{where}: {k!r} is added by the layer after confirmation, not expected")
		p = props[k]
		kind = p.get("type")
		if "enum" in p and v not in p["enum"]:
			errors.append(f"{where}: {tool}.{k}={v!r} not in {p['enum']}")
		elif kind == "boolean" and not isinstance(v, bool):
			errors.append(f"{where}: {tool}.{k} must be true/false")
		elif kind == "integer" and not isinstance(v, int):
			errors.append(f"{where}: {tool}.{k} must be a number")
		elif kind == "string" and not isinstance(v, str):
			errors.append(f"{where}: {tool}.{k} must be text")
		elif kind == "array" and not (isinstance(v, list) and all(isinstance(x, str if p["items"].get("type") == "string" else int) for x in v)):
			errors.append(f"{where}: {tool}.{k} must be a list")
	asked = set(ask if isinstance(ask, list) else [ask]) if ask else set()
	for r in schema.get("required", []):
		if r not in args and r not in asked and r not in CONFIRM:
			errors.append(f"{where}: {tool} needs {r!r} (or \"ask\": \"{r}\")")


def check_entry(tools, entry, where, errors, per_tool):
	"""One expected answer: a call, several calls in order, or none. Returns its kind."""
	call, args = entry.get("call"), entry.get("args", {})
	count = per_tool.update if per_tool is not None else (lambda _: None)
	if call is None:
		count(["(none)"])
		return "none"
	if isinstance(call, list):
		args = args if isinstance(args, list) else [{}] * len(call)
		if len(args) != len(call):
			errors.append(f"{where}: {len(call)} calls but {len(args)} args")
			return "multi"
		for t, a in zip(call, args):
			check_call(tools, t, a, None, where, errors)
		count(call)
		return "multi"
	check_call(tools, call, args, entry.get("ask"), where, errors)
	count([call])
	return "ask" if "ask" in entry else "single"


def main():
	quiet = "--quiet" in sys.argv
	tools = load_tools()
	errors, per_file, per_lang, per_tool, texts = [], Counter(), Counter(), Counter(), Counter()
	kinds = Counter()
	for path in sorted(CASES.glob("*.jsonl")) + sorted((CASES / "train").glob("*.jsonl")):
		for n, line in enumerate(path.read_text().splitlines(), 1):
			if not line.strip():
				continue
			where = f"{path.name}:{n}"
			try:
				case = json.loads(line)
			except json.JSONDecodeError as e:
				errors.append(f"{where}: not JSON ({e})")
				continue
			langs = [k for k in ("tr", "en") if k in case]
			if len(langs) != 1:
				errors.append(f"{where}: needs exactly one of \"tr\" / \"en\"")
				continue
			unknown = set(case) - {"tr", "en", "call", "args", "or", "ask"}
			if unknown:
				errors.append(f"{where}: unknown keys {sorted(unknown)}")
			text = case[langs[0]].strip().lower()
			texts[text] += 1
			if texts[text] == 2:
				errors.append(f"{where}: repeated sentence {case[langs[0]]!r}")
			per_file[path.name] += 1
			per_lang[langs[0]] += 1
			if "call" not in case:
				errors.append(f"{where}: no \"call\"")
				continue
			if path.parent.name == "train":
				# training-only: the tool is all the model learns from it
				if case["call"] is not None and case["call"] not in tools:
					errors.append(f"{where}: unknown tool {case['call']}")
				continue
			kind = check_entry(tools, case, where, errors, per_tool)
			kinds[kind] += 1
			for alt in case.get("or", []):
				check_entry(tools, alt, where + " (or)", errors, None)
	for e in errors:
		print(e)
	if not quiet:
		total = sum(per_file.values())
		print(f"\n{total} cases — " + ", ".join(f"{k} {v}" for k, v in per_lang.items()) + " — " + ", ".join(f"{k} {v}" for k, v in kinds.items()))
		for f, c in sorted(per_file.items()):
			print(f"  {f:22} {c}")
		missing = sorted(set(tools) - set(per_tool))
		thin = sorted(t for t in tools if 0 < per_tool[t] < 4)
		print(f"tools with no case ({len(missing)}): {' '.join(missing) or '-'}")
		print(f"tools with under 4 cases ({len(thin)}): {' '.join(thin) or '-'}")
	sys.exit(1 if errors else 0)


if __name__ == "__main__":
	main()
