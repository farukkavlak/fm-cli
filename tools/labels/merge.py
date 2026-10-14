#!/usr/bin/env python3
"""Adds numbered translations to cli/fm/labels-english.tsv.

Usage: tools/labels/merge.py <keys.txt> <translations.tsv> ...
  keys.txt: one template a line, as tools/labels/keys.py --missing prints them (its line number is the key's number)
  translations.tsv: "<number>\t<english>" lines
A translation whose holes ({0}, {1,-24}) differ from its template's is refused.
"""
import re
import sys

CATALOG = "cli/fm/labels-english.tsv"
keys = [line.rstrip("\n").rstrip("\t") for line in open(sys.argv[1], encoding="utf-8")]
have = {line.split("\t", 1)[0] for line in open(CATALOG, encoding="utf-8") if "\t" in line and not line.startswith("#")}
added = refused = 0
with open(CATALOG, "a", encoding="utf-8") as out:
    for f in sys.argv[2:]:
        for line in open(f, encoding="utf-8"):
            line = line.rstrip("\n")
            if not line or "\t" not in line:
                continue
            n, english = line.split("\t", 1)
            key = keys[int(n) - 1]
            holes = lambda s: sorted(re.findall(r"(?<!\{)\{\d+[^{}]*\}", s))
            if holes(key) != holes(english):
                print(f"{n}: holes differ: {key!r} -> {english!r}", file=sys.stderr)
                refused += 1
                continue
            if key in have:
                continue
            out.write(f"{key}\t{english}\n")
            have.add(key)
            added += 1
print(f"{added} added, {refused} refused")
