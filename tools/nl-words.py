#!/usr/bin/env python3
"""Writes cli/fm/Nl/common-words.txt: the most used words of Turkish and English, folded (no
Turkish letters), each with the languages it is common in, for the name reader. A word on it is
a weak name: "arda güler", all common words, is a name only when nothing less common is; an
English one at the edge of a name goes ("sell rinaldi" → rinaldi). Turkish ones stay at the
edge, since Turkish surnames are words (yıldız, güler).

Source: FrequencyWords (Hermit Dave), OpenSubtitles 2018, CC BY-SA 4.0
https://github.com/hermitdave/FrequencyWords — the top TOP words of each list. Players' names
sit lower than that (esposito 17k, marin 16k, rinaldi 34k in English).

  tools/nl-words.py            # downloads the lists, writes the file
"""
import re
import sys
import unicodedata
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "cli/fm/Nl/common-words.txt"
URL = "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/{}/{}_50k.txt"
TOP = 15000


def fold(s):
	s = s.lower().replace("ı", "i")
	return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")


def main():
	words = {}
	for lang in ("tr", "en"):
		text = urllib.request.urlopen(URL.format(lang, lang)).read().decode()
		for line in text.splitlines()[:TOP]:
			w = fold(line.split()[0])
			if re.fullmatch("[a-z]{3,}", w):
				words.setdefault(w, set()).add(lang)
	head = ("# Most used Turkish and English words, folded: weak names for cli/fm/Nl/Ctx.cs.\n"
		"# Written by tools/nl-words.py from FrequencyWords (Hermit Dave), OpenSubtitles 2018, CC BY-SA 4.0.\n")
	OUT.write_text(head + "\n".join(f"{w}\t{' '.join(sorted(words[w]))}" for w in sorted(words)) + "\n")
	print(f"{len(words)} words → {OUT.relative_to(ROOT)}", file=sys.stderr)


if __name__ == "__main__":
	main()
