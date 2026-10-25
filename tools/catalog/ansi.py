"""
Real terminal output for the catalog: the same cells the PNGs are drawn from, as ANSI text.

The terminal-native styles become 24-bit colour escapes (characters, braille, '▀' half blocks).
The image styles are sent as an inline picture (iTerm2's OSC 1337, which VS Code, iTerm2 and
WezTerm show), sized in terminal columns.
"""

from __future__ import annotations

import base64
import io

import numpy as np
from PIL import Image

from . import styles, term
from .common import Match, load
from .styles import ART

CELL_ASPECT = 2.05  # a terminal cell is about twice as tall as it is wide
RESET = "\x1b[0m"


def fg(c) -> str:
    return f"\x1b[38;2;{c[0]};{c[1]};{c[2]}m"


def bg(c) -> str:
    return f"\x1b[48;2;{c[0]};{c[1]};{c[2]}m"


def grid_ansi(g: term.CharGrid) -> str:
    out = []
    for r in range(g.rows):
        line, state = [], None
        for c in range(g.cols):
            cell = (g.fg[r][c] or (220, 220, 220), g.bg[r][c], g.bold[r][c])
            if cell != state:
                line.append(RESET + ("\x1b[1m" if cell[2] else "") + fg(cell[0]) + bg(cell[1]))
                state = cell
            line.append(g.ch[r][c])
        out.append("".join(line) + RESET)
    return "\n".join(out)


# braille bit for each dot (dy, dx) in a 2x4 cell
BRAILLE_BITS = {(0, 0): 0x01, (1, 0): 0x02, (2, 0): 0x04, (0, 1): 0x08, (1, 1): 0x10, (2, 1): 0x20, (3, 0): 0x40, (3, 1): 0x80}


def braille_ansi(colours: np.ndarray, lit: np.ndarray, cols: int, rows: int, background) -> str:
    out = []
    for r in range(rows):
        line, state = [bg(background)], None
        for c in range(cols):
            block = lit[r * 4:(r + 1) * 4, c * 2:(c + 1) * 2]
            if not block.any():
                line.append(" ")
                continue
            if block.max() >= 1.0:
                block = np.where(block < 0.5, 0, block)
            k = np.unravel_index(np.argmax(block), block.shape)
            col = tuple(int(v) for v in colours[r * 4 + k[0], c * 2 + k[1]])
            bits = sum(BRAILLE_BITS[(dy, dx)] for dy in range(4) for dx in range(2) if block[dy, dx] > 0)
            if col != state:
                line.append(fg(col))
                state = col
            line.append(chr(0x2800 + bits))
        out.append("".join(line) + RESET)
    return "\n".join(out)


def halfblock_ansi(px: Image.Image) -> str:
    a = np.asarray(px.convert("RGB"))
    h = a.shape[0] - a.shape[0] % 2
    out = []
    for y in range(0, h, 2):
        line, state = [], None
        for x in range(a.shape[1]):
            top, bottom = tuple(int(v) for v in a[y, x]), tuple(int(v) for v in a[y + 1, x])
            if (top, bottom) != state:
                line.append(fg(top) + bg(bottom))
                state = (top, bottom)
            line.append("▀")
        out.append("".join(line) + RESET)
    return "\n".join(out)


# ---------------------------------------------------------------- sub-cell blocks

QUADRANTS = " ▘▝▀▖▌▞▛▗▚▐▜▄▙▟█"  # index = UL 1 | UR 2 | LL 4 | LR 8


def _sextant(v: int) -> str:
    """Bits row-major (r0c0=1, r0c1=2, r1c0=4 ... r2c1=32) to a character."""
    special = {0: " ", 63: "█", 21: "▌", 42: "▐"}
    if v in special:
        return special[v]
    i = v - 1 - (1 if v > 21 else 0) - (1 if v > 42 else 0)
    return chr(0x1FB00 + i)


MODES = {  # sub-pixels per cell (wide, tall)
    "half": (1, 2),
    "quad": (2, 2),
    "sext": (2, 3),
}


def _partitions(n: int) -> np.ndarray:
    """Every way to split n sub-pixels into foreground/background (first one always background)."""
    return np.array([[(m >> i) & 1 for i in range(n)] for m in range(0, 1 << (n - 1))], bool)[:, ::-1] if n > 1 else np.zeros((1, 1), bool)


