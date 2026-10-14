#!/usr/bin/env python3
"""Takes L(...) off the texts whose templates are listed (one a line, as keys.py prints them): texts
that are not the CLI's words to translate (a data path, a date format, an English name).

Usage: tools/labels/unwrap.py <templates.txt> [cli/fm]
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from wrap import literal_end, starts_literal  # noqa: E402
from keys import key_of, esc  # noqa: E402

drop = {line.rstrip("\n") for line in open(sys.argv[1], encoding="utf-8") if line.strip()}
folder = sys.argv[2] if len(sys.argv) > 2 else "cli/fm"
total = 0
for name in sorted(os.listdir(folder)):
    if not name.endswith(".cs"):
        continue
    path = os.path.join(folder, name)
    src = open(path, encoding="utf-8").read()
    out, j = [], 0
    for m in re.finditer(r"(?<![\w.])L\(", src):
        k = m.end()
        if m.start() < j or k >= len(src) or src[k] not in "$@\"" or not starts_literal(src, k):
            continue
        end = literal_end(src, k)
        if end < len(src) and src[end] == ")" and esc(key_of(src[k:end])) in drop:
            out.append(src[j:m.start()])
            out.append(src[k:end])
            j = end + 1
            total += 1
    out.append(src[j:])
    new = "".join(out)
    if new != src:
        open(path, "w", encoding="utf-8").write(new)
print(f"{total} unwrapped")
