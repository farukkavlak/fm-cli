#!/usr/bin/env python3
"""Wraps the Turkish values of the CLI's word tables (["Star Player"] = "Yıldız") in L(...), and adds
to the catalog the ones whose key is the game's own English ("Star Player"): in a game that is not
Turkish the game's word is shown. Keys that are data names ("PositionCombinedString") are listed for
translating by hand. Lower-case keys are words typed in (aliases) and are left alone.

Usage: tools/labels/dicts.py file.cs ...
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from wrap import ascii_turkish  # noqa: E402

CATALOG = "cli/fm/labels-english.tsv"
ENTRY = re.compile(r'\["((?:[^"\\]|\\.)+)"\](\s*=\s*)"((?:[^"\\]|\\.)*)"')
have = {l.split("\t", 1)[0] for l in open(CATALOG, encoding="utf-8") if "\t" in l and not l.startswith("#")}
added = []
by_hand = []
for f in sys.argv[1:]:
    src = open(f, encoding="utf-8").read()

    def wrap(m):
        key, eq, value = m.group(1), m.group(2), m.group(3)
        turkish = re.search("[çğıöşüÇĞİÖŞÜ]", value) or ascii_turkish('"' + value + '"')
        if not turkish or key == key.lower():
            return m.group(0)
        if value not in have:
            if " " in key.strip() or not re.search(r"[a-z][A-Z]", key):
                added.append((value, key))
            else:
                by_hand.append((value, key))
            have.add(value)
        return f'["{key}"]{eq}L("{value}")'

    new = ENTRY.sub(wrap, src)
    if new != src:
        open(f, "w", encoding="utf-8").write(new)
with open(CATALOG, "a", encoding="utf-8") as out:
    for value, key in added:
        out.write(f"{value}\t{key}\n")
print(f"{len(added)} added from their keys; by hand:")
for value, key in by_hand:
    print(f"{value}\t{key}")
