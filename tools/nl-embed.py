#!/usr/bin/env python3
"""An embedding candidate for the natural-language router, to measure with tools/nl-score.py.

Every dev sentence of tests/nl/*.jsonl is embedded once; a typed sentence is embedded and the
tool is voted by its nearest dev neighbours (cosine, weighted). Held sentences are never in the
index, so the held score is honest. Arguments come from the rule router: with --hybrid the
picked tool is handed to `fm __route` ("tool" on the line) and its reader fills the arguments;
a sentence the rules split into several asks stays split, and with --trust-rules S the rules'
own pick wins when it scores S or more.

  tools/nl-score.py --router "tools/nl-embed.py" --set base
  tools/nl-score.py --router "tools/nl-embed.py --model minilm --k 5" --set base
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
from collections import defaultdict
from pathlib import Path

import numpy as np
import onnxruntime as ort
from huggingface_hub import hf_hub_download
from tokenizers import Tokenizer

ROOT = Path(__file__).resolve().parent.parent
FM = ROOT / "cli/fm/bin/Debug/net10.0/fm"
MODELS = {
	# name: (repo, onnx file, tokenizer file, query prefix)
	"e5": ("intfloat/multilingual-e5-small", "onnx/model.onnx", "onnx/tokenizer.json", "query: "),
	"minilm": ("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", "onnx/model.onnx", "tokenizer.json", ""),
	"minilm-q": ("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", "onnx/model_qint8_arm64.onnx", "tokenizer.json", ""),
}


class Embedder:
	def __init__(self, name):
		if name in MODELS:
			repo, onnx, tok, self.prefix = MODELS[name]
			onnx, tok = hf_hub_download(repo, onnx), hf_hub_download(repo, tok)
		else:  # a directory from tools/nl-train.py
			d = Path(name).expanduser()
			onnx, tok, self.prefix = d / "model.onnx", d / "tokenizer.json", (d / "prefix.txt").read_text()
		self.tok = Tokenizer.from_file(str(tok))
		self.tok.enable_padding()
		self.tok.enable_truncation(64)
		so = ort.SessionOptions()
		so.intra_op_num_threads = 4
		self.sess = ort.InferenceSession(str(onnx), so, providers=["CPUExecutionProvider"])
		self.inputs = {i.name for i in self.sess.get_inputs()}

	def __call__(self, texts, batch=1):
		# one at a time: the int8 model scales a batch as a whole, so a sentence beside others
		# comes out a little different from the same sentence alone (as a typed one is embedded)
		out = []
		for i in range(0, len(texts), batch):
			enc = self.tok.encode_batch([self.prefix + t for t in texts[i:i + batch]])
			ids = np.array([e.ids for e in enc], dtype=np.int64)
			mask = np.array([e.attention_mask for e in enc], dtype=np.int64)
			feed = {"input_ids": ids, "attention_mask": mask}
			if "token_type_ids" in self.inputs:
				feed["token_type_ids"] = np.zeros_like(ids)
			h = self.sess.run(None, feed)[0]
			v = (h * mask[..., None]).sum(1) / mask.sum(1, keepdims=True)
			out.append(v / np.linalg.norm(v, axis=1, keepdims=True))
		return np.concatenate(out)


def dev_cases():
	rows = []
	for f in sorted((ROOT / "tests/nl").glob("*.jsonl")):
		for line in f.read_text().splitlines():
			if not line.strip():
				continue
			c = json.loads(line)
			text = c.get("tr") or c.get("en")
			if int(hashlib.sha1(text.encode()).hexdigest(), 16) % 10 < 3:
				continue  # held: never in the index
			call = c.get("call")
			rows.append((text, json.dumps(call)))
	for f in sorted((ROOT / "tests/nl/train").glob("*.jsonl")):
		for line in f.read_text().splitlines():
			if line.strip():
				c = json.loads(line)
				rows.append((c.get("tr") or c.get("en"), json.dumps(c.get("call"))))
	return rows


def tool_cards():
	"""Each MCP tool's description, sentence by sentence (English), as extra index entries."""
	src = (ROOT / "cli/fm/Core/Mcp.Tools.cs").read_text()
	rows = []
	for m in re.finditer(r'new\("(fm_\w+)", "((?:[^"\\]|\\.)*)"', src):
		for sent in re.split(r"(?<=[.;])\s+", m.group(2))[:2]:
			rows.append((sent, json.dumps(m.group(1))))
	return rows


def main():
	ap = argparse.ArgumentParser()
	ap.add_argument("--model", default="e5", help=f"{', '.join(MODELS)} or a tools/nl-train.py directory")
	ap.add_argument("--k", type=int, default=5)
	ap.add_argument("--none-below", type=float, default=0.0, help="no call when the best neighbour is less similar")
	ap.add_argument("--none-margin", type=float, default=0.0, help="\"nothing does this\" only when it outvotes the next tool by this much")
	ap.add_argument("--cards", action="store_true", help="also index each tool's own description")
	ap.add_argument("--hybrid", action="store_true", help="arguments from fm __route for the picked tool")
	ap.add_argument("--trust-rules", type=float, default=0.0, help="with --hybrid: the rules win from this score up (0: never)")
	a = ap.parse_args()

	t0 = time.perf_counter()
	emb = Embedder(a.model)
	dev = dev_cases() + (tool_cards() if a.cards else [])
	index = emb([t for t, _ in dev])
	labels = [l for _, l in dev]
	print(f"index {len(dev)} sentences in {time.perf_counter() - t0:.1f} s", file=sys.stderr)

	texts = [json.loads(l)["text"] for l in sys.stdin if l.strip()]
	t1 = time.perf_counter()
	q = emb(texts)
	sims = q @ index.T
	# a dev sentence must not find itself, or the dev score says nothing
	where = defaultdict(list)
	for j, (t, _) in enumerate(dev):
		where[t].append(j)
	for i, t in enumerate(texts):
		sims[i, where.get(t, [])] = -1.0
	print(f"{len(texts)} sentences, {1000 * (time.perf_counter() - t1) / max(len(texts), 1):.2f} ms each", file=sys.stderr)

	picks = []
	for row in sims:
		top = np.argsort(-row)[: a.k]
		votes = defaultdict(float)
		for j in top:
			votes[labels[j]] += float(row[j])
		ranked = sorted(votes, key=votes.get, reverse=True)
		best = ranked[0]
		if best == "null" and len(ranked) > 1 and votes["null"] - votes[ranked[1]] < a.none_margin:
			best = ranked[1]
		if row[top[0]] < a.none_below:
			best = "null"
		picks.append(json.loads(best))

	if not a.hybrid:
		for pick in picks:
			print(json.dumps({"call": pick}, ensure_ascii=False))
		return
	feed = "".join(json.dumps({"text": t, "tool": p if isinstance(p, str) else None}, ensure_ascii=False) + "\n"
		for t, p in zip(texts, picks))
	p = subprocess.run([str(FM), "__route"], input=feed, capture_output=True, text=True, check=True)
	for pick, r in zip(picks, (json.loads(l) for l in p.stdout.splitlines() if l.strip())):
		if isinstance(r.get("call"), list) or isinstance(pick, list) or (a.trust_rules and r.get("score", 0) >= a.trust_rules):
			out = r
		elif pick is None:
			out = {"call": None}
		else:
			out = r["forced"]
		print(json.dumps(out, ensure_ascii=False))

if __name__ == "__main__":
	main()
