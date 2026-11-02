#!/usr/bin/env python3
"""What `fm crawl` found, crossed with the code and with FM26's button catalog.

    tools/crawl-report.py crawl.json [out.md]

Per page: its own controls (the frame's - menus, back, search - are on every page and listed once),
each control's events, and for each event whether the code names it anywhere (cli/fm, the plugin)
and whether FM26's catalog (docs/reference/buttons.txt) had it. An event the code never names is a
thing the tool cannot do yet; one FM26 lacked is new in FM27.
"""
from __future__ import annotations

import json
import re
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCES = [ROOT / "cli/fm", ROOT / "plugin/FmCliProbe/src"]


def tokens(events: str) -> list[str]:
    """The event and action names in a control's Events ("A, RightClickEvents→B", "EventToPlugin:C")."""
    out = []
    for part in re.split(r",\s*|→|:", events):
        part = part.strip()
        if part and part not in ("RightClickEvents", "LeftClickEvents", "EventToPlugin", "GenericAction"):
            out.append(part)
    return out


def code_words() -> set[str]:
    words: set[str] = set()
    for root in SOURCES:
        for path in root.rglob("*.cs"):
            if "/obj/" in str(path) or "/bin/" in str(path):
                continue
            words.update(re.findall(r"[A-Za-z_][A-Za-z0-9_]{3,}", path.read_text(encoding="utf-8")))
    return words


def fm26_events() -> set[str]:
    found: set[str] = set()
    path = ROOT / "docs/reference/buttons.txt"
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("  ") and " | " in line:
            found.update(tokens(line.split(" | ")[-1]))
    return found


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 64
    pages = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    out = Path(sys.argv[2]) if len(sys.argv) > 2 else None
    used, old = code_words(), fm26_events()

    opened = [p for p in pages if "Buttons" in p]
    seen = Counter()
    for p in opened:
        seen.update({(b["Name"], b["Events"]) for b in p["Buttons"] if b["Events"]})
    frame = {k for k, n in seen.items() if n >= 0.8 * len(opened)}

    lines = [f"# FM27 crawl: {len(pages)} pages, {len(opened)} opened", ""]
    all_events: Counter = Counter()
    unused_by_page: dict[str, set[str]] = {}
    for p in opened:
        own = sorted({(b["Name"], b["Events"], b["Label"]) for b in p["Buttons"] if b["Events"] and (b["Name"], b["Events"]) not in frame})
        evs = {t for _, e, _ in own for t in tokens(e)}
        all_events.update(evs)
        unused = {e for e in evs if e not in used}
        title = " | ".join(p["Names"])
        unused_by_page[title] = unused
        lines.append(f"## {title}  ({p['Guid']})")
        lines.append(f"{len(own)} controls, {len(p['Dropdowns'])} lists, {len(unused)} events the code never names")
        for name, events, label in own:
            marks = []
            for t in tokens(events):
                tag = "" if t in used else " [not in code]"
                tag += "" if t in old else " [new]"
                marks.append(t + tag)
            lines.append(f"- `{name or '-'}` {label!r}: {', '.join(marks)}")
        for d in p["Dropdowns"]:
            if d["Binds"]:
                lines.append(f"- list `{d['Binds']}`: {len(d['Values'])} choices{'' if d['Binds'].split('.')[-1] in used else ' [binding not in code]'}")
        lines.append("")

    for p in pages:
        if "Error" in p:
            lines.append(f"- not opened: {' | '.join(p['Names'])} ({p['Guid']}): {p['Error']}")

    summary = [
        "",
        "## Summary",
        f"- events on the pages: {len(all_events)}; the code never names {sum(1 for e in all_events if e not in used)};"
        f" new since FM26: {sum(1 for e in all_events if e not in old)}",
        "- pages by events the code never names:",
    ]
    for title, unused in sorted(unused_by_page.items(), key=lambda kv: -len(kv[1])):
        if unused:
            summary.append(f"  - {len(unused):3d}  {title}: {', '.join(sorted(unused))}")
    text = "\n".join(lines + summary) + "\n"
    if out:
        out.write_text(text, encoding="utf-8")
        print("\n".join(summary))
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
