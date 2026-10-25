#!/usr/bin/env python3
"""
Render a captured fm-cli radar frame as terminal-style concept art.

The frames come from the plugin's /radar route (real positions from the game's
match simulation).  Three styles are tried:

  pixel      TRUE 24-bit colour, Unicode half-blocks (each cell is two pixels)
  braille    2x4 dots per cell, coloured per cell
  isometric  a 2.5D concept: the same positions projected and lit

The PNGs are presentation renders; the .ansi files are the actual terminal
output (pixel and braille).  Nothing here talks to the game.

    python3 tools/radar-styles.py FRAME.json [--out DIR]
"""

from __future__ import annotations

import argparse
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# --------------------------------------------------------------------------- data


@dataclass
class Player:
    number: int
    x: float
    y: float
    kit: tuple[int, int, int]
    text: tuple[int, int, int]
    team: int
    sub: bool
    rotation: float
    height: float


@dataclass
class Frame:
    players: list[Player]
    ball: tuple[float, float, float]
    state: dict
    ball_trail: list[tuple[float, float]]

    @property
    def starters(self) -> list[Player]:
        return [p for p in self.players if not p.sub and 0 <= p.x <= 1 and 0 <= p.y <= 1]

    @property
    def minute(self) -> str:
        return self.state.get("MatchTime") or "00:00"

    @property
    def score(self) -> tuple[int, int]:
        return int(self.state.get("HomeScore") or 0), int(self.state.get("AwayScore") or 0)

    @property
    def human_is_home(self) -> bool:
        return str(self.state.get("HumanIsHome", "False")).lower() == "true"

    @property
    def period(self) -> str:
        return {
            "FirstHalf": "İlk yarı",
            "SecondHalf": "İkinci yarı",
            "ExtraTimeFirstHalf": "Uzatma 1",
            "ExtraTimeSecondHalf": "Uzatma 2",
        }.get(str(self.state.get("Period")), "")

    def team(self, index: int) -> int:
        return index


def rgb(hex_text: str) -> tuple[int, int, int]:
    value = (hex_text or "808080").lstrip("#")
    if len(value) != 6:
        return (128, 128, 128)
    return tuple(int(value[i : i + 2], 16) for i in (0, 2, 4))  # type: ignore[return-value]


def load_frame(path: Path, trail: int = 6) -> Frame:
    raw = json.loads(path.read_text())
    radar = raw.get("radar") or raw
    state = raw.get("state") or {}

    players: list[Player] = []
    team = -1
    after_subs = True
    for p in radar.get("players", []):
        sub = bool(p.get("sub"))
        if not sub and after_subs:
            team += 1
            after_subs = False
        if sub:
            after_subs = True
        players.append(
            Player(
                number=int(p.get("n") or 0),
                x=float(p.get("x") or 0),
                y=float(p.get("y") or 0),
                kit=rgb(p.get("kit") or ""),
                text=rgb(p.get("txt") or ""),
                team=max(0, team),
                sub=sub,
                rotation=float(p.get("rot") or 0),
                height=float(p.get("h") or 0),
            )
        )

    ball = radar.get("ball") or {}
    bx, by, bh = float(ball.get("x") or 0), float(ball.get("y") or 0), float(ball.get("h") or 0)

    trail_points: list[tuple[float, float]] = []
    match = list(path.parent.glob("frame-*.json"))
    current = path.name
    if current.startswith("frame-") and trail > 0:
        ordered = sorted(match, key=lambda p: p.name)
        try:
            at = ordered.index(path)
        except ValueError:
            at = -1
        for prev in ordered[max(0, at - trail) : at]:
            try:
                pr = json.loads(prev.read_text()).get("radar", {})
                b = pr.get("ball") or {}
                if float(b.get("x") or 0) or float(b.get("y") or 0):
                    trail_points.append((float(b["x"]), float(b["y"])))
            except Exception:
                pass

    return Frame(players, (bx, by, bh), state, trail_points)


# --------------------------------------------------------------------------- colour


def luminance(c: tuple[int, int, int]) -> float:
    return (0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]) / 255


def mix(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))  # type: ignore[return-value]


def readable(c: tuple[int, int, int], floor: float = 0.55) -> tuple[int, int, int]:
    """Lift very dark kits so they can be seen on a dark terminal."""
    lum = luminance(c)
    if lum >= floor:
        return c
    return mix(c, (255, 255, 255), (floor - lum) / (1 - lum) * 0.9)


def outlined(c: tuple[int, int, int]) -> tuple[int, int, int]:
    return (235, 240, 245) if luminance(c) < 0.18 else (12, 16, 20)


def outfield_colour(frame: Frame, team: int) -> tuple[int, int, int]:
    colours = [p.kit for p in frame.players if p.team == team and not p.sub and luminance(p.kit) > 0.02]
    if not colours:
        colours = [p.kit for p in frame.players if p.team == team and not p.sub]
    return max(set(colours), key=colours.count) if colours else (200, 200, 200)


