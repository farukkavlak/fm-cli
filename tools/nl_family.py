"""Sentence families of tests/nl: near-copies that must stay on one side of the split.

Two sentences are one family when their letter-trigram cosine (Turkish letters folded) is at
least ANY, or at least SAME when they expect the same call. Training sentences (tests/nl/train/)
join families too: a test case near a training sentence has been seen, so its family is dev.

Every test case carries its side in "split": dev (tune on it), held (score new wording),
final (scored only to confirm a decision; never tune on it). tools/nl-split.py fills it in for
new cases, tools/nl-check.py checks that no family crosses sides.
"""
import hashlib
import json
import re
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CASES = ROOT / "tests/nl"
SAME, ANY = 0.65, 0.8
SPLITS = ("dev", "held", "final")
# a new family that no training sentence touches: sha1(its first sentence) % 100
FINAL, HELD = 18, 48  # < FINAL is final, < HELD is held, the rest dev
RULE = f"families (trigram cosine >= {ANY}, or >= {SAME} with the same call; near a training sentence = dev); new family: sha1 % 100 < {FINAL} final, < {HELD} held"


def fold(s):
	s = str(s).lower().replace("ı", "i").replace("i̇", "i")
	s = "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")
	return re.sub(r"[^a-z0-9]+", " ", s).strip()


def grams(s):
	s = f" {fold(s)} "
	return Counter(s[i:i + 3] for i in range(len(s) - 2))


def cosine(a, b):
	n = sum(v * b[k] for k, v in a.items() if k in b)
	return n / ((sum(v * v for v in a.values()) * sum(v * v for v in b.values())) ** .5 or 1)


def rows():
	"""Every sentence: test cases (with where they are) and training sentences."""
	out = []
	for test, files in ((True, sorted(CASES.glob("*.jsonl"))), (False, sorted((CASES / "train").glob("*.jsonl")))):
		for f in files:
			for n, line in enumerate(f.read_text().splitlines(), 1):
				if line.strip():
					c = json.loads(line)
					out.append({"where": f"{f.name}:{n}", "path": f, "line": n, "case": c, "text": c.get("tr") or c.get("en"),
						"call": json.dumps(c.get("call")), "test": test, "split": c.get("split")})
	return out


def families(rs):
	"""Lists of indexes into rs, one per family."""
	gs = [grams(r["text"]) for r in rs]
	inv = defaultdict(set)
	for i, g in enumerate(gs):
		for k in g:
			inv[k].add(i)
	par = list(range(len(rs)))

	def find(x):
		while par[x] != x:
			par[x] = par[par[x]]
			x = par[x]
		return x

	for i, g in enumerate(gs):
		seen = set()
		for k in g:
			seen |= {j for j in inv[k] if j > i}
		for j in seen:
			if not (rs[i]["test"] or rs[j]["test"]):
				continue  # two training sentences: no matter
			s = cosine(g, gs[j])
			if s >= ANY or (s >= SAME and rs[i]["call"] == rs[j]["call"]):
				par[find(i)] = find(j)
	fam = defaultdict(list)
	for i in range(len(rs)):
		fam[find(i)].append(i)
	return list(fam.values())


def new_split(rs, fam):
	"""The side for a family none of whose cases has one yet."""
	if any(not rs[i]["test"] for i in fam):
		return "dev"
	key = min(fold(rs[i]["text"]) for i in fam)
	b = int(hashlib.sha1(key.encode()).hexdigest(), 16) % 100
	return "final" if b < FINAL else "held" if b < HELD else "dev"
