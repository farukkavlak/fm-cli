#!/usr/bin/env python3
"""Fine-tunes a small sentence embedder on the dev split of tests/nl, for tools/nl-embed.py.

Pairs of dev sentences that ask the same tool are pulled together, everything else in the batch
pushed apart (in-batch negatives). Noisy variants (tests/nl/expanded/) of dev sentences join in;
held sentences and their variants are never seen. The result is exported to ONNX with its
tokenizer, under ~/.cache/fm-cli/nl/<name>/, which `tools/nl-embed.py --model DIR` loads.

  tools/nl-train.py --epochs 2 --name mpnet-n --int8     # the best so far (~35 min on the CPU)
  tools/nl-train.py --base minilm --epochs 2
"""
import argparse
import hashlib
import json
import random
import shutil
import time
from collections import defaultdict
from pathlib import Path

import torch
import torch.nn.functional as F
from huggingface_hub import hf_hub_download
from tokenizers import Tokenizer
from transformers import AutoModel

ROOT = Path(__file__).resolve().parent.parent
BASES = {
	"minilm": ("sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2", "tokenizer.json", ""),
	"e5": ("intfloat/multilingual-e5-small", "tokenizer.json", "query: "),
	"mpnet": ("sentence-transformers/paraphrase-multilingual-mpnet-base-v2", "tokenizer.json", ""),
}


def held(text):
	return int(hashlib.sha1(text.encode()).hexdigest(), 16) % 10 < 3


def training_rows(noisy):
	rows, by_ref = [], {}
	for f in sorted((ROOT / "tests/nl").glob("*.jsonl")):
		for n, line in enumerate(f.read_text().splitlines(), 1):
			if not line.strip():
				continue
			c = json.loads(line)
			text = c.get("tr") or c.get("en")
			by_ref[f"{f.name}:{n}"] = text
			if not held(text):
				rows.append((text, json.dumps(c.get("call"))))
	# training-only sentences (never scored) and their noisy variants
	for f in sorted((ROOT / "tests/nl/train").glob("*.jsonl")):
		for line in f.read_text().splitlines():
			if line.strip():
				c = json.loads(line)
				rows.append((c.get("tr") or c.get("en"), json.dumps(c.get("call"))))
	exp = ROOT / "tests/nl/expanded/all.jsonl"
	if noisy and exp.exists():
		for line in exp.read_text().splitlines():
			c = json.loads(line)
			if not held(by_ref[c["from"]]):
				rows.append((c.get("tr") or c.get("en"), json.dumps(c.get("call"))))
	exp = ROOT / "tests/nl/expanded/train.jsonl"
	if noisy and exp.exists():
		for line in exp.read_text().splitlines():
			c = json.loads(line)
			rows.append((c.get("tr") or c.get("en"), json.dumps(c.get("call"))))
	return rows


