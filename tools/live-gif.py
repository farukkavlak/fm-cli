#!/usr/bin/env python3
"""Turn captured live-panel frames into a GIF (pixel / braille / colour / ascii).

The frames are the text files written by a pty capture of `fm match --play`; one
GIF is drawn from them at 120x40 cells, with a title strip.  A goal frame is
centred when the score changes; otherwise a slice from the middle is used.

    python3 tools/live-gif.py FRAME_DIR --style pixel --out out.gif --mode goal
"""
from __future__ import annotations

import argparse
import json
import re
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

BG = (6, 8, 12)
TEXT = (220, 230, 240)
COLS, ROWS = 120, 40
CW, CH = 6, 12
TITLE = 26
MONO = "/System/Library/Fonts/Menlo.ttc"

SGR = re.compile(r"\x1b\[([0-9;?]*)([A-Za-z])")


def palette(n: int):
    if n < 16:
        return [(0, 0, 0), (128, 0, 0), (0, 128, 0), (128, 128, 0), (0, 0, 128),
                (128, 0, 128), (0, 128, 128), (192, 192, 192), (128, 128, 128),
                (255, 0, 0), (0, 255, 0), (255, 255, 0), (0, 0, 255), (255, 0, 255),
                (0, 255, 255), (255, 255, 255)][n]
    if n >= 232:
        v = 8 + (n - 232) * 10
        return (v, v, v)
    n -= 16
    r, g, b = n // 36, (n % 36) // 6, n % 6
    steps = [0, 95, 135, 175, 215, 255]
    return (steps[r], steps[g], steps[b])


def parse_cells(text: str):
    """Yield (char, fg, bg, bold) for the panel, starting at CANLI/LIVE."""
    lines = text.replace("\r", "").split("\n")
    start = 0
    for i, line in enumerate(lines):
        plain = re.sub(r"\x1b\[[0-9;?]*[A-Za-z]", "", line)
        if ("CANLI" in plain or "LIVE" in plain) and len(plain) > 20:
            start = max(0, i - 1)
            break
    cells = []
    for line in lines[start:start + ROWS]:
        fg, bg, bold = TEXT, BG, False
        row = []
        i = 0
        while i < len(line):
            m = SGR.match(line, i)
            if m:
                params, kind = m.group(1), m.group(2)
                if kind == "m":
                    ps = [int(x) for x in params.split(";") if x]
                    if not ps:
                        fg, bg, bold = TEXT, BG, False
                    else:
                        j = 0
                        while j < len(ps):
                            p = ps[j]
                            if p == 0:
                                fg, bg, bold = TEXT, BG, False
                            elif p == 1:
                                bold = True
                            elif p == 22:
                                bold = False
                            elif p == 38 and j + 4 < len(ps) and ps[j + 1] == 2:
                                fg = tuple(ps[j + 2:j + 5]); j += 4
                            elif p == 48 and j + 4 < len(ps) and ps[j + 1] == 2:
                                bg = tuple(ps[j + 2:j + 5]); j += 4
                            elif p == 38 and j + 2 < len(ps) and ps[j + 1] == 5:
                                fg = palette(ps[j + 2]); j += 2
                            elif p == 48 and j + 2 < len(ps) and ps[j + 1] == 5:
                                bg = palette(ps[j + 2]); j += 2
                            j += 1
                i = m.end()
                continue
            row.append((line[i], fg, bg, bold))
            i += 1
        cells.append(row)
    return cells


def render(text: str, style: str) -> Image.Image:
    cells = parse_cells(text)
    img = Image.new("RGB", (COLS * CW, ROWS * CH), BG)
    d = ImageDraw.Draw(img)
    f = ImageFont.truetype(MONO, 9)
    bits = ((0x01, 0x08), (0x02, 0x10), (0x04, 0x20), (0x40, 0x80))
    for r, row in enumerate(cells):
        for c, (ch, fg, bg, bold) in enumerate(row[:COLS]):
            x, y = c * CW, r * CH
            if style == "pixel" and ch == "▀":
                d.rectangle([x, y, x + CW - 1, y + CH // 2 - 1], fill=fg)
                d.rectangle([x, y + CH // 2, x + CW - 1, y + CH - 1], fill=bg)
                continue
            if 0x2800 <= ord(ch) <= 0x28FF:
                d.rectangle([x, y, x + CW - 1, y + CH - 1], fill=bg)
                code = ord(ch) - 0x2800
                for ry in range(4):
                    for rx in range(2):
                        if code & bits[ry][rx]:
                            xx, yy = x + rx * 3 + 1, y + ry * 3 + 1
                            d.rectangle([xx, yy, xx + 1, yy + 1], fill=fg)
                continue
            d.rectangle([x, y, x + CW - 1, y + CH - 1], fill=bg)
            if ch != " ":
                d.text((x, y), ch, font=f, fill=fg)
    return img


def header(s: str):
    m = re.search(r"(?:CANLI|LIVE).*?(\d+)\s*-\s*(\d+).*?(\d+:\d+)", re.sub(r"\x1b\[[0-9;?]*[A-Za-z]", "", s))
    return (int(m.group(1)), int(m.group(2)), m.group(3)) if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("frames", type=Path)
    ap.add_argument("--style", required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--mode", choices=("flow", "goal"), default="goal")
    ap.add_argument("--count", type=int, default=30)
    args = ap.parse_args()

    files = sorted(args.frames.glob("frame-*.txt"))
    parsed = []
    prev = None
    goal_at = None
    for i, f in enumerate(files):
        text = f.read_text(encoding="utf-8", errors="replace")
        h = header(text)
        if not h:
            continue
        score = (h[0], h[1])
        parsed.append((f, text, h))
        if goal_at is None and prev is not None and sum(score) > sum(prev):
            goal_at = len(parsed) - 1
        prev = score
    if not parsed:
        raise SystemExit("no panel frames")
    if args.mode == "goal" and goal_at is not None:
        start = max(0, goal_at - 10)
    else:
        start = max(0, len(parsed) // 3)
    chosen = parsed[start:start + args.count]
    images = []
    for f, text, h in chosen:
        art = render(text, args.style)
        canvas = Image.new("RGB", (art.width, art.height + TITLE), BG)
        canvas.paste(art, (0, TITLE))
        d = ImageDraw.Draw(canvas)
        d.text((10, 5), f"fm-cli  ·  live panel  ·  {args.style}  ·  {h[0]}-{h[1]}  {h[2]}",
               font=ImageFont.truetype(MONO, 14), fill=TEXT)
        images.append(canvas)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(args.out, save_all=True, append_images=images[1:], duration=300, loop=0, optimize=True, disposal=2)
    print(args.out, len(images), "frames", args.out.stat().st_size, "bytes", "goal_at", goal_at)


if __name__ == "__main__":
    main()
