#!/usr/bin/env python3
"""Writes noisy variants of the natural-language test set (tests/nl/*.jsonl).

Each case gets a few variants the way people really type: Turkish letters folded (ş→s, ı→i),
a letter dropped, doubled, swapped or hit on the key next to it, filler words ("abi", "pls"),
odd capitals, and another player's name in place of the one in the sentence (the expected
arguments change with it). Names and other argument values that appear in the sentence are
never misspelt, so the expected call stays exact. Same seed, same output.

  tools/nl-expand.py                 # 6 variants per case → tests/nl/expanded/all.jsonl
  tools/nl-expand.py -n 10 --seed 2  # more, another draw
  tools/nl-expand.py --stdout | head
"""
import argparse
import json
import random
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "tests/nl"
OUT = CASES / "expanded/all.jsonl"

FOLD = str.maketrans("şŞıİğĞüÜöÖçÇ", "sSiIgGuUoOcC")
NEAR = {c: n for c, n in zip("qwertyuiopasdfghjklzxcvbnm", [
	"wa", "qes", "wrd", "etf", "ryg", "tuh", "yij", "uok", "ipl", "o", "qsz", "awdx", "sefc", "drgv", "fthb",
	"gyjn", "hukm", "jil", "kp", "xs", "zcd", "xvf", "cbg", "vnh", "bmj", "nk"])}
FILLER = {
	"tr": {"pre": ["abi ", "kanka ", "ya ", "hadi ", "şu ", "bi ", "lan ", "hocam ", "bak "], "post": [" abi", " ya", " lan", " pls", " bakalım", " hemen", " kanka", "?", "!!", " :)"]},
	"en": {"pre": ["hey ", "yo ", "ok ", "pls ", "can you ", "bro ", "quick, "], "post": [" pls", " please", " mate", " bro", " rn", "?", "!!", " thx"]},
}
# Our squad and other clubs' players, kept apart so a swap stays the same kind of person.
OURS = ["rinaldi", "ghiglione", "pieragnolo", "tosto", "fazzini", "esposito", "gyasi", "colombo", "maleh", "marin",
	"de luca", "ferrari", "romano", "ricci", "bruno", "galli", "conti", "mancini", "lombardi", "moretti", "kerem", "musaba"]
OTHERS = ["kenan yıldız", "arda güler", "semih kılıçsoy", "yamal", "barella", "dimarco", "lukaku", "lautaro",
	"orkun kökçü", "ferdi kadıoğlu", "wirtz", "musiala", "bellingham", "pedri", "gavi", "saka"]
STAFF = ["rossi", "bianchi", "verdi", "cohen", "russo", "gallo", "costa", "fontana", "rinaldo", "longo"]
# Arguments that hold a person, per tool; "name" elsewhere is a setting, a group or a save.
PERSON = {"fm_player": {"name"}, "fm_offer": {"name"}, "fm_contract": {"name"}, "fm_staff_hire": {"name"},
	"fm_staff_fire": {"name"}, "fm_compare": {"a", "b"}, "fm_sub": {"off", "on"}, "fm_captains": {"captain", "vice"},
	"fm_actions": {"person"}, "fm_offers": {"offer"}, "fm_coaches": {"coach"}, "fm_register": {"add", "remove"},
	"fm_mentoring": {"players"}, "fm_happiness": {"player"}, "fm_numbers": {"player"}, "fm_penalties": {"player"},
	"fm_takers": {"player"}, "fm_training": {"player"}, "fm_intensity": {"player"}, "fm_propose": {"player"},
	"fm_release": {"player"}, "fm_opposition": {"player"}, "fm_tactic_player": {"player"}, "fm_approach": {"player"},
	"fm_scout_player": {"player"}, "fm_shortlist_change": {"player"}}
STAFF_TOOLS = {"fm_staff_hire", "fm_staff_fire", "fm_coaches", "fm_actions"}
VOWELS = set("aeıioöuü")


def calls(case):
	"""(tool, args) pairs of the main answer."""
	call, args = case.get("call"), case.get("args", {})
	if call is None:
		return []
	if isinstance(call, list):
		return list(zip(call, args if isinstance(args, list) else [{}] * len(call)))
	return [(call, args)]


def protected(text, case):
	"""Spans of the sentence that hold an argument value; typos stay out of them."""
	spans, low = [], text.lower().translate(FOLD)
	for _, args in calls(case):
		for v in args.values():
			for s in (v if isinstance(v, list) else [v]):
				if isinstance(s, str) and len(s) > 1:
					i = low.find(s.lower().translate(FOLD))
					if i >= 0:
						spans.append((i, i + len(s)))
	return spans