def crisp(src: Image.Image, size) -> Image.Image:
    """Shrink without the mush: Lanczos, an unsharp mask, a touch more colour and contrast."""
    from PIL import ImageEnhance, ImageFilter
    small = src.convert("RGB").resize(size, Image.Resampling.LANCZOS)
    small = small.filter(ImageFilter.UnsharpMask(radius=1.6, percent=140, threshold=2))
    small = ImageEnhance.Color(small).enhance(1.18)
    return ImageEnhance.Contrast(small).enhance(1.08)


def blocks(src: Image.Image, cols: int, rows: int, mode: str, sharpen: bool = True):
    """
    Two colours per cell, the best split of its sub-pixels between them.
    Returns (ansi text, preview image as a terminal would draw it).
    """
    sx, sy = MODES[mode]
    if sharpen:
        src = crisp(src, (cols * sx * 2, rows * sy * 2))
    small = np.asarray(src.convert("RGB").resize((cols * sx, rows * sy), Image.Resampling.BOX)).astype(np.float32)
    cells = small.reshape(rows, sy, cols, sx, 3).transpose(0, 2, 1, 3, 4).reshape(rows, cols, sy * sx, 3)
    n = sx * sy
    if mode == "half":
        masks = np.array([[True, False]])  # top is foreground
    else:
        masks = _partitions(n)[1:] if n > 1 else _partitions(n)
        masks = np.concatenate([np.zeros((1, n), bool), masks])  # allow a flat cell
    best_err = np.full((rows, cols), np.inf, np.float32)
    best = np.zeros((rows, cols), int)
    fgc = np.zeros((rows, cols, 3), np.float32)
    bgc = np.zeros((rows, cols, 3), np.float32)
    for k, mk in enumerate(masks):
        w = mk.astype(np.float32)[None, None, :, None]
        nf = w.sum(axis=2)
        nb = (1 - w).sum(axis=2)
        f = (cells * w).sum(axis=2) / np.maximum(nf, 1)
        b = (cells * (1 - w)).sum(axis=2) / np.maximum(nb, 1)
        err = (((cells - f[:, :, None]) ** 2) * w).sum(axis=(2, 3)) + (((cells - b[:, :, None]) ** 2) * (1 - w)).sum(axis=(2, 3))
        better = err < best_err
        best_err = np.where(better, err, best_err)
        best = np.where(better, k, best)
        fgc = np.where(better[..., None], f, fgc)
        bgc = np.where(better[..., None], b, bgc)
    fgc, bgc = fgc.round().astype(int), bgc.round().astype(int)
    # text
    out = []
    for r in range(rows):
        line, state = [], None
        for c in range(cols):
            mk = masks[best[r, c]]
            if mode == "half":
                ch = "▀"
            elif mode == "quad":
                ch = QUADRANTS[sum(1 << i for i in range(4) if mk[i])]
            else:
                ch = _sextant(sum(1 << i for i in range(6) if mk[i]))
            f, b = tuple(fgc[r, c]), tuple(bgc[r, c])
            if ch == " ":
                f = b
            if (f, b) != state:
                line.append(fg(f) + bg(b))
                state = (f, b)
            line.append(ch)
        out.append("".join(line) + RESET)
    # preview: what the terminal shows, at 8x16 pixel cells
    pw, ph = 8, 16
    sub = np.zeros((rows, cols, n, 3), np.uint8)
    chosen = masks[best]  # rows, cols, n
    sub[:] = np.where(chosen[..., None], fgc[:, :, None, :], bgc[:, :, None, :])
    grid = sub.reshape(rows, cols, sy, sx, 3).transpose(0, 2, 1, 3, 4).reshape(rows * sy, cols * sx, 3)
    preview = Image.fromarray(grid).resize((cols * pw, rows * ph), Image.Resampling.NEAREST)
    return "\n".join(out), preview


def inline_image(img: Image.Image, cols: int) -> str:
    buf = io.BytesIO()
    img.save(buf, "PNG")
    data = base64.b64encode(buf.getvalue()).decode()
    return f"\x1b]1337;File=inline=1;width={cols};preserveAspectRatio=1;size={len(buf.getvalue())}:{data}\x07"


