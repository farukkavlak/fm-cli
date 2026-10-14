#!/usr/bin/env python3
"""Lists the templates of the CLI's own texts (every L("...") / L($"...") in cli/fm), as Labels.cs
keys them: an interpolation's holes become {0}, {1,-24}, {2:0.0}; braces written as text are doubled.

Usage: tools/labels/keys.py [--missing] [cli/fm]
  --missing   only the ones cli/fm/labels-english.tsv does not hold yet, as "key<TAB>" lines
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from wrap import literal_end, hole_end, starts_literal  # noqa: E402

ESCAPES = {"n": "\n", "t": "\t", "r": "\r", "\\": "\\", '"': '"', "'": "'", "0": "\0", "e": "\x1b"}


def unescape(s, verbatim):
    if verbatim:
        return s.replace('""', '"')
    out = []
    j = 0
    while j < len(s):
        c = s[j]
        if c == "\\":
            n = s[j + 1]
            if n == "u":
                out.append(chr(int(s[j + 2:j + 6], 16)))
                j += 6
                continue
            out.append(ESCAPES.get(n, n))
            j += 2
            continue
        out.append(c)
        j += 1
    return "".join(out)


def split_hole(expr):
    """The expression, alignment and format of a hole's text."""
    depth = 0
    j = 0
    while j < len(expr):
        c = expr[j]
        if c in "$@\"" and starts_literal(expr + "   ", j):
            j = literal_end(expr + "   ", j)
            continue
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif depth == 0 and c in ",:":
            rest = expr[j:]
            m = re.match(r"(,\s*-?\d+)?(:.*)?$", rest, re.S)
            if m:
                align = m.group(1).replace(" ", "")[1:] if m.group(1) else None
                fmt = m.group(2)[1:] if m.group(2) else None
                return expr[:j], align, fmt
        j += 1
    return expr, None, None


def key_of(lit):
    j = 0
    interp = verbatim = False
    while lit[j] in "$@":
        interp |= lit[j] == "$"
        verbatim |= lit[j] == "@"
        j += 1
    body = lit[j + 1:-1]
    if not interp:
        return unescape(body, verbatim).replace("{", "{{").replace("}", "}}")
    out = []
    part = []
    n = 0
    k = 0
    while k < len(body):
        c = body[k]
        if c == "{" and k + 1 < len(body) and body[k + 1] == "{":
            part.append("{")
            k += 2
            continue
        if c == "}" and k + 1 < len(body) and body[k + 1] == "}":
            part.append("}")
            k += 2
            continue
        if c == "{":
            out.append(unescape("".join(part), verbatim).replace("{", "{{").replace("}", "}}"))
            part = []
            end = hole_end(body + "    ", k + 1)
            _, align, fmt = split_hole(body[k + 1:end - 1])
            out.append("{" + str(n) + ("," + align if align else "") + (":" + fmt if fmt else "") + "}")
            n += 1
            k = end
            continue
        part.append(c)
        k += 1
    out.append(unescape("".join(part), verbatim).replace("{", "{{").replace("}", "}}"))
    return "".join(out)


def keys(folder):
    found = {}
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".cs"):
            continue
        src = open(os.path.join(folder, name), encoding="utf-8").read()
        for m in re.finditer(r"(?<![\w.])L\(", src):
            j = m.end()
            if j < len(src) and src[j] in "$@\"" and starts_literal(src, j):
                k = literal_end(src, j)
                found.setdefault(key_of(src[j:k]), f"{name}:{src.count(chr(10), 0, j) + 1}")
    return found


def esc(s):
    return s.replace("\t", "\\t").replace("\n", "\\n")


def main():
    args = sys.argv[1:]
    missing = "--missing" in args
    folder = next((a for a in args if not a.startswith("--")), "cli/fm")
    have = set()
    catalog = os.path.join(folder, "labels-english.tsv")
    if missing and os.path.exists(catalog):
        for line in open(catalog, encoding="utf-8"):
            line = line.rstrip("\n")
            if line and not line.startswith("#") and "\t" in line:
                have.add(line.split("\t", 1)[0])
    for key, where in keys(folder).items():
        if missing and esc(key) in have:
            continue
        print(f"{esc(key)}\t" if missing else f"{where}\t{esc(key)}")


if __name__ == "__main__":
    main()
