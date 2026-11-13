#!/usr/bin/env python3
"""Which of the game's identities the code leans on, and which a new game version dropped.

The CLI and the plugin reach the game by identity: page GUIDs, event names, element names, panel
names, data-model properties, enum members, the plugin's private fields, and (weakest) the game's
English words. The FM26 reference catalogs in docs/reference/ hold every one the game had. This
crosses them with the string literals in the code, so a new game's catalogs can be checked in one
pass instead of command by command (docs/PORTING.md, "FM27 on Apple Silicon").

    tools/identity-check.py uses                 # write docs/reference/identities.tsv from the code
    tools/identity-check.py catalog DIR          # dump the running game's catalogs into DIR
    tools/identity-check.py check DIR            # what in identities.tsv DIR's catalogs no longer have

`catalog` needs the game running with the plugin (and FM_BEP=<the new BepInEx folder> for the
metadata dumps); the button catalog opens every page, so leave the game between matches.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import urllib.request
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REFERENCE = ROOT / "docs/reference"
USES = REFERENCE / "identities.tsv"
SOURCES = [ROOT / "cli/fm", ROOT / "plugin/FmCliProbe/src"]
BRIDGE = "http://127.0.0.1:7777/"

# Strongest first: a literal that is both an element name and a translated word counts as the element.
KINDS = ["page", "event", "element", "panel", "field", "path", "property", "enum", "text"]


# --------------------------------------------------------------------------- catalogs

def read_catalogs(folder: Path) -> dict[str, set[str]]:
    """Every identity the game had, by kind, from the catalog files that are in `folder`."""
    cat: dict[str, set[str]] = {}

    buttons = folder / "buttons.txt"
    if buttons.exists():
        pages, elements, events = set(), set(), set()
        for line in buttons.read_text(encoding="utf-8").splitlines():
            if m := re.match(r"## .*\(([0-9a-f]{32})\)", line):
                pages.add(m.group(1))
            elif line.startswith("  ") and " | " in line:
                # "  label | name | events", an empty label written "-"
                parts = [p.strip() for p in line.split("|")]
                if len(parts) >= 3:
                    if parts[1]:
                        elements.add(parts[1])
                    for ev in parts[2].split(","):
                        ev = ev.strip()
                        if ev:
                            events.add(ev)
                            events.update(ev.split(":"))
        cat["page"], cat["element"] = pages, elements
        cat.setdefault("event", set()).update(events)

    ev = folder / "events.txt"
    if ev.exists():
        for line in ev.read_text(encoding="utf-8").splitlines():
            if line and not line.startswith(" ") and (m := re.match(r"(\S+)\s+\[", line)):
                cat.setdefault("event", set()).add(re.split(r"[.+]", m.group(1))[-1])

    enums = folder / "enums.txt"
    if enums.exists():
        members, owner = set(), ""
        for line in enums.read_text(encoding="utf-8").splitlines():
            if line and not line.startswith(" "):
                owner = line.split()[0]
            elif (m := re.match(r"\s+-?\d+\s+(\S+)", line)):
                members.add(m.group(1))
                # the game's own event and action ids are what buttons send
                if owner in ("FM.GamePlugin.InteropEventIDs", "FM.GamePlugin.ActionID"):
                    cat.setdefault("event", set()).add(m.group(1))
        cat["enum"] = members

    panels = folder / "panels.txt"
    if panels.exists():
        cat["panel"] = {m.group(1) for line in panels.read_text(encoding="utf-8").splitlines()
                        if (m := re.match(r"\s+\d+\s+(\S+)", line))}

    model = folder / "data-model.txt"
    if model.exists():
        props = set()
        for line in model.read_text(encoding="utf-8").splitlines():
            if line and not line.startswith(" "):
                props.add(line.split()[0].split(".")[-1].removesuffix("Reference"))
            elif (m := re.match(r"\s+-?\d+\s+\S+\s+(\S+)", line)):
                props.add(m.group(1))
        cat["property"] = props

    members = folder / "members.tsv"
    if members.exists():
        cat["field"] = {line.split("\t")[1] for line in members.read_text(encoding="utf-8").splitlines()
                        if "\t" in line and line.split("\t")[1].startswith("m_")}

    words = folder / "translations-en.tsv"
    if words.exists():
        cat["text"] = {line.split("\t")[1] for line in words.read_text(encoding="utf-8").splitlines()
                       if "\t" in line and not line.startswith("#")}
    return cat


# --------------------------------------------------------------------------- the code

LITERAL = re.compile(r'(?<![$@])"((?:[^"\\\n]|\\.)*)"')
# Literals the code uses as a control's name or an event whatever the catalogs say: the match
# screens are not in buttons.txt (the catalog cannot open them), so these are kept as identities
# and reported as "seen only live" when no catalog has them.
AS_NAME = [re.compile(p) for p in (
    r'Field\(\w+(?:!?\.Value)?, "Name"\) == "([^"]+)"',
    r'\("(?:name|anchorname)", "([^"]+)"\)',
)]
AS_EVENT = [re.compile(p) for p in (
    r'"Events"\)(?:\.Contains\(| == )"([^"]+)"',
    r'Field\(\w+(?:!?\.Value)?, "Events"\) == "([^"]+)"',
)]
FIELD = re.compile(r"\bm_[A-Za-z]+\b")


def literals():
    """(text, "file:line") for every plain string literal and m_ field in the sources."""
    for root in SOURCES:
        for path in sorted(root.rglob("*.cs")):
            if "/obj/" in str(path) or "/bin/" in str(path):
                continue
            rel = path.relative_to(ROOT)
            for n, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
                if line.lstrip().startswith("//"):
                    continue
                code = line
                for m in LITERAL.finditer(code):
                    yield m.group(1), f"{rel}:{n}", None
                for kind, patterns in (("element", AS_NAME), ("event", AS_EVENT)):
                    for p in patterns:
                        for m in p.finditer(code):
                            yield m.group(1), f"{rel}:{n}", kind
                if "plugin" in str(rel):
                    for m in FIELD.finditer(code):
                        yield m.group(0), f"{rel}:{n}", None


def camel(s: str) -> bool:
    return len(s) >= 5 and re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", s) is not None and sum(c.isupper() for c in s) >= 2


def classify(text: str, cat: dict[str, set[str]]) -> tuple[str, str] | None:
    """(kind, identity) when a literal is one of the game's identities, else None."""
    if re.fullmatch(r"[0-9a-f]{32}", text):
        return "page", text
    for kind in ("event", "element", "panel"):
        if text in cat.get(kind, ()) and (camel(text) or kind != "event"):
            if len(text) >= 4:
                return kind, text
    if text.startswith("m_") and text in cat.get("field", ()):
        return "field", text
    # a data path: dotted, and at least one segment a property the game names
    if "." in text and re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+", text):
        segs = text.split(".")
        if any(s in cat.get("property", ()) for s in segs if camel(s) or s[:1].isupper()):
            return "path", text
    if camel(text) and text in cat.get("property", ()):
        return "property", text
    if camel(text) and text in cat.get("enum", ()):
        return "enum", text
    if " " in text and len(text) >= 6 and text in cat.get("text", ()):
        return "text", text
    return None