def colour_distance(a: tuple[int, int, int], b: tuple[int, int, int]) -> float:
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def defending_side(frame: Frame, team: int) -> str:
    """Which goal a team defends, read from its goalkeeper rather than from the period."""
    outfield = outfield_colour(frame, team)
    players = [p for p in frame.starters if p.team == team]
    if not players:
        return "right"
    keepers = [p for p in players if colour_distance(p.kit, outfield) > 70]
    keeper = min(keepers or players, key=lambda p: min(p.x, 1 - p.x))
    return "right" if keeper.x > 0.5 else "left"


# --------------------------------------------------------------------------- tiny digits

LETTERS = {
    "E": ("11111", "10000", "11110", "10000", "11111"),
    "M": ("10001", "11011", "10101", "10001", "10001"),
    "N": ("10001", "11001", "10101", "10011", "10001"),
    "P": ("11110", "10001", "11110", "10000", "10000"),
    "V": ("10001", "10001", "01010", "01010", "00100"),
}

DIGITS = {
    "0": ("111", "101", "101", "101", "111"),
    "1": ("010", "110", "010", "010", "111"),
    "2": ("111", "001", "111", "100", "111"),
    "3": ("111", "001", "111", "001", "111"),
    "4": ("101", "101", "111", "001", "001"),
    "5": ("111", "100", "111", "001", "111"),
    "6": ("111", "100", "111", "101", "111"),
    "7": ("111", "001", "010", "010", "010"),
    "8": ("111", "101", "111", "101", "111"),
    "9": ("111", "101", "111", "001", "111"),
}


def draw_number(d: ImageDraw.ImageDraw, number: int, x: int, y: int, colour, scale: int = 1):
    text = str(number)
    width = (3 * len(text) + len(text) - 1) * scale
    cx = x - width // 2
    for ch in text:
        glyph = DIGITS.get(ch)
        if glyph:
            for row, line in enumerate(glyph):
                for col, bit in enumerate(line):
                    if bit == "1":
                        d.rectangle(
                            [cx + col * scale, y + row * scale, cx + col * scale + scale - 1, y + row * scale + scale - 1],
                            fill=colour,
                        )
        cx += 4 * scale


def draw_word(d: ImageDraw.ImageDraw, word: str, x: int, y: int, colour, scale: int = 1):
    cx = x
    for ch in word:
        glyph = LETTERS.get(ch)
        if glyph:
            for row, line in enumerate(glyph):
                for col, bit in enumerate(line):
                    if bit == "1":
                        d.rectangle(
                            [cx + col * scale, y + row * scale, cx + col * scale + scale - 1, y + row * scale + scale - 1],
                            fill=colour,
                        )
        cx += 6 * scale


# --------------------------------------------------------------------------- chrome

BG = (10, 14, 19)
WINDOW = (14, 19, 26)
WINDOW_2 = (17, 23, 31)
BORDER = (39, 50, 64)
TEXT = (222, 230, 240)
DIM = (128, 143, 162)
ACCENT = (86, 214, 162)
MONO = "/System/Library/Fonts/Menlo.ttc"


def font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(MONO, size)


def terminal_chrome(
    content: Image.Image,
    title: str,
    subtitle: str,
    footer: str,
    legend: list[tuple[str, tuple[int, int, int]]],
    pad: int = 34,
    footer_lines: Iterable[str] = (),
) -> Image.Image:
    cw, ch = content.size
    width = cw + pad * 2
    top = 112
    bottom = 118
    height = ch + top + bottom
    img = Image.new("RGB", (width, height), BG)
    d = ImageDraw.Draw(img)

    # window
    d.rounded_rectangle([12, 12, width - 12, height - 12], radius=18, fill=WINDOW, outline=BORDER, width=2)
    d.rounded_rectangle([12, 12, width - 12, 58], radius=18, fill=WINDOW_2)
    d.rectangle([12, 40, width - 12, 58], fill=WINDOW_2)
    for i, colour in enumerate(((255, 95, 86), (255, 189, 46), (39, 201, 63))):
        d.ellipse([30 + i * 22, 28, 42 + i * 22, 40], fill=colour)

    f_title = font(21)
    f_small = font(16)
    f_tiny = font(14)
    d.text((116, 26), title, font=f_title, fill=TEXT)
    tw = d.textlength(title, font=f_title)
    d.text((116 + tw + 14, 31), subtitle, font=f_small, fill=DIM)

    # content
    img.paste(content, (pad, top))

    # footer
    y = top + ch + 20
    d.line([(pad, y - 12), (width - pad, y - 12)], fill=BORDER, width=1)
    x = pad
    for label, colour in legend:
        d.ellipse([x, y + 4, x + 12, y + 16], fill=colour, outline=(0, 0, 0), width=1)
        d.text((x + 20, y), label, font=f_small, fill=DIM)
        x += d.textlength(label, font=f_small) + 56

    d.text((width - pad - d.textlength(footer, font=f_tiny), y + 1), footer, font=f_tiny, fill=DIM)

    yy = y + 34
    for line in footer_lines:
        d.text((pad, yy), line, font=f_small, fill=DIM)
        yy += 24
    return img