# ---------------------------------------------------------------- per style


def _pitch_rows(cols: int, margin: int = 2) -> int:
    """Rows that keep a 105x68 pitch in proportion for `cols` character columns."""
    return round((cols - 2 * margin) * 68 / 105 / CELL_ASPECT) + 2


def _halfblock_px(cols: int) -> tuple[int, int]:
    h = round(cols * ART[1] / ART[0])
    return cols, h - h % 2


def scene_src(number: str, m: Match, goal: Match, size):
    """The 3D styles drawn for a terminal: bigger figures and thicker lines than the PNG."""
    if number == "05":
        return styles.isometric_src(m, size, zoom=1.38, scale=3.8, line_w=0.42, target=(57, 29, 0))
    if number == "06":
        return styles.raycast_src(m, size, scale=1.5, line_w=0.32, crowd_calm=0.8, eye=(78, 45, 2.0), target=(88, 29, 1.3), fov=38)
    return styles.voxel(goal, size, figure_size=0.72, zoom=1.12)


def render(number: str, m: Match, goal: Match, cols: int, mode: str = "sext") -> str:
    """Terminal output for one card, by its catalog number."""
    if number == "01":
        return grid_ansi(styles.ascii_classic_grid(m, cols, _pitch_rows(cols), CELL_ASPECT))
    if number == "02":
        return grid_ansi(styles.ascii_colour_grid(m, cols, _pitch_rows(cols), CELL_ASPECT))
    if number == "03":
        w, h = _halfblock_px(cols)
        return halfblock_ansi(styles.pixel_canvas(m, w, h))
    if number == "04":
        rows = round(cols * 2 * 68 / 105 / 4 * 1.12)
        col, lit = styles.braille_dots(m, cols, rows)
        return braille_ansi(col, lit, cols, rows, styles.BRAILLE_BG)
    if number in ("05", "06", "07") and mode != "image":
        rows = round(cols * ART[1] / ART[0] * 8 / 16)
        text, _ = blocks(scene_src(number, m, goal, (cols * 8, rows * 16)), cols, rows, mode)
        return text
    # image styles: the PNG itself, inline
    from .layout import CARDS
    card = next(c for c in CARDS if c.number == number)
    return inline_image(card.render(goal if card.moment == "goal" else m), cols)


CELL_PX = (8, 16)  # preview cell size, close to a 13pt monospace terminal


def preview(number: str, m: Match, goal: Match, cols: int, mode: str = "sext") -> Image.Image:
    """What a terminal `cols` wide shows for this card, as a picture."""
    cw, ch = CELL_PX
    if number in ("01", "02"):
        fn = styles.ascii_classic_grid if number == "01" else styles.ascii_colour_grid
        g = fn(m, cols, _pitch_rows(cols), CELL_ASPECT)
        return g.render((cols * cw, g.rows * ch))
    if number == "03":
        w, h = _halfblock_px(cols)
        return styles.pixel_canvas(m, w, h).resize((cols * cw, h // 2 * ch), Image.Resampling.NEAREST)
    if number == "04":
        rows = round(cols * 2 * 68 / 105 / 4 * 1.12)
        col, lit = styles.braille_dots(m, cols, rows)
        return term.braille(col, lit, (cols * cw, rows * ch), cols, rows, bg=styles.BRAILLE_BG, dot=0.42)
    if number in ("05", "06", "07") and mode != "image":
        rows = round(cols * ART[1] / ART[0] * 8 / 16)
        _, img = blocks(scene_src(number, m, goal, (cols * cw, rows * ch)), cols, rows, mode)
        return img
    from .layout import CARDS
    card = next(c for c in CARDS if c.number == number)
    return card.render(goal if card.moment == "goal" else m).resize((cols * cw, round(cols * cw * ART[1] / ART[0])), Image.Resampling.LANCZOS)


def build(numbers: list[str], cols: int, mode: str = "sext") -> dict[str, str]:
    m, goal = load(goal=False), load(goal=True)
    return {n: render(n, m, goal, cols, mode) for n in numbers}
