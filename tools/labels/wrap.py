#!/usr/bin/env python3
"""Wraps the CLI's own printed texts in L(...) (cli/fm/Labels.cs).

Inside the arguments of the calls that print (Console.WriteLine, Ui.Line, Table.Write ...), every
string literal with a letter in it becomes L(literal), also the ones inside an interpolation's holes.
Literals already inside L(...), Ui.Safe(...) or GameWords.*(...) (the game's own words) are left alone.

Usage: tools/labels/wrap.py [--dry] file.cs ...
"""
import re
import sys

CALLS = [
    "Console.WriteLine(", "Console.Error.WriteLine(", "Console.Write(", "Console.Error.Write(",
    "Ui.Line(", "Ui.Ask(", "Table.Write(", "Report(",
]
SKIP_CALLS = ["L(", "Ui.Safe(", "GameWords.Of(", "GameWords.InLine(", "Field(", "Fm.Get(", "Fm.GetJson(", "Fm.Pull("]


def literal_end(src, i):
    """Index just past the string literal starting at i (at its first char: $, @ or ")."""
    j = i
    interp = verbatim = False
    while src[j] in "$@":
        if src[j] == "$":
            interp = True
        else:
            verbatim = True
        j += 1
    assert src[j] == '"', (i, src[i:i + 20])
    j += 1
    while True:
        c = src[j]
        if verbatim and c == '"' and src[j + 1] == '"':
            j += 2
            continue
        if not verbatim and c == "\\":
            j += 2
            continue
        if c == '"':
            return j + 1
        if interp and c == "{":
            if src[j + 1] == "{":
                j += 2
                continue
            j = hole_end(src, j + 1)
            continue
        j += 1


def hole_end(src, j):
    """Index just past the '}' closing an interpolation hole whose content starts at j."""
    depth = 0
    while True:
        c = src[j]
        if c in "$@\"" and starts_literal(src, j):
            j = literal_end(src, j)
            continue
        if c == "'":
            j = char_end(src, j)
            continue
        if c in "({[":
            depth += 1
        elif c in ")]":
            depth -= 1
        elif c == "}":
            if depth == 0:
                return j + 1
            depth -= 1
        j += 1


def char_end(src, j):
    m = re.match(r"'(\\u[0-9a-fA-F]{4}|\\.|[^'\\\n])'", src[j:j + 8])
    return j + (len(m.group(0)) if m else 1)


def starts_literal(src, j):
    if src[j] == '"':
        return not (src[j + 1] == '"' and src[j + 2] == '"')  # raw literals are not handled
    m = re.match(r'(\$@|@\$|\$|@)"', src[j:j + 3])
    return m is not None


def is_ident_char(c):
    return c.isalnum() or c == "_"


def call_end(src, i):
    """Index of the ')' matching the '(' at i - 1."""
    depth = 1
    j = i
    while True:
        c = src[j]
        if c == "/" and src[j + 1] == "/":
            j = src.index("\n", j)
            continue
        if c in "$@\"" and starts_literal(src, j):
            j = literal_end(src, j)
            continue
        if c == "'":
            j = char_end(src, j)
            continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return j
        j += 1


# Calls whose literal arguments are shown as they are: the printing calls, and joining or padding text.
DISPLAY = {"WriteLine", "Write", "Line", "Ask", "Report", "L", "Join", "Concat", "Fit", "PadRight", "PadLeft", ""}


def opener_name(src, j):
    """The name of the call a '(' at j opens; "" for a group, a cast or a ternary's parentheses."""
    k = j
    while k > 0 and src[k - 1] == " ":
        k -= 1
    m = re.search(r"([A-Za-z_]\w*)\s*(<[^<>()]*>)?$", src[max(0, k - 60):k])
    if not m:
        return ""
    name = m.group(1)
    return "" if name in ("if", "while", "switch", "return", "is", "and", "or", "not", "await", "in", "new") else name


def compared(src, start, end):
    """Whether a literal is one side of an equality: a value tested, not a text shown."""
    before = src[max(0, start - 4):start].rstrip()
    after = src[end:end + 4].lstrip()
    return before.endswith(("==", "!=")) or after.startswith(("==", "!="))


def wrap_span(src, start, end, out, top="WriteLine"):
    """Copies src[start:end] to out, wrapping literals; skips calls in SKIP_CALLS."""
    stack = [top]
    j = start
    while j < end:
        c = src[j]
        if c == "/" and src[j + 1] == "/":
            k = src.index("\n", j)
            out.append(src[j:k])
            j = k
            continue
        for skip in SKIP_CALLS:
            if src.startswith(skip, j) and (j == 0 or not (is_ident_char(src[j - 1]) or src[j - 1] == ".")):
                k = call_end(src, j + len(skip))
                out.append(src[j:k + 1])
                j = k + 1
                break
        else:
            if c == "'":
                k = char_end(src, j)
                out.append(src[j:k])
                j = k
                continue
            if c == "(":
                stack.append(opener_name(src, j))
            elif c == ")" and len(stack) > 1:
                stack.pop()
            if c in "$@\"" and starts_literal(src, j) and not (c in "$@" and j > 0 and is_ident_char(src[j - 1])):
                k = literal_end(src, j)
                text = src[j:k]
                inner = []
                wrap_literal(src, j, k, inner)
                rebuilt = "".join(inner)
                if has_words(strip_holes(text)) and not in_pattern(src, j, k) and "await " not in text \
                        and stack[-1] in DISPLAY and not compared(src, j, k):
                    out.append("L(" + rebuilt + ")")
                else:
                    out.append(rebuilt)
                j = k
                continue
            out.append(c)
            j += 1