def main():
	ap = argparse.ArgumentParser()
	ap.add_argument("--base", choices=BASES, default="mpnet")
	ap.add_argument("--epochs", type=int, default=3)
	ap.add_argument("--batch", type=int, default=16)
	ap.add_argument("--threads", type=int, default=4, help="CPU threads (the machine stays usable)")
	ap.add_argument("--mps", action="store_true", help="train on the GPU: faster, but shares the 8 GB with everything else")
	ap.add_argument("--lr", type=float, default=3e-5)
	ap.add_argument("--unfreeze", action="store_true", help="train the word table too (~2.5 GB)")
	ap.add_argument("--no-noisy", action="store_true", help="leave the noisy variants out")
	ap.add_argument("--name", help="output name (default: <base>-ft)")
	ap.add_argument("--int8", action="store_true", help="also write <name>-q: weights in int8, a quarter of the size, same scores")
	a = ap.parse_args()
	repo, tokfile, prefix = BASES[a.base]
	out = Path.home() / ".cache/fm-cli/nl" / (a.name or f"{a.base}-ft")
	random.seed(1)
	torch.manual_seed(1)

	tok = Tokenizer.from_file(hf_hub_download(repo, tokfile))
	tok.enable_padding()
	tok.enable_truncation(64)
	torch.set_num_threads(a.threads)
	dev = torch.device("mps" if a.mps else "cpu")
	model = AutoModel.from_pretrained(repo).to(dev)
	# the word table is most of the model (250k words); training it costs gigabytes and the
	# few sentences here could not move it well anyway
	model.embeddings.word_embeddings.weight.requires_grad_(a.unfreeze)
	train = [p for p in model.parameters() if p.requires_grad]
	print(f"training {sum(p.numel() for p in train) / 1e6:.0f}M of {sum(p.numel() for p in model.parameters()) / 1e6:.0f}M parameters")
	opt = torch.optim.AdamW(train, lr=a.lr)

	rows = training_rows(not a.no_noisy)
	by_label = defaultdict(list)
	for t, l in rows:
		by_label[l].append(t)
	print(f"{len(rows)} sentences, {len(by_label)} labels, on {dev}")

	def embed(texts):
		enc = tok.encode_batch([prefix + t for t in texts])
		ids = torch.tensor([e.ids for e in enc], device=dev)
		mask = torch.tensor([e.attention_mask for e in enc], device=dev)
		h = model(input_ids=ids, attention_mask=mask).last_hidden_state
		v = (h * mask[..., None]).sum(1) / mask.sum(1, keepdim=True)
		return F.normalize(v, dim=-1)

	model.train()
	for ep in range(a.epochs):
		t0 = time.time()
		order = rows[:]
		random.shuffle(order)
		total, steps = 0.0, 0
		batch, seen = [], set()
		for t, l in order:
			if l in seen or len(by_label[l]) < 2:
				continue
			pos = random.choice([x for x in by_label[l] if x != t] or [t])
			batch.append((t, pos))
			seen.add(l)
			if len(batch) == a.batch:
				anc, pos = embed([x for x, _ in batch]), embed([y for _, y in batch])
				logits = anc @ pos.T * 20.0
				target = torch.arange(len(batch), device=dev)
				loss = (F.cross_entropy(logits, target) + F.cross_entropy(logits.T, target)) / 2
				opt.zero_grad()
				loss.backward()
				opt.step()
				total += loss.item()
				steps += 1
				if steps % 50 == 0:
					print(f"  step {steps}: loss {total / steps:.3f}, {time.time() - t0:.0f} s", flush=True)
				batch, seen = [], set()
		print(f"epoch {ep + 1}: loss {total / max(steps, 1):.3f}, {steps} steps, {time.time() - t0:.0f} s")

	model.eval().to("cpu")
	if out.exists():
		shutil.rmtree(out)
	out.mkdir(parents=True)
	shutil.copy(hf_hub_download(repo, tokfile), out / "tokenizer.json")
	(out / "prefix.txt").write_text(prefix)
	model.save_pretrained(out / "torch")  # kept even if the export below fails
	ids = torch.ones(1, 8, dtype=torch.long)
	torch.onnx.export(model, (ids, torch.ones_like(ids)), str(out / "model.onnx"),
		input_names=["input_ids", "attention_mask"], output_names=["last_hidden_state"],
		dynamic_axes={"input_ids": {0: "b", 1: "n"}, "attention_mask": {0: "b", 1: "n"}, "last_hidden_state": {0: "b", 1: "n"}},
		opset_version=14)
	print(f"saved {out}")
	if a.int8:
		from onnxruntime.quantization import QuantType, quantize_dynamic
		q = out.with_name(out.name + "-q")
		shutil.rmtree(q, ignore_errors=True)
		q.mkdir()
		for f in ("tokenizer.json", "prefix.txt"):
			shutil.copy(out / f, q / f)
		quantize_dynamic(str(out / "model.onnx"), str(q / "model.onnx"), weight_type=QuantType.QInt8)
		print(f"saved {q}")


if __name__ == "__main__":
	main()
