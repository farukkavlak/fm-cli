#!/usr/bin/env python3
"""Opens every page the game's menus list and writes down the buttons on it: label, element name
and the events each sends. The label changes with the game's language; the events and the name do
not, so this is where a command that presses a button by its words finds what to press it by
instead .

Usage: tools/buttons-catalog.py [output]   (default docs/reference/buttons.txt; the game must be
between matches). Match Day › Overview is opened last: it stops the screen from being drawn for the
rest of the session.
"""
import json
import sys
import time
import urllib.parse
import urllib.request

BRIDGE = "http://127.0.0.1:7777/"
MATCH_DAY_HOME = "5fca83a7c7c29c14ab624fcb3ef707f9"


def get(route, **query):
    url = BRIDGE + route + ("?" + urllib.parse.urlencode(query) if query else "")
    with urllib.request.urlopen(url, timeout=30) as answer:
        return answer.read().decode()


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "docs/reference/buttons.txt"
    pages = json.loads(get("pages"))
    seen, order = set(), []
    for page in pages:
        key = (page["Guid"], page["Object"])
        if key in seen:
            continue
        seen.add(key)
        order.append(page)
    order.sort(key=lambda p: p["Guid"] == MATCH_DAY_HOME)

    lines = ["# Buttons on each page: label | element name | events (tools/buttons-catalog.py)", ""]
    for page in order:
        query = {"guid": page["Guid"]}
        if page["Object"] not in ("", "-1"):
            query["object"] = page["Object"]
        opened = get("open", **query)
        title = f"{page['Tab']} › {page['Page']}  ({page['Guid']}{'' if page['Object'] in ('', '-1') else ', ' + page['Object']})"
        if opened.startswith("error"):
            # A page behind a submenu needs its tab up first; it is asked for again.
            time.sleep(1.5)
            opened = get("open", **query)
        time.sleep(2.5)
        lines.append(f"## {title}")
        if opened.startswith("error"):
            lines.append(f"  (not opened: {opened.strip()})")
            lines.append("")
            continue
        rows = set()
        for control in json.loads(get("buttons", json="1")):
            if not control.get("Events"):
                continue
            rows.add(f"  {control['Label'] or '-'} | {control['Name']} | {control['Events']}")
        lines.extend(sorted(rows))
        lines.append("")
        print(f"{len(rows):4d}  {title}", file=sys.stderr)

    with open(out, "w") as f:
        f.write("\n".join(lines) + "\n")
    print(f"{out}: {len(order)} pages", file=sys.stderr)


if __name__ == "__main__":
    main()