def match_header(frame: Frame, home: str, away: str, competition: str) -> str:
    hs, as_ = frame.score
    return f"{home} {hs}-{as_} {away}   ·   {frame.minute}   ·   {competition}"


# --------------------------------------------------------------------------- 1. pixel


def render_pixel(frame: Frame, width: int = 120, home: str = "Venezia", away: str = "Empoli") -> Image.Image:
    rows = round(width * 76 / 120)
    img = Image.new("RGB", (width, rows), (24, 78, 42))
    d = ImageDraw.Draw(img)

    # mown stripes
    for x in range(0, width, 12):
        shade = (28, 88, 48) if (x // 12) % 2 else (25, 80, 44)
        d.rectangle([x, 0, min(width, x + 12) - 1, rows - 1], fill=shade)

    m = 9
    x0, y0, x1, y1 = m, m, width - m, rows - m
    line = (228, 238, 232)
    shadow = (18, 58, 32)

    def box(fx0: float, fy0: float, fx1: float, fy1: float):
        d.rectangle(
            [
                round(x0 + fx0 * (x1 - x0)),
                round(y0 + fy0 * (y1 - y0)),
                round(x0 + fx1 * (x1 - x0)),
                round(y0 + fy1 * (y1 - y0)),
            ],
            outline=line,
        )

    # pitch and markings
    d.rectangle([x0, y0, x1, y1], outline=line)
    midx = (x0 + x1) // 2
    d.line([(midx, y0), (midx, y1)], fill=line)
    cy = (y0 + y1) // 2
    r = int((y1 - y0) * 0.145)
    d.ellipse([midx - r, cy - r, midx + r, cy + r], outline=line)
    d.rectangle([midx - 1, cy - 1, midx + 1, cy + 1], fill=line)
    box(0, 0.205, 0.157, 0.795)
    box(0.843, 0.205, 1, 0.795)
    box(0, 0.365, 0.052, 0.635)
    box(0.948, 0.365, 1, 0.635)
    for sx, sy in ((0.105, 0.5), (0.895, 0.5)):
        px, py = round(x0 + sx * (x1 - x0)), round(y0 + sy * (y1 - y0))
        d.rectangle([px - 1, py - 1, px + 1, py + 1], fill=line)
    # goals
    d.rectangle([x0 - 2, round(y0 + 0.42 * (y1 - y0)), x0, round(y0 + 0.58 * (y1 - y0))], outline=line)
    d.rectangle([x1, round(y0 + 0.42 * (y1 - y0)), x1 + 2, round(y0 + 0.58 * (y1 - y0))], outline=line)

    # ball trail
    if frame.ball_trail:
        for i, (tx, ty) in enumerate(frame.ball_trail):
            px = round(x0 + tx * (x1 - x0))
            py = round(y0 + ty * (y1 - y0))
            fade = (190, 170, 70) if i == len(frame.ball_trail) - 1 else (120, 110, 55)
            d.rectangle([px, py, px, py], fill=fade)

    # players, far (top) to near (bottom) is irrelevant here, but numbers stay on top
    for p in sorted(frame.starters, key=lambda p: p.y):
        px = round(x0 + p.x * (x1 - x0))
        py = round(y0 + p.y * (y1 - y0))
        kit = p.kit
        edge = outlined(kit)
        # shadow
        d.ellipse([px - 3, py - 2, px + 3, py + 1], fill=shadow)
        # body and legs
        d.rectangle([px - 2, py - 7, px + 2, py - 3], fill=kit, outline=edge)
        d.rectangle([px - 2, py - 3, px - 1, py], fill=mix(kit, (0, 0, 0), 0.45))
        d.rectangle([px + 1, py - 3, px + 2, py], fill=mix(kit, (0, 0, 0), 0.45))
        # head
        d.ellipse([px - 1, py - 10, px + 1, py - 7], fill=(214, 168, 128), outline=(70, 45, 32))
        # number
        num = str(p.number)
        if len(num) <= 2:
            draw_number(d, p.number, px, py - 7, p.text, 1)

    # ball last, so it is never hidden
    bx = round(x0 + frame.ball[0] * (x1 - x0))
    by = round(y0 + frame.ball[1] * (y1 - y0))
    lift = min(4, round(frame.ball[2] * 2))
    d.ellipse([bx - 2, by - 1, bx + 2, by + 1], fill=(15, 50, 26))
    d.ellipse([bx - 1, by - 1 - lift, bx + 1, by + 1 - lift], fill=(250, 250, 240), outline=(30, 30, 30))

    # team abbreviations in the top margin, at the end each side defends
    home_left = defending_side(frame, 0) == "left"
    left_name = home if home_left else away
    right_name = away if home_left else home
    draw_word(d, left_name[:3].upper(), x0 + 1, 1, (180, 210, 255))
    draw_word(d, right_name[:3].upper(), x1 - 18, 1, (220, 220, 220))
    return img


def pixel_ansi(frame: Frame, width: int = 120, home: str = "Venezia", away: str = "Empoli") -> str:
    img = render_pixel(frame, width, home, away)
    px = img.load()
    w, h = img.size
    out: list[str] = []
    for y in range(0, h - 1, 2):
        line = []
        for x in range(w):
            top = px[x, y]
            bottom = px[x, y + 1]
            line.append(
                f"\x1b[38;2;{top[0]};{top[1]};{top[2]}m"
                f"\x1b[48;2;{bottom[0]};{bottom[1]};{bottom[2]}m▀"
            )
        out.append("".join(line) + "\x1b[0m")
    return "\n".join(out) + "\n"


# --------------------------------------------------------------------------- 2. braille

BRAILLE_BITS = ((0, 3), (1, 4), (2, 5), (6, 7))
PITCH_GRASS = (8, 28, 17)


def _dot_line(dots, x0, y0, x1, y1, colour, priority=1):
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
    err = dx + dy
    while True:
        _dot(dots, x0, y0, colour, priority)
        if x0 == x1 and y0 == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


def _dot(dots, x, y, colour, priority):
    if 0 <= y < len(dots) and 0 <= x < len(dots[0]):
        old = dots[y][x]
        if old is None or old[1] <= priority:
            dots[y][x] = (colour, priority)


def render_braille(frame: Frame, cells_w: int = 120, home: str = "Venezia", away: str = "Empoli") -> tuple[Image.Image, str]:
    # 2x4 dots per cell: 120 cells wide is 240 dots, so 152 dots tall is 38 cells.
    cells_h = round(cells_w * 152 / 480)
    w, h = cells_w * 2, cells_h * 4
    dots: list[list[tuple[tuple[int, int, int], int] | None]] = [[None] * w for _ in range(h)]
    m = 8
    x0, y0, x1, y1 = m, m, w - m - 1, h - m - 1
    line_colour = (212, 232, 220)

    def put_line(fx0, fy0, fx1, fy1, colour=line_colour, priority=1):
        _dot_line(
            dots,
            round(x0 + fx0 * (x1 - x0)),
            round(y0 + fy0 * (y1 - y0)),
            round(x0 + fx1 * (x1 - x0)),
            round(y0 + fy1 * (y1 - y0)),
            colour,
            priority,
        )

    # boundary, halfway and boxes
    for a, b, c, d in (
        (0, 0, 1, 0), (1, 0, 1, 1), (1, 1, 0, 1), (0, 1, 0, 0),
        (0.5, 0, 0.5, 0.5), (0.5, 1, 0.5, 0.5),
        (0, 0.205, 0.157, 0.205), (0.157, 0.205, 0.157, 0.795), (0.157, 0.795, 0, 0.795),
        (1, 0.205, 0.843, 0.205), (0.843, 0.205, 0.843, 0.795), (0.843, 0.795, 1, 0.795),
        (0, 0.365, 0.052, 0.365), (0.052, 0.365, 0.052, 0.635), (0.052, 0.635, 0, 0.635),
        (1, 0.365, 0.948, 0.365), (0.948, 0.365, 0.948, 0.635), (0.948, 0.635, 1, 0.635),
    ):
        put_line(a, b, c, d)

    # centre circle
    cx, cy, rx, ry = x0 + (x1 - x0) / 2, y0 + (y1 - y0) / 2, (y1 - y0) * 0.145, (y1 - y0) * 0.145
    prev = None
    for step in range(129):
        a = 2 * math.pi * step / 128
        pt = (round(cx + math.cos(a) * rx), round(cy + math.sin(a) * ry))
        if prev:
            _dot_line(dots, prev[0], prev[1], pt[0], pt[1], line_colour, 1)
        prev = pt

    # penalty spots
    for fx in (0.105, 0.895):
        px, py = round(x0 + fx * (x1 - x0)), round(y0 + 0.5 * (y1 - y0))
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                _dot(dots, px + dx, py + dy, line_colour, 1)

    # end labels in the margin, at the goal each side defends
    home_left = defending_side(frame, 0) == "left"
    left_name = home if home_left else away
    right_name = away if home_left else home
    for word, at in ((left_name[:3].upper(), 2), (right_name[:3].upper(), w - 20)):
        for i, ch in enumerate(word):
            glyph = LETTERS.get(ch)
            if not glyph:
                continue
            for row, line in enumerate(glyph):
                for col, bit in enumerate(line):
                    if bit == "1":
                        _dot(dots, at + i * 6 + col, 1 + row, (92, 124, 108), 0.5)

    # players as lit dots; lift dark kits for a dark terminal
    for p in frame.starters:
        px = x0 + p.x * (x1 - x0)
        py = y0 + p.y * (y1 - y0)
        colour = readable(p.kit, 0.58)
        radius = 3
        for dy in range(-radius, radius + 1):
            for dx in range(-radius, radius + 1):
                if dx * dx + dy * dy <= radius * radius:
                    _dot(dots, int(math.floor(px + dx + 0.5)), int(math.floor(py + dy + 0.5)), colour, 2)

    # ball
    bx = x0 + frame.ball[0] * (x1 - x0)
    by = y0 + frame.ball[1] * (y1 - y0)
    lift = min(5, round(frame.ball[2] * 2))
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            if dx * dx + dy * dy <= 4:
                _dot(
                    dots,
                    int(math.floor(bx + dx + 0.5)),
                    int(math.floor(by + dy + 0.5 - lift)),
                    (255, 250, 210),
                    3,
                )

    # cells
    cell_w, cell_h = cells_w, cells_h
    glyphs: list[list[int]] = [[0] * cell_w for _ in range(cell_h)]
    colours: list[list[tuple[int, int, int]]] = [[(120, 150, 130)] * cell_w for _ in range(cell_h)]
    priority_grid: list[list[int]] = [[-1] * cell_w for _ in range(cell_h)]
    for cyi in range(cell_h):
        for cxi in range(cell_w):
            bits = 0
            best = None
            best_priority = -1
            for ry in range(4):
                for rx in range(2):
                    dot = dots[cyi * 4 + ry][cxi * 2 + rx]
                    if dot:
                        bits |= 1 << BRAILLE_BITS[ry][rx]
                        if dot[1] > best_priority:
                            best_priority = dot[1]
                            best = dot[0]
            glyphs[cyi][cxi] = 0x2800 + bits
            if best is not None:
                colours[cyi][cxi] = best
            priority_grid[cyi][cxi] = best_priority

    # ANSI
    ansi = []
    for cyi in range(cell_h):
        line = []
        for cxi in range(cell_w):
            stripe = (7, 26, 16) if (cxi // 10) % 2 else (8, 30, 18)
            fg = colours[cyi][cxi]
            line.append(
                f"\x1b[38;2;{fg[0]};{fg[1]};{fg[2]}m"
                f"\x1b[48;2;{stripe[0]};{stripe[1]};{stripe[2]}m"
                f"{chr(glyphs[cyi][cxi])}"
            )
        ansi.append("".join(line) + "\x1b[0m")

    # presentation image: a dot-matrix panel
    scale = 7
    cw, chh = 2 * scale, 4 * scale
    img = Image.new("RGB", (cell_w * cw, cell_h * chh), BG)
    d = ImageDraw.Draw(img)
    for cyi in range(cell_h):
        for cxi in range(cell_w):
            stripe = (7, 26, 16) if (cxi // 10) % 2 else (8, 30, 18)
            d.rectangle(
                [cxi * cw, cyi * chh, (cxi + 1) * cw - 1, (cyi + 1) * chh - 1],
                fill=stripe,
            )
            dot = dots[cyi * 4 : cyi * 4 + 4]
            bits = glyphs[cyi][cxi] - 0x2800
            for ry in range(4):
                for rx in range(2):
                    bit = BRAILLE_BITS[ry][rx]
                    if bits & (1 << bit):
                        x = cxi * cw + rx * scale
                        y = cyi * chh + ry * scale
                        colour = colours[cyi][cxi]
                        glow = mix(colour, stripe, 0.55)
                        d.ellipse([x + 1, y + 1, x + scale - 1, y + scale - 1], fill=glow)
                        d.ellipse([x + 2, y + 2, x + scale - 2, y + scale - 2], fill=colour)
    # grid
    for cxi in range(0, cell_w * cw, cw):
        d.line([(cxi, 0), (cxi, img.height)], fill=(4, 16, 10))
    for cyi in range(0, cell_h * chh, chh):
        d.line([(0, cyi), (img.width, cyi)], fill=(4, 16, 10))
    return img, "\n".join(ansi) + "\n"


# --------------------------------------------------------------------------- 3. isometric


def _iso_projector(world_w=105.0, world_h=68.0, width=1500, height=940, margin=95):
    cos30, sin30 = math.cos(math.radians(30)), math.sin(math.radians(30))

    def raw(x, y, z=0.0):
        return (x - y) * cos30, (x + y) * sin30 - z

    corners = [raw(0, 0), raw(world_w, 0), raw(world_w, world_h), raw(0, world_h)]
    min_x = min(c[0] for c in corners)
    max_x = max(c[0] for c in corners)
    min_y = min(c[1] for c in corners)
    max_y = max(c[1] for c in corners)
    scale = min((width - margin * 2) / (max_x - min_x), (height - margin * 2) / (max_y - min_y))
    ox = (width - (max_x - min_x) * scale) / 2 - min_x * scale
    oy = (height - (max_y - min_y) * scale) / 2 - min_y * scale

    def project(x, y, z=0.0):
        rx, ry = raw(x, y, z)
        return ox + rx * scale, oy + ry * scale

    return project, scale


def _poly(d, points, fill=None, outline=None, width=1):
    d.polygon(points, fill=fill)
    if outline:
        d.line(points + [points[0]], fill=outline, width=width)


def render_isometric(frame: Frame, home: str, away: str, competition: str) -> Image.Image:
    width, height = 1560, 1000
    img = Image.new("RGB", (width, height), (7, 11, 15))
    d = ImageDraw.Draw(img, "RGBA")

    project, scale = _iso_projector(width=width, height=height, margin=90)
    P = project
    W, H = 105.0, 68.0
    zline = 0.03

    # dark stadium field behind the pitch
    glow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([-120, 120, width + 120, height + 240], fill=(24, 92, 55, 100))
    img = Image.alpha_composite(img.convert("RGBA"), glow).convert("RGB")
    d = ImageDraw.Draw(img, "RGBA")

    # mown bands, depth-shaded
    bands = 20
    for i in range(bands):
        x0, x1 = W * i / bands, W * (i + 1) / bands
        base = (30, 96, 53) if i % 2 == 0 else (27, 86, 48)
        _poly(
            d,
            [P(x0, 0), P(x1, 0), P(x1, H), P(x0, H)],
            fill=base,
        )

    # subtle texture
    rng = np.random.default_rng(7)
    for _ in range(2400):
        x, y = rng.uniform(0, W), rng.uniform(0, H)
        sx, sy = P(x, y)
        if rng.random() < 0.5:
            d.point((sx, sy), fill=(20, 66, 36))

    line = (224, 236, 228, 230)
    lw = max(2, round(scale * 0.10))

    def line3(a, b, colour=line, width=lw):
        d.line([P(*a, zline), P(*b, zline)], fill=colour, width=width)

    def circle3(cx, cy, r, colour=line):
        pts = []
        for step in range(97):
            a = 2 * math.pi * step / 96
            pts.append(P(cx + math.cos(a) * r, cy + math.sin(a) * r, zline))
        d.line(pts, fill=colour, width=lw)

    # markings
    line3((0, 0), (W, 0)); line3((W, 0), (W, H)); line3((W, H), (0, H)); line3((0, H), (0, 0))
    line3((W / 2, 0), (W / 2, H))
    circle3(W / 2, H / 2, 9.15)
    for side in (0, 1):
        xin = 0.0 if side == 0 else W
        xout = 16.5 if side == 0 else W - 16.5
        y0, y1 = (H - 40.32) / 2, (H + 40.32) / 2
        line3((xin, y0), (xout, y0)); line3((xout, y0), (xout, y1)); line3((xout, y1), (xin, y1))
        x6 = 5.5 if side == 0 else W - 5.5
        y6, y7 = (H - 18.32) / 2, (H + 18.32) / 2
        line3((xin, y6), (x6, y6)); line3((x6, y6), (x6, y7)); line3((x6, y7), (xin, y7))
        spot = 11.0 if side == 0 else W - 11.0
        px, py = P(spot, H / 2, zline)
        d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=(206, 220, 210, 210))
        # penalty arc
        pts = []
        for step in range(41):
            a = math.radians(-53 + step * 106 / 40)
            wx, wy = spot + math.cos(a) * 9.15 * (1 if side == 0 else -1), H / 2 + math.sin(a) * 9.15
            if 0 <= wy <= H:
                pts.append(P(wx, wy, zline))
        if len(pts) > 1:
            d.line(pts, fill=line, width=lw)

    # goals with nets
    for side in (0, 1):
        x = 0.0 if side == 0 else W
        y0, y1 = H / 2 - 3.66, H / 2 + 3.66
        h = 2.44
        post = (235, 240, 238, 245)
        for y in (y0, y1):
            d.line([P(x, y, 0), P(x, y, h)], fill=post, width=max(3, lw + 1))
        d.line([P(x, y0, h), P(x, y1, h)], fill=post, width=max(3, lw + 1))
        # net
        back = -1.9 if side == 0 else 1.9
        for step in range(1, 6):
            yy = y0 + (y1 - y0) * step / 6
            d.line([P(x, yy, 0), P(x + back, yy, h * 0.72)], fill=(190, 205, 198, 50), width=1)
        for step in range(1, 5):
            zz = h * step / 5
            d.line([P(x, y0, zz), P(x + back, y0, zz)], fill=(190, 205, 198, 45), width=1)
            d.line([P(x, y1, zz), P(x + back, y1, zz)], fill=(190, 205, 198, 45), width=1)
        d.line([P(x + back, y0, 0), P(x + back, y1, 0)], fill=(190, 205, 198, 55), width=1)

    # players, far to near
    def draw_player(p: Player):
        sx, sy = P(p.x * W, p.y * H, 0)
        kit = p.kit
        edge = outlined(kit)
        # shadow
        sh = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        sd = ImageDraw.Draw(sh)
        sd.ellipse([sx - 13, sy - 6, sx + 13, sy + 6], fill=(0, 0, 0, 120))
        nonlocal img
        img = Image.alpha_composite(img.convert("RGBA"), sh).convert("RGB")
        d = ImageDraw.Draw(img, "RGBA")

        top_y = sy - scale * 1.9
        body_w = max(8, round(scale * 0.95))
        body_top = top_y + scale * 0.65
        body_bottom = sy - scale * 0.45
        # legs
        leg_w = max(3, body_w // 3)
        d.rectangle([sx - body_w // 2, body_bottom - 2, sx - body_w // 2 + leg_w, sy], fill=(22, 26, 31))
        d.rectangle([sx + body_w // 2 - leg_w, body_bottom - 2, sx + body_w // 2, sy], fill=(22, 26, 31))
        # shirt
        d.rounded_rectangle(
            [sx - body_w // 2, body_top, sx + body_w // 2, body_bottom],
            radius=max(2, body_w // 4),
            fill=kit,
            outline=edge,
            width=2,
        )
        # shorts
        d.rectangle([sx - body_w // 2 + 1, body_bottom - max(3, body_w // 3), sx + body_w // 2 - 1, body_bottom], fill=mix(kit, (0, 0, 0), 0.55))
        # head
        head_r = max(4, round(scale * 0.36))
        head_y = body_top - head_r + 1
        d.ellipse([sx - head_r, head_y - head_r, sx + head_r, head_y + head_r], fill=(205, 155, 116), outline=(58, 38, 28), width=1)
        # hair
        d.arc([sx - head_r, head_y - head_r, sx + head_r, head_y + head_r], 190, 350, fill=(45, 32, 24), width=2)
        # number
        draw_number(
            d,
            p.number,
            sx,
            round(body_top + (body_bottom - body_top) / 2 - 4 * max(1, body_w // 9)),
            p.text if luminance(p.text) > 0.2 else (250, 250, 250),
            max(1, body_w // 9),
        )

    for p in sorted(frame.starters, key=lambda p: p.x * W + p.y * H):
        draw_player(p)

    # ball trail, then the ball itself
    if frame.ball_trail:
        overlay = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        od = ImageDraw.Draw(overlay)
        for i, (tx, ty) in enumerate(frame.ball_trail):
            sx, sy = P(tx * W, ty * H, 0)
            alpha = 30 + int(80 * (i + 1) / len(frame.ball_trail))
            od.ellipse([sx - 3, sy - 2, sx + 3, sy + 2], fill=(255, 246, 190, alpha))
        img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")
        d = ImageDraw.Draw(img, "RGBA")

    bx, by = P(frame.ball[0] * W, frame.ball[1] * H, 0)
    lift = min(scale * 0.55, frame.ball[2] * scale * 0.9)
    # shadow
    sh = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.ellipse([bx - 8, by - 3, bx + 8, by + 3], fill=(0, 0, 0, 130))
    img = Image.alpha_composite(img.convert("RGBA"), sh).convert("RGB")
    d = ImageDraw.Draw(img, "RGBA")
    # a soft glow so the ball reads at a glance
    glow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for ring in range(4, 0, -1):
        r = 9 + ring * 4
        gd.ellipse([bx - r, by - r - lift, bx + r, by + r - lift], fill=(255, 244, 190, 16))
    img = Image.alpha_composite(img.convert("RGBA"), glow).convert("RGB")
    d = ImageDraw.Draw(img, "RGBA")
    br = max(5, round(scale * 0.34))
    d.ellipse([bx - br, by - br - lift, bx + br, by + br - lift], fill=(252, 252, 244), outline=(28, 30, 30), width=2)
    d.ellipse([bx - br + 2, by - br + 2 - lift, bx + br - 3, by + br - 3 - lift], fill=(255, 255, 252))

    # vignette + scanlines
    arr = np.asarray(img).astype(np.float32)
    yy, xx = np.mgrid[0:height, 0:width]
    vignette = np.clip(1.15 - 0.55 * (((xx - width / 2) / (width * 0.72)) ** 2 + ((yy - height / 2) / (height * 0.72)) ** 2), 0.45, 1.0)
    arr *= vignette[..., None]
    arr[::3, :, :] *= 0.94
    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    img = img.filter(ImageFilter.GaussianBlur(0.35))

    # broadcast card
    overlay = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    card = [32, 32, 510, 146]
    od.rounded_rectangle(card, radius=16, fill=(7, 12, 17, 210), outline=(48, 62, 78, 220), width=2)
    hs, as_ = frame.score
    f_team = font(27)
    f_score = font(38)
    f_small = font(15)
    f_tiny = font(13)
    od.text((54, 48), home.upper(), font=f_team, fill=(235, 240, 245))
    od.text((54, 92), away.upper(), font=f_team, fill=(180, 205, 235))
    od.text((430, 52), str(hs), font=f_score, fill=(255, 255, 255))
    od.text((430, 96), str(as_), font=f_score, fill=(255, 255, 255))
    od.text((54, 162), f"{competition}   ·   {frame.minute}   ·   {frame.period}", font=f_small, fill=(140, 158, 178))
    od.text((width - 300, 40), "CANLI RADAR", font=f_tiny, fill=ACCENT)
    od.text((width - 300, 60), "izometrik 2.5D · konsept", font=f_tiny, fill=(120, 138, 158))

    # legend
    ven = outfield_colour(frame, 0)
    emp = outfield_colour(frame, 1)
    od.rounded_rectangle([32, height - 74, 420, height - 30], radius=12, fill=(7, 12, 17, 190), outline=(40, 52, 66, 200))
    od.ellipse([50, height - 60, 62, height - 48], fill=ven, outline=(230, 235, 240), width=1)
    od.text((70, height - 64), home, font=f_small, fill=(170, 184, 200))
    od.ellipse([190, height - 60, 202, height - 48], fill=emp, outline=(230, 235, 240), width=1)
    od.text((210, height - 64), away, font=f_small, fill=(170, 184, 200))
    od.ellipse([330, height - 60, 342, height - 48], fill=(250, 250, 238), outline=(40, 40, 40), width=1)
    od.text((350, height - 64), "top", font=f_small, fill=(170, 184, 200))

    img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")
    return img


# --------------------------------------------------------------------------- output


def save_png(img: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path, "PNG", optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("frame", type=Path)
    parser.add_argument("--out", type=Path, default=Path("docs/experiments/radar-styles"))
    parser.add_argument("--home", default="Venezia")
    parser.add_argument("--away", default="Empoli")
    parser.add_argument("--competition", default="Serie BKT")
    parser.add_argument("--pixel-width", type=int, default=120)
    parser.add_argument("--braille-width", type=int, default=120)
    parser.add_argument("--trail", type=int, default=6)
    parser.add_argument("--only", choices=("pixel", "braille", "iso"), action="append")
    args = parser.parse_args()

    frame = load_frame(args.frame, trail=args.trail)
    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    subtitle = match_header(frame, args.home, args.away, args.competition)
    footer = "[a] radar  [v] görünüm  [s] oyuncu değiştir  [h] bağır  [t] taktik  [+/-] hız"
    legend = [
        (args.home, outfield_colour(frame, 0)),
        (args.away, outfield_colour(frame, 1)),
    ]
    braille_legend = [
        (args.home, readable(outfield_colour(frame, 0), 0.58)),
        (args.away, readable(outfield_colour(frame, 1), 0.58)),
    ]

    chosen = set(args.only or ["pixel", "braille", "iso"])

    if "pixel" in chosen:
        art = render_pixel(frame, args.pixel_width, args.home, args.away)
        art = art.resize((art.width * 9, art.height * 9), Image.Resampling.NEAREST)
        img = terminal_chrome(
            art,
            "fm match  ·  canlı radar",
            subtitle,
            footer,
            legend,
            footer_lines=[f"stil: pixel / half-block   ·   {frame.state.get('Commentary','')}"],
        )
        save_png(img, out / "01-pixel.png")
        (out / "01-pixel.ansi").write_text(pixel_ansi(frame, args.pixel_width, args.home, args.away))
        print("pixel", img.size)

    if "braille" in chosen:
        art, ansi = render_braille(frame, args.braille_width, args.home, args.away)
        img = terminal_chrome(
            art,
            "fm match  ·  canlı radar",
            subtitle,
            footer,
            braille_legend,
            footer_lines=[f"stil: braille / 2×4 nokta   ·   {frame.state.get('Commentary','')}"],
        )
        save_png(img, out / "02-braille.png")
        (out / "02-braille.ansi").write_text(ansi)
        print("braille", img.size)

    if "iso" in chosen:
        art = render_isometric(frame, args.home, args.away, args.competition)
        save_png(art, out / "03-isometric.png")
        print("iso", art.size)


if __name__ == "__main__":
    main()
