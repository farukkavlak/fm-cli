"""
Terminal emulation: what a picture looks like when it is made of cells.

    CharGrid    one glyph per cell, with a foreground and a background colour
    braille     2x4 dots per cell, one colour per cell (the terminal's real limit)
    halfblock   two pixels per cell stacked vertically (the '▀' trick)
"""

from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw

from .common import menlo


class CharGrid:
    def __init__(self, cols: int, rows: int, bg=(0, 0, 0)):
        self.cols, self.rows = cols, rows
        self.ch = [[" "] * cols for _ in range(rows)]
        self.fg = [[None] * cols for _ in range(rows)]
        self.bg = [[bg] * cols for _ in range(rows)]
        self.bold = [[False] * cols for _ in range(rows)]
        self.prio = [[0.0] * cols for _ in range(rows)]

    def put(self, c: int, r: int, ch: str, fg=None, bg=None, prio: float = 1.0, bold: bool = False):
        if 0 <= c < self.cols and 0 <= r < self.rows and prio >= self.prio[r][c]:
            self.ch[r][c] = ch
            if fg is not None:
                self.fg[r][c] = fg
            if bg is not None:
                self.bg[r][c] = bg
            self.bold[r][c] = bold
            self.prio[r][c] = prio

    def text(self, c: int, r: int, s: str, fg, bg=None, prio: float = 5.0, bold: bool = False):
        for i, ch in enumerate(s):
            self.put(c + i, r, ch, fg, bg, prio, bold)

    def paint_bg(self, c: int, r: int, bg):
        if 0 <= c < self.cols and 0 <= r < self.rows:
            self.bg[r][c] = bg

    def render(self, size: tuple[int, int], font_scale: float = 1.0, glow: bool = False) -> Image.Image:
        w, h = size
        cw, chh = w / self.cols, h / self.rows
        img = Image.new("RGB", size)
        d = ImageDraw.Draw(img)
        for r in range(self.rows):
            for c in range(self.cols):
                d.rectangle([round(c * cw), round(r * chh), round((c + 1) * cw), round((r + 1) * chh)], fill=self.bg[r][c])
        fsize = max(6, round(min(chh * 0.86, cw / 0.6) * font_scale))
        f, fb = menlo(fsize), menlo(fsize, True)
        for r in range(self.rows):
            for c in range(self.cols):
                ch = self.ch[r][c]
                if ch == " ":
                    continue
                d.text(((c + 0.5) * cw, (r + 0.55) * chh), ch, font=fb if self.bold[r][c] else f,
                       fill=self.fg[r][c] or (220, 220, 220), anchor="mm")
        if glow:
            from .common import glow as _glow
            img = _glow(img, radius=2.2, gain=0.55, threshold=120)
        return img


# ---------------------------------------------------------------- braille


def braille(colours: np.ndarray, lit: np.ndarray, size: tuple[int, int], cols: int, rows: int,
            bg=(0, 0, 0), dot: float = 0.36, per_dot_colour: bool = False, ss: int = 3) -> Image.Image:
    """
    colours: (rows*4, cols*2, 3) colour per dot; lit: (rows*4, cols*2) priority, 0 = off.
    A terminal cell has one foreground colour, so unless per_dot_colour is set each cell
    takes the colour of its highest-priority dot.
    """
    w, h = size
    cw, chh = w * ss / cols, h * ss / rows
    img = Image.new("RGB", (w * ss, h * ss), bg)
    d = ImageDraw.Draw(img)
    r_dot = min(cw / 2, chh / 4) * dot
    for r in range(rows):
        for c in range(cols):
            block = lit[r * 4:(r + 1) * 4, c * 2:(c + 1) * 2]
            if not block.any():
                continue
            k = np.unravel_index(np.argmax(block), block.shape)
            if block.max() >= 1.0:
                # a figure owns this cell: texture dots in it would take the figure's colour
                block = np.where(block < 0.5, 0, block)
            cell_col = tuple(int(v) for v in colours[r * 4 + k[0], c * 2 + k[1]])
            for dy in range(4):
                for dx in range(2):
                    if block[dy, dx] <= 0:
                        continue
                    col = tuple(int(v) for v in colours[r * 4 + dy, c * 2 + dx]) if per_dot_colour else cell_col
                    x = c * cw + (dx + 0.5) * cw / 2
                    y = r * chh + (dy + 0.5) * chh / 4
                    d.ellipse([x - r_dot, y - r_dot, x + r_dot, y + r_dot], fill=col)
    return img.resize(size, Image.Resampling.LANCZOS)


# ---------------------------------------------------------------- half blocks


def halfblock(px: Image.Image, size: tuple[int, int], gap: float = 0.0) -> Image.Image:
    """Show a small picture as half-block cells: each source pixel becomes a crisp rectangle."""
    out = px.resize(size, Image.Resampling.NEAREST)
    if gap <= 0:
        return out
    # a hairline between rows of cells, as a terminal with line spacing would show
    a = np.asarray(out).astype(np.float32)
    rows = px.height // 2
    ch = size[1] / rows
    for r in range(1, rows):
        y = round(r * ch)
        if 0 <= y < size[1]:
            a[y] *= 1 - gap
    return Image.fromarray(a.astype(np.uint8))


def quantize(img: Image.Image, colours: int = 48) -> Image.Image:
    return img.quantize(colours, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")


def pixelate(img: Image.Image, cells: tuple[int, int], colours: int | None = 64) -> Image.Image:
    """Average down to cells (cols, rows*2) and show as crisp half-block pixels."""
    small = img.resize(cells, Image.Resampling.BOX)
    if colours:
        small = quantize(small, colours)
    return small