def in_pattern(src, start, end):
    """Whether a literal is a constant in a pattern (a switch arm, "is", a property pattern), where a call cannot be."""
    before = src[max(0, start - 40):start]
    after = src[end:end + 12]
    if re.match(r"\s*(=>|or\b|and\b|\}|when\b)", after):
        return True
    return re.search(r"(\b(is|case|or|not|and)|\w:)\s*$", before) is not None


def has_words(text):
    """Whether a literal holds words: escapes ("\\n") and markup ("[bold]", "[/]") are not words."""
    text = re.sub(r"\\.", "", text)
    text = re.sub(r"\[/?[a-z #0-9]*\]", "", text)
    text = re.sub(r'^[$@]+"|"$', "", text)
    return re.search(r"[A-Za-zÇĞİÖŞÜçğıöşü]{2,}", text) is not None


def strip_holes(text):
    """The literal parts of a literal: an interpolation's holes left out."""
    if not text.startswith(("$", "@$")):
        return text
    res = []
    j = 0
    while j < len(text):
        c = text[j]
        if c == "{":
            if j + 1 < len(text) and text[j + 1] == "{":
                j += 2
                continue
            k = hole_end(text, j + 1)
            j = k
            continue
        res.append(c)
        j += 1
    return "".join(res)


def wrap_literal(src, start, end, out):
    """Copies a literal, wrapping the literals inside its holes."""
    head = src[start:end]
    if not head.lstrip("$@").startswith('"') or "$" not in head[:3]:
        out.append(head)
        return
    j = start
    while src[j] in "$@":
        j += 1
    out.append(src[start:j + 1])
    verbatim = "@" in src[start:j]
    j += 1
    while j < end:
        c = src[j]
        if verbatim and c == '"' and src[j + 1] == '"':
            out.append('""')
            j += 2
            continue
        if not verbatim and c == "\\":
            out.append(src[j:j + 2])
            j += 2
            continue
        if c == "{":
            if src[j + 1] == "{":
                out.append("{{")
                j += 2
                continue
            k = hole_end(src, j + 1)
            out.append("{")
            # A hole's format or alignment comes after a top-level ':' or ','; only the expression is wrapped.
            wrap_span(src, j + 1, k - 1, out, top="")
            out.append("}")
            j = k
            continue
        out.append(c)
        j += 1


def process(src):
    out = []
    j = 0
    n = len(src)
    while j < n:
        c = src[j]
        if c == "/" and j + 1 < n and src[j + 1] == "/":
            k = src.find("\n", j)
            k = n if k < 0 else k
            out.append(src[j:k])
            j = k
            continue
        if c == "/" and j + 1 < n and src[j + 1] == "*":
            k = src.index("*/", j) + 2
            out.append(src[j:k])
            j = k
            continue
        if c in "$@\"" and starts_literal(src, j):
            k = literal_end(src, j)
            out.append(src[j:k])
            j = k
            continue
        if c == '"' and src.startswith('"""', j):
            k = src.index('"""', j + 3) + 3
            out.append(src[j:k])
            j = k
            continue
        if c == "'":
            k = char_end(src, j)
            out.append(src[j:k])
            j = k
            continue
        hit = None
        for call in CALLS:
            if src.startswith(call, j) and (j == 0 or not (is_ident_char(src[j - 1]) or src[j - 1] == ".")):
                hit = call
                break
        if hit:
            k = call_end(src, j + len(hit))
            out.append(hit)
            wrap_span(src, j + len(hit), k, out)
            out.append(")")
            j = k + 1
            continue
        out.append(c)
        j += 1
    return "".join(out)


def main():
    args = sys.argv[1:]
    dry = "--dry" in args
    files = [a for a in args if a != "--dry"]
    for f in files:
        src = open(f, encoding="utf-8").read()
        try:
            new = process(src)
        except Exception as e:
            print(f"{f}: {e!r}", file=sys.stderr)
            continue
        if new != src:
            if dry:
                import difflib
                sys.stdout.writelines(difflib.unified_diff(src.splitlines(True), new.splitlines(True), f, f, n=0))
            else:
                open(f, "w", encoding="utf-8").write(new)


if __name__ == "__main__":
    main()
