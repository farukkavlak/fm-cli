#!/usr/bin/env python3
"""Draw terminal output as a manual screenshot: a dark window with a title bar.

Either runs a command in a 24-bit colour pty, or takes a frame captured by `live-capture.py`
(only the live panel is kept, from its CANLI/LIVE line). Sextant and half-block cells are drawn
as blocks, since Menlo has no glyphs for sextants.

    python3 tools/manual-shot.py --out docs/manual/img/match/radar-iso.png -- fm settings panel radar iso
    python3 tools/manual-shot.py --out live.png --frame FRAMES/frame-00300.txt --title "fm — match --play"
"""
from __future__ import annotations

import argparse
import fcntl
import importlib.util
import os
import re
import pty
import select
import struct
import termios
import time
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

_spec = importlib.util.spec_from_file_location("live_gif", Path(__file__).with_name("live-gif.py"))
lg = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(lg)

CW, CH, FONT = 9, 18, 15
PAGE, WINDOW, BAR, EDGE, DIM = (11, 14, 19), (13, 17, 23), (22, 26, 33), (48, 54, 64), (140, 148, 160)
MONO = "/System/Library/Fonts/Menlo.ttc"


def run(cmd: list[str], cols: int, rows: int, no_input: bool = False) -> str:
    env = dict(os.environ, TERM="xterm-256color", COLORTERM="truecolor")
    pid, fd = pty.fork()
    if pid == 0:
        if no_input:  # colours stay on (stdout is the pty), but no card waits for arrow keys
            os.dup2(os.open(os.devnull, os.O_RDONLY), 0)
        os.execvpe(cmd[0], cmd, env)
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", rows, cols, 0, 0))
    out = b""
    end = time.time() + 60
    while time.time() < end:
        ready, _, _ = select.select([fd], [], [], 0.5)
        if not ready:
            continue
        try:
            data = os.read(fd, 65536)
        except OSError:
            break
        if not data:
            break
        out += data
    os.waitpid(pid, 0)
    return out.decode(errors="replace")


def cells_of(text: str, panel: bool) -> list[list[tuple]]:
    # keep colour codes (SGR), drop the rest (cursor keys mode, keypad, clears)
    text = re.sub(r"\x1b(?:\[[0-9;?]*[A-Za-ln-z]|[=>])", "", text)
    if panel:
        return lg.parse_cells(text)
    saved, lg.ROWS = lg.ROWS, text.count("\n") + 1
    try:
        return lg.parse_cells(text)
    finally:
        lg.ROWS = saved


def draw(cells: list[list[tuple]], title: str, out: Path) -> None:
    while cells and all(ch == " " for ch, *_ in cells[-1]):
        cells.pop()
    while cells and all(ch == " " for ch, *_ in cells[0]):
        cells.pop(0)
    def used(row):  # cells up to the last one with a glyph or a background
        return max((i + 1 for i, (ch, _, bg, _) in enumerate(row) if ch != " " or bg != lg.BG), default=0)
    cols = max((used(r) for r in cells), default=1)
    rows = len(cells)
    pad, bar, margin = 20, 34, 30
    w, h = cols * CW + pad * 2, rows * CH + pad * 2 + bar
    img = Image.new("RGB", (w + margin * 2, h + margin * 2), PAGE)
    d = ImageDraw.Draw(img)
    x0, y0 = margin, margin
    d.rounded_rectangle([x0, y0, x0 + w, y0 + h], 12, fill=WINDOW, outline=EDGE)
    d.rounded_rectangle([x0, y0, x0 + w, y0 + bar], 12, fill=BAR, outline=EDGE)
    d.rectangle([x0 + 1, y0 + bar - 12, x0 + w - 1, y0 + bar], fill=BAR)
    d.line([x0, y0 + bar, x0 + w, y0 + bar], fill=EDGE)
    for i, c in enumerate([(255, 95, 87), (254, 188, 46), (40, 200, 64)]):
        cx, cy = x0 + 20 + i * 20, y0 + bar // 2
        d.ellipse([cx - 6, cy - 6, cx + 6, cy + 6], fill=c)
    small = ImageFont.truetype(MONO, 13)
    d.text((x0 + 82, y0 + bar // 2), title, font=small, fill=DIM, anchor="lm")
    font = ImageFont.truetype(MONO, FONT)
    bold = ImageFont.truetype(MONO, FONT, index=1)
    ox, oy = x0 + pad, y0 + bar + pad
    for r, row in enumerate(cells):
        for c, (ch, fg, bg, isbold) in enumerate(row):
            x, y = ox + c * CW, oy + r * CH
            if bg != lg.BG:
                d.rectangle([x, y, x + CW - 1, y + CH - 1], fill=bg)
            mask = lg.sextant(ch)
            if ch == "▀":
                d.rectangle([x, y, x + CW - 1, y + CH // 2 - 1], fill=fg)
                d.rectangle([x, y + CH // 2, x + CW - 1, y + CH - 1], fill=bg)
                continue
            if mask is not None:
                xs, ys = [0, CW // 2, CW], [0, CH // 3, 2 * CH // 3, CH]
                for sy in range(3):
                    for sx in range(2):
                        on = mask & (1 << (sy * 2 + sx))
                        d.rectangle([x + xs[sx], y + ys[sy], x + xs[sx + 1] - 1, y + ys[sy + 1] - 1], fill=fg if on else bg)
                continue
            if 0x2800 <= ord(ch) <= 0x28FF:
                code = ord(ch) - 0x2800
                bits = ((0x01, 0x08), (0x02, 0x10), (0x04, 0x20), (0x40, 0x80))
                for ry in range(4):
                    for rx in range(2):
                        if code & bits[ry][rx]:
                            xx, yy = x + 1 + rx * 4, y + 1 + ry * 4
                            d.rectangle([xx, yy, xx + 2, yy + 2], fill=fg)
                continue
            if ch != " ":
                d.text((x, y), ch, font=bold if isbold else font, fill=fg)
    out.parent.mkdir(parents=True, exist_ok=True)
    img.save(out)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--frame", type=Path, help="a live-capture frame instead of a command")
    ap.add_argument("--title", default=None)
    ap.add_argument("--cols", type=int, default=120)
    ap.add_argument("--rows", type=int, default=40)
    ap.add_argument("--skip", type=int, default=0, help="drop this many leading lines of command output")
    ap.add_argument("--no-input", action="store_true", help="stdin from /dev/null: a card that takes arrow keys is drawn and left")
    ap.add_argument("cmd", nargs="*")
    args = ap.parse_args()
    if args.frame:
        cells = cells_of(args.frame.read_text(errors="replace"), panel=True)
        title = args.title or "fm — match --play"
    else:
        cells = cells_of(run(args.cmd, args.cols, args.rows, args.no_input), panel=False)[args.skip:]
        title = args.title or "fm — " + " ".join(args.cmd[1:])
    draw(cells, title, args.out)
    print(args.out)


if __name__ == "__main__":
    main()
