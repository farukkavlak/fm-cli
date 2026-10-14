#!/usr/bin/env python3
"""Lists the Turkish texts in cli/fm that are not wrapped in L(...): what is left to put into the
game's language (raw literals, such as the help text, are listed by their first line)."""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from wrap import literal_end, starts_literal, char_end  # noqa: E402

TURKISH = re.compile("[çğıöşüÇĞİÖŞÜ]")
folder = sys.argv[1] if len(sys.argv) > 1 else "cli/fm"
for name in sorted(os.listdir(folder)):
    if not name.endswith(".cs") or name in ("Mcp.cs", "Labels.cs"):
        continue
    src = open(os.path.join(folder, name), encoding="utf-8").read()
    lines = src.splitlines()
    j, n = 0, len(src)
    while j < n:
        c = src[j]
        if src.startswith("//", j):
            j = src.find("\n", j)
            j = n if j < 0 else j
            continue
        if src.startswith("/*", j):
            j = src.index("*/", j) + 2
            continue
        raw = re.match(r'\$*"""', src[j:j + 8])
        if raw:
            k = src.index('"""', j + raw.end()) + 3
            if TURKISH.search(src[j:k]):
                line = src.count("\n", 0, j) + 1
                print(f"{name}:{line}\t(raw) {lines[line - 1].strip()[:150]}")
            j = k
            continue
        if c == "'":
            j = char_end(src, j)
            continue
        if c in '$@"' and starts_literal(src, j) and not (c in "$@" and j > 0 and (src[j - 1].isalnum() or src[j - 1] == "_")):
            k = literal_end(src, j)
            if TURKISH.search(src[j:k]) and not src[max(0, j - 2):j].endswith("L("):
                line = src.count("\n", 0, j) + 1
                print(f"{name}:{line}\t{lines[line - 1].strip()[:170]}")
            j = k
            continue
        j += 1