def typo(text, spans, rng):
	words = [m for m in re.finditer(r"[^\W\d_]{4,}", text) if not any(a < m.end() and m.start() < b for a, b in spans)]
	if not words:
		return None
	m = rng.choice(words)
	w, i = m.group(), rng.randrange(1, len(m.group()) - 1)
	kind = rng.choice(["drop", "double", "swap", "near"])
	if kind == "drop":
		w = w[:i] + w[i + 1:]
	elif kind == "double":
		w = w[:i] + w[i] + w[i:]
	elif kind == "swap":
		w = w[:i] + w[i + 1] + w[i] + w[i + 2:]
	else:
		c = w[i].lower()
		if c not in NEAR:
			return None
		w = w[:i] + rng.choice(NEAR[c]) + w[i + 1:]
	return text[:m.start()] + w + text[m.end():], f"typo-{kind}"


def swap_name(text, case, rng):
	"""Puts another name of the same kind (and the same last-letter sound) in place of one."""
	pairs = calls(case)
	found = []
	for n, (tool, args) in enumerate(pairs):
		for k, v in args.items():
			for s in (v if isinstance(v, list) else [v]):
				if k in PERSON.get(tool, ()) and isinstance(s, str) and not s.isdigit() and s.lower() in text.lower():
					found.append((tool, s))
	if not found:
		return None
	tool, old = rng.choice(found)
	pool = STAFF if tool in STAFF_TOOLS else OTHERS if old in OTHERS or " " in old else OURS
	vowel = old[-1] in VOWELS
	choices = [p for p in pool if p != old and (p[-1] in VOWELS) == vowel and p.lower() not in text.lower()]
	if not choices:
		return None
	new = rng.choice(choices)
	text = re.sub(re.escape(old), new, text, flags=re.I)

	def fix(v):
		if isinstance(v, list):
			return [fix(x) for x in v]
		return new if isinstance(v, str) and v.lower() == old.lower() else v

	out = dict(case)
	main = [(t, {k: fix(v) for k, v in a.items()}) for t, a in pairs]
	if isinstance(case["call"], list):
		out["args"] = [a for _, a in main]
	else:
		out["args"] = main[0][1]
	if "or" in case:
		out["or"] = [dict(alt, args=({k: fix(v) for k, v in alt["args"].items()} if isinstance(alt.get("args"), dict) else [{k: fix(v) for k, v in a.items()} for a in alt["args"]])) if "args" in alt else alt for alt in case["or"]]
	return text, out


def variant(case, lang, rng):
	text, out, done = case[lang], dict(case), []
	ops = rng.sample(["fold", "typo", "typo", "filler", "case", "name", "name"], k=rng.randint(1, 3))
	for op in ops:
		if op == "name" and "name" not in done:
			r = swap_name(text, out, rng)
			if r:
				text, out = r
				done.append("name")
		elif op == "fold" and lang == "tr" and text != text.translate(FOLD):
			text = text.translate(FOLD)
			done.append("fold")
		elif op == "typo":
			r = typo(text, protected(text, out), rng)
			if r:
				text = r[0]
				done.append(r[1])
		elif op == "filler":
			f = FILLER[lang]
			text = rng.choice(f["pre"]) + text if rng.random() < 0.5 else text + rng.choice(f["post"])
			done.append("filler")
		elif op == "case":
			text = text.upper() if rng.random() < 0.3 else text[:1].upper() + text[1:]
			done.append("case")
	out[lang] = text
	return out, done


def main():
	ap = argparse.ArgumentParser()
	ap.add_argument("-n", type=int, default=6, help="variants per case")
	ap.add_argument("--seed", type=int, default=1)
	ap.add_argument("--stdout", action="store_true")
	opt = ap.parse_args()
	rng = random.Random(opt.seed)
	rows, seen = [], set()
	for path in sorted(CASES.glob("*.jsonl")):
		for n, line in enumerate(path.read_text().splitlines(), 1):
			if not line.strip():
				continue
			case = json.loads(line)
			lang = "tr" if "tr" in case else "en"
			seen.add(case[lang].lower())
			made = 0
			for _ in range(opt.n * 3):
				if made >= opt.n:
					break
				v, ops = variant(case, lang, rng)
				key = v[lang].lower()
				if not ops or key in seen:
					continue
				seen.add(key)
				rows.append({**v, "from": f"{path.name}:{n}", "noise": ops})
				made += 1
	text = "".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows)
	if opt.stdout:
		print(text, end="")
		return
	OUT.parent.mkdir(exist_ok=True)
	OUT.write_text(text)
	print(f"{len(rows)} variants → {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
	main()