def uses() -> int:
    cat = read_catalogs(REFERENCE)
    found: dict[tuple[str, str], list[str]] = defaultdict(list)
    for text, where, used_as in literals():
        hit = classify(text, cat)
        if hit is None and used_as and re.fullmatch(r"[A-Za-z][A-Za-z0-9_:-]+", text):
            hit = (used_as, text)
        if hit and where not in found[hit]:
            found[hit].append(where)
    rows = sorted(found.items(), key=lambda kv: (KINDS.index(kv[0][0]), kv[0][1]))
    with USES.open("w", encoding="utf-8") as out:
        out.write("# Identities the code uses, crossed with the FM26 catalogs (tools/identity-check.py uses)\n")
        out.write("# kind\tidentity\twhere (first three)\tcount\n")
        for (kind, ident), where in rows:
            out.write(f"{kind}\t{ident}\t{' '.join(where[:3])}\t{len(where)}\n")
    counts = defaultdict(int)
    for (kind, _), _ in rows:
        counts[kind] += 1
    print(f"{len(rows)} identities -> {USES.relative_to(ROOT)}")
    print("  " + "  ".join(f"{k} {counts[k]}" for k in KINDS if counts[k]))
    return 0


# --------------------------------------------------------------------------- check

def check(folder: Path) -> int:
    cat = read_catalogs(folder)
    if not cat:
        print(f"no catalogs in {folder} (buttons.txt, events.txt, enums.txt, panels.txt, data-model.txt, members.tsv, translations-en.tsv)")
        return 1
    old = read_catalogs(REFERENCE)
    rows = [line.split("\t") for line in USES.read_text(encoding="utf-8").splitlines() if line and not line.startswith("#")]
    report: dict[str, dict[str, list]] = defaultdict(lambda: {"ok": [], "missing": [], "unchecked": []})
    for kind, ident, where, count in rows:
        home = "property" if kind == "path" else kind
        if home not in cat:
            report[kind]["unchecked"].append(ident)
            continue
        if kind == "path":
            # only the segments FM26 named; plugin-made roots (fmcli.sides…) are not the game's
            gone = [s for s in ident.split(".") if s in old.get("property", ()) and s not in cat["property"]]
            (report[kind]["missing"] if gone else report[kind]["ok"]).append((ident, where, count, gone))
        elif ident in cat[home]:
            report[kind]["ok"].append((ident, where, count, []))
        elif ident not in old.get(home, ()):
            # never in a catalog (a match screen's control): only a live run tells
            report[kind]["unchecked"].append(ident)
        else:
            report[kind]["missing"].append((ident, where, count, []))

    total_missing = 0
    print(f"{'kind':10} {'ok':>5} {'missing':>8} {'unchecked':>10}")
    for kind in KINDS:
        if kind in report:
            r = report[kind]
            total_missing += len(r["missing"])
            print(f"{kind:10} {len(r['ok']):5} {len(r['missing']):8} {len(r['unchecked']):10}")
    for kind in KINDS:
        if kind in report and report[kind]["missing"]:
            print(f"\n## missing {kind} ({len(report[kind]['missing'])})")
            for ident, where, count, gone in sorted(report[kind]["missing"], key=lambda r: -int(r[2])):
                extra = f"  [gone: {', '.join(gone)}]" if gone else ""
                print(f"  {ident}{extra}\n      used {count}x: {where}")
    return 1 if total_missing else 0


# --------------------------------------------------------------------------- catalog

def bridge(route: str) -> str:
    with urllib.request.urlopen(BRIDGE + route, timeout=120) as r:
        return r.read().decode("utf-8")


def catalog(folder: Path) -> int:
    folder.mkdir(parents=True, exist_ok=True)
    sig = ["dotnet", "run", "--project", str(ROOT / "tools/sigdump"), "--"]
    for name, args in [("enums.txt", ["--enums"]), ("events.txt", ["--catalog", "events"]), ("members.tsv", ["--members"])]:
        print(f"  {name} (sigdump {' '.join(args)}, FM_BEP={os.environ.get('FM_BEP', '~/fm26_bep')})")
        with (folder / name).open("w", encoding="utf-8") as out:
            subprocess.run(sig + args, stdout=out, check=False)
    for name, route in [("data-model.txt", "schema"), ("panels.txt", "panels")]:
        print(f"  {name} (/{route})")
        (folder / name).write_text(bridge(route), encoding="utf-8")
    # The English words only mean something when the game is in English.
    head = bridge("translate?from=1&to=1").splitlines()[0]
    if "language=english" in head:
        print("  translations-en.tsv (/translate)")
        lines = []
        for start in range(1, 200001, 5000):
            chunk = [l for l in bridge(f"translate?from={start}&to={start + 4999}").splitlines()[1:] if "\t" in l]
            if not chunk and start > 60000:
                break
            lines += [l.rstrip("\t") for l in chunk]
        (folder / "translations-en.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    else:
        print(f"  translations-en.tsv skipped: the game is not in English ({head.split()[0]})")
    print("  buttons.txt (tools/buttons-catalog.py, opens every page)")
    subprocess.run([sys.executable, str(ROOT / "tools/buttons-catalog.py"), str(folder / "buttons.txt")], check=False)
    return 0


def main() -> int:
    if len(sys.argv) >= 2 and sys.argv[1] == "uses":
        return uses()
    if len(sys.argv) >= 3 and sys.argv[1] == "check":
        return check(Path(sys.argv[2]))
    if len(sys.argv) >= 3 and sys.argv[1] == "catalog":
        return catalog(Path(sys.argv[2]))
    print(__doc__)
    return 64


if __name__ == "__main__":
    sys.exit(main())
